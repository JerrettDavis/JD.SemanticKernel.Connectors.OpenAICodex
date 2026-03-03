using System.Text.Json;
using JD.SemanticKernel.Connectors.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JD.SemanticKernel.Connectors.OpenAICodex;

/// <summary>
/// Resolves OpenAI API credentials from multiple sources in priority order:
/// <list type="number">
///   <item><description><c>CodexSession:ApiKey</c> in options/configuration</description></item>
///   <item><description><c>CodexSession:AccessToken</c> in options/configuration</description></item>
///   <item><description><c>OPENAI_API_KEY</c> environment variable</description></item>
///   <item><description><c>CODEX_TOKEN</c> environment variable</description></item>
///   <item><description><c>~/.codex/auth.json</c> — Codex CLI local session</description></item>
///   <item><description>Interactive device code login (when enabled)</description></item>
/// </list>
/// </summary>
public sealed class CodexSessionProvider : ISessionProvider, IDisposable
{
    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        WriteIndented = true
    };
    private readonly CodexSessionOptions _options;
    private readonly ILogger<CodexSessionProvider> _logger;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private volatile CodexCredentialsFile? _cached;

    /// <summary>
    /// Pluggable token refresher. Defaults to <see cref="CodexTokenRefresher.RefreshAsync"/>.
    /// Tests can inject a stub.
    /// </summary>
    internal Func<string, string, CancellationToken, Task<CodexCredentialsFile?>>? TokenRefresher { get; set; }

    /// <summary>
    /// Pluggable token exchanger (OAuth token → API key).
    /// Tests can inject a stub.
    /// </summary>
    internal Func<string, string, CancellationToken, Task<string?>>? TokenExchanger { get; set; }

    /// <summary>
    /// Initialises the provider with DI-injected options and logger.
    /// </summary>
    public CodexSessionProvider(
        IOptions<CodexSessionOptions> options,
        ILogger<CodexSessionProvider> logger)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns the best available API key for the current session.
    /// </summary>
    /// <exception cref="CodexSessionException">
    /// Thrown when no valid credential is found.
    /// </exception>
    public async Task<string> GetApiKeyAsync(CancellationToken ct = default)
    {
        // 1. Explicit API key from options
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogDebug("Using explicit API key from options");
            return _options.ApiKey!;
        }

        // 2. Explicit access token from options
        if (!string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            _logger.LogDebug("Using explicit access token from options");
            return await ExchangeForApiKeyOrReturnAsync(_options.AccessToken!, ct)
                .ConfigureAwait(false);
        }

        // 3. OPENAI_API_KEY env var
        var envApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(envApiKey))
        {
            _logger.LogDebug("Using OPENAI_API_KEY environment variable");
            return envApiKey!;
        }

        // 4. CODEX_TOKEN env var
        var envToken = Environment.GetEnvironmentVariable("CODEX_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            _logger.LogDebug("Using CODEX_TOKEN environment variable");
            return await ExchangeForApiKeyOrReturnAsync(envToken!, ct)
                .ConfigureAwait(false);
        }

        // 5. Credentials file
        return await ExtractFromCredentialsFileAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the cached credentials file, or reads it fresh.
    /// </summary>
    public async Task<CodexCredentialsFile?> GetCredentialsAsync(CancellationToken ct = default)
    {
        var snapshot = _cached;
        if (snapshot is not null && !snapshot.IsExpired)
            return snapshot;

        await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached is not null && !_cached.IsExpired)
                return _cached;

            var file = await ReadCredentialsFileAsync(ct).ConfigureAwait(false);

            // If token is expired and we have a refresh token, try refreshing
            if (file is not null && file.IsExpired
                && !string.IsNullOrWhiteSpace(file.RefreshToken)
                && _options.AutoRefreshTokens)
            {
                _logger.LogInformation("Access token expired, attempting refresh");
                var refreshed = await RefreshTokenAsync(file.RefreshToken!, ct)
                    .ConfigureAwait(false);
                if (refreshed is not null)
                {
                    await PersistCredentialsAsync(refreshed, ct).ConfigureAwait(false);
                    file = refreshed;
                }
            }

            _cached = file;
            return _cached;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <inheritdoc/>
    async Task<SessionCredentials> ISessionProvider.GetCredentialsAsync(CancellationToken ct)
    {
        var apiKey = await GetApiKeyAsync(ct).ConfigureAwait(false);
        var creds = _cached;
        var expiresAt = creds?.ExpiresAtUtc;
        return new SessionCredentials(apiKey, expiresAt);
    }

    /// <inheritdoc/>
    public async Task<bool> IsAuthenticatedAsync(CancellationToken ct = default)
    {
        try
        {
            await GetApiKeyAsync(ct).ConfigureAwait(false);
            return true;
        }
#pragma warning disable CA1031 // Intentional: check-don't-throw API
        catch
#pragma warning restore CA1031
        {
            return false;
        }
    }

    private async Task<string> ExtractFromCredentialsFileAsync(CancellationToken ct)
    {
        var creds = await GetCredentialsAsync(ct).ConfigureAwait(false);

        if (creds is null)
            throw new CodexSessionException(
                "No Codex credentials found. " +
                "Install Codex CLI and run 'codex login', or set the OPENAI_API_KEY environment variable.");

        if (creds.IsExpired)
            throw new CodexSessionException(
                $"Codex session expired at {creds.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC. " +
                "Run 'codex login' to refresh your session.");

        // If we have an id_token, exchange it for an API key
        if (!string.IsNullOrWhiteSpace(creds.IdToken))
        {
            var apiKey = await ExchangeForApiKeyAsync(creds.IdToken!, ct)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogInformation("Exchanged Codex id_token for API key");
                return apiKey!;
            }
        }

        // Fall back to access token directly
        if (!string.IsNullOrWhiteSpace(creds.AccessToken))
        {
            _logger.LogInformation("Using Codex access token directly");
            return creds.AccessToken!;
        }

        throw new CodexSessionException(
            "Codex credentials file is present but contains no usable token. " +
            "Run 'codex login' to obtain a new token.");
    }

    private async Task<string> ExchangeForApiKeyOrReturnAsync(string token, CancellationToken ct)
    {
        // If it looks like an API key already (sk-...), use it directly
        if (token.StartsWith("sk-", StringComparison.Ordinal))
            return token;

        var apiKey = await ExchangeForApiKeyAsync(token, ct).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(apiKey) ? apiKey! : token;
    }

    private async Task<string?> ExchangeForApiKeyAsync(string idToken, CancellationToken ct)
    {
        if (TokenExchanger is not null)
            return await TokenExchanger(_options.Issuer, idToken, ct).ConfigureAwait(false);

        return await CodexTokenRefresher.ExchangeForApiKeyAsync(
            _options.Issuer, idToken, ct).ConfigureAwait(false);
    }

    private async Task<CodexCredentialsFile?> RefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        if (TokenRefresher is not null)
            return await TokenRefresher(_options.Issuer, refreshToken, ct).ConfigureAwait(false);

        return await CodexTokenRefresher.RefreshAsync(
            _options.Issuer, refreshToken, _options.ClientId, ct).ConfigureAwait(false);
    }

    internal async Task<CodexCredentialsFile?> ReadCredentialsFileAsync(CancellationToken ct)
    {
        var path = ResolveCredentialsPath();

        _logger.LogDebug("Reading credentials from {Path}", path);

        try
        {
#if NETSTANDARD2_0
            var json = await Task
                .Run(() => File.ReadAllText(path), ct)
                .ConfigureAwait(false);
            return JsonSerializer
                .Deserialize<CodexCredentialsFile>(json);
#else
            await using var stream = File.OpenRead(path);
            return await JsonSerializer
                .DeserializeAsync<CodexCredentialsFile>(
                    stream, cancellationToken: ct)
                .ConfigureAwait(false);
#endif
        }
        catch (FileNotFoundException)
        {
            _logger.LogDebug("Credentials file not found at {Path}", path);
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            _logger.LogDebug("Credentials directory not found for {Path}", path);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Permission denied reading credentials at {Path}", path);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "I/O error reading credentials at {Path}", path);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed JSON in credentials file at {Path}", path);
            return null;
        }
    }

    private async Task PersistCredentialsAsync(CodexCredentialsFile creds, CancellationToken ct)
    {
        var path = ResolveCredentialsPath();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(creds, s_writeOptions);

#if NETSTANDARD2_0
            await Task.Run(() => File.WriteAllText(path, json), ct).ConfigureAwait(false);
#else
            await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
#endif

            _logger.LogDebug("Persisted refreshed credentials to {Path}", path);
        }
#pragma warning disable CA1031 // Intentional: best-effort persistence
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "Failed to persist refreshed credentials to {Path}", path);
        }
    }

    internal string ResolveCredentialsPath() =>
        !string.IsNullOrWhiteSpace(_options.CredentialsPath)
            ? _options.CredentialsPath!
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex", "auth.json");

    /// <inheritdoc/>
    public void Dispose() => _cacheLock.Dispose();
}
