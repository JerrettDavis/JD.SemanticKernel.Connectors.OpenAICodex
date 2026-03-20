using System.Text.Json;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
    private static readonly Regex s_cliAuthCredentialsStorePattern = new(
        @"(?im)^\s*cli_auth_credentials_store\s*=\s*[""'](?<mode>file|keyring|auto)[""']",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));
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

        // 3. Managed credentials (auth.json/keyring) in ChatGPT mode should take
        // precedence over environment API keys to avoid stale/billing-limited key
        // shadowing valid OAuth subscription auth.
        var managedCreds = await GetCredentialsAsync(ct).ConfigureAwait(false);
        var envApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (ShouldPreferManagedCredentials(managedCreds, envApiKey))
        {
            _logger.LogDebug("Using managed ChatGPT credentials from Codex auth storage");
            return await ExtractFromCredentialsAsync(managedCreds!, ct, preferTokens: true)
                .ConfigureAwait(false);
        }

        // 4. OPENAI_API_KEY env var
        if (!string.IsNullOrWhiteSpace(envApiKey))
        {
            _logger.LogDebug("Using OPENAI_API_KEY environment variable");
            return envApiKey!;
        }

        // 5. CODEX_TOKEN env var
        var envToken = Environment.GetEnvironmentVariable("CODEX_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            _logger.LogDebug("Using CODEX_TOKEN environment variable");
            return await ExchangeForApiKeyOrReturnAsync(envToken!, ct)
                .ConfigureAwait(false);
        }

        // 6. Credentials storage fallback
        if (managedCreds is not null)
        {
            var preferTokens = ShouldPreferManagedCredentials(managedCreds, envApiKey: null);
            return await ExtractFromCredentialsAsync(managedCreds, ct, preferTokens)
                .ConfigureAwait(false);
        }

        throw new CodexSessionException(
            "No Codex credentials found. " +
            "Run 'codex login' or set OPENAI_API_KEY.");
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

            var file = await ReadCredentialsAsync(ct).ConfigureAwait(false);

            // If token is expired and we have a refresh token, try refreshing
            if (file is not null && file.IsExpired
                && !string.IsNullOrWhiteSpace(file.EffectiveRefreshToken)
                && _options.AutoRefreshTokens)
            {
                _logger.LogInformation("Access token expired, attempting refresh");
                var refreshed = await RefreshTokenAsync(file.EffectiveRefreshToken!, ct)
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

    private static bool ShouldPreferManagedCredentials(
        CodexCredentialsFile? creds,
        string? envApiKey)
    {
        if (creds is null)
            return false;

        var token = !string.IsNullOrWhiteSpace(creds.EffectiveAccessToken)
            ? creds.EffectiveAccessToken
            : creds.EffectiveIdToken;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var apiKeyWouldShadow = !string.IsNullOrWhiteSpace(creds.OpenAIApiKey) ||
                                !string.IsNullOrWhiteSpace(envApiKey);
        if (!apiKeyWouldShadow)
            return false;

        return IsChatGptAuthMode(creds.AuthMode) || LooksLikeOAuthJwt(token!);
    }

    private async Task<string> ExtractFromCredentialsAsync(
        CodexCredentialsFile creds,
        CancellationToken ct,
        bool preferTokens = false)
    {
        if (preferTokens && !creds.IsExpired)
        {
            var accessTokenPreferred = creds.EffectiveAccessToken;
            if (!string.IsNullOrWhiteSpace(accessTokenPreferred))
            {
                _logger.LogInformation("Using preferred Codex access token directly");
                return accessTokenPreferred!;
            }

            var idTokenPreferred = creds.EffectiveIdToken;
            if (!string.IsNullOrWhiteSpace(idTokenPreferred))
            {
                var apiKeyPreferred = await ExchangeForApiKeyAsync(idTokenPreferred!, ct)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(apiKeyPreferred))
                {
                    _logger.LogInformation("Exchanged preferred Codex id_token for API key");
                    return apiKeyPreferred!;
                }
            }
        }

        // If the file has an explicit API key at root level, use it
        if (!string.IsNullOrWhiteSpace(creds.OpenAIApiKey))
        {
            _logger.LogInformation("Using OPENAI_API_KEY from credentials file");
            return creds.OpenAIApiKey!;
        }

        if (creds.IsExpired)
            throw new CodexSessionException(
                $"Codex session expired at {creds.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC. " +
                "Run 'codex login' to refresh your session.");

        // Prefer direct OAuth access tokens over token exchange where possible.
        var accessToken = creds.EffectiveAccessToken;
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogInformation("Using Codex access token directly");
            return accessToken!;
        }

        // If we have an id_token (flat or nested), exchange it for an API key.
        var idToken = creds.EffectiveIdToken;
        if (!string.IsNullOrWhiteSpace(idToken))
        {
            var apiKey = await ExchangeForApiKeyAsync(idToken!, ct)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogInformation("Exchanged Codex id_token for API key");
                return apiKey!;
            }
        }

        throw new CodexSessionException(
            "Codex credentials file is present but contains no usable token. " +
            "Run 'codex login' to obtain a new token.");
    }

    private static bool IsChatGptAuthMode(string? authMode) =>
        !string.IsNullOrWhiteSpace(authMode) &&
        authMode.Contains("chatgpt", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeOAuthJwt(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith("eyJ", StringComparison.Ordinal))
            return false;

        var firstDot = token.IndexOf('.');
        if (firstDot <= 0)
            return false;

        var secondDot = token.IndexOf('.', firstDot + 1);
        return secondDot > firstDot + 1 && secondDot < token.Length - 1;
    }

    private async Task<string> ExchangeForApiKeyOrReturnAsync(string token, CancellationToken ct)
    {
        // If it looks like an API key already (sk-...), use it directly
        if (token.StartsWith("sk-", StringComparison.Ordinal))
            return token;
        // ChatGPT OAuth access tokens are valid bearer tokens for API calls.
        // Prefer direct usage to avoid minting account-scoped API keys that can
        // trigger false insufficient_quota paths.
        if (LooksLikeOAuthJwt(token))
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

    internal async Task<CodexCredentialsFile?> ReadCredentialsAsync(CancellationToken ct)
    {
        var storeMode = ResolveAuthStoreMode();
        var path = ResolveCredentialsPath();

        _logger.LogDebug("Reading credentials from auth storage ({StoreMode})", storeMode);

        switch (storeMode)
        {
            case AuthStoreMode.Keyring:
                {
                    var keyringCreds = TryReadCredentialsFromKeyring();
                    return keyringCreds;
                }
            case AuthStoreMode.Auto:
                {
                    var keyringCreds = TryReadCredentialsFromKeyring();
                    if (keyringCreds is not null)
                        return keyringCreds;
                    return await ReadCredentialsFileByPathAsync(path, ct).ConfigureAwait(false);
                }
            case AuthStoreMode.File:
            default:
                return await ReadCredentialsFileByPathAsync(path, ct).ConfigureAwait(false);
        }
    }

    internal async Task<CodexCredentialsFile?> ReadCredentialsFileAsync(CancellationToken ct) =>
        await ReadCredentialsFileByPathAsync(ResolveCredentialsPath(), ct).ConfigureAwait(false);

    private async Task<CodexCredentialsFile?> ReadCredentialsFileByPathAsync(
        string path,
        CancellationToken ct)
    {
        _logger.LogDebug("Reading credentials file from {Path}", path);

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

    private AuthStoreMode ResolveAuthStoreMode()
    {
        var codexHome = ResolveCodexHomePath();
        if (string.IsNullOrWhiteSpace(codexHome))
            return AuthStoreMode.File;

        var configPath = Path.Combine(codexHome, "config.toml");
        if (!File.Exists(configPath))
            return AuthStoreMode.File;

        try
        {
            var config = File.ReadAllText(configPath);
            var match = s_cliAuthCredentialsStorePattern.Match(config);
            if (!match.Success)
                return AuthStoreMode.File;

            var mode = match.Groups["mode"].Value;
            return mode.ToUpperInvariant() switch
            {
                "KEYRING" => AuthStoreMode.Keyring,
                "AUTO" => AuthStoreMode.Auto,
                _ => AuthStoreMode.File
            };
        }
        catch (IOException)
        {
            return AuthStoreMode.File;
        }
        catch (UnauthorizedAccessException)
        {
            return AuthStoreMode.File;
        }
    }

    private CodexCredentialsFile? TryReadCredentialsFromKeyring()
    {
#if NET5_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
#else
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
#endif
            return null;

        var codexHome = ResolveCodexHomePath();
        if (string.IsNullOrWhiteSpace(codexHome))
            return null;

        var account = ComputeCodexKeyringAccountKey(codexHome);
        if (string.IsNullOrWhiteSpace(account))
            return null;

        if (!TryReadWindowsCodexKeyringJson(account, out var json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<CodexCredentialsFile>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string ResolveCodexHomePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.CredentialsPath))
        {
            var dir = Path.GetDirectoryName(_options.CredentialsPath);
            if (!string.IsNullOrWhiteSpace(dir))
                return dir;
        }

        var envCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(envCodexHome))
            return envCodexHome;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".codex");
    }

    internal static string ComputeCodexKeyringAccountKey(string codexHomePath)
    {
        var normalized = codexHomePath;
        try
        {
            normalized = Path.GetFullPath(codexHomePath);
        }
        catch (Exception ex) when (
            ex is NotSupportedException or
            ArgumentException or
            PathTooLongException or
            UnauthorizedAccessException or
            IOException)
        {
            // Keep original path if normalization fails.
        }

        var bytes = HashSha256(System.Text.Encoding.UTF8.GetBytes(normalized));
        var hex = ToLowerHex(bytes);
        var shortHex = hex.Length >= 16 ? hex.Substring(0, 16) : hex;
        return $"cli|{shortHex}";
    }

    private static bool TryReadWindowsCodexKeyringJson(string account, out string json)
    {
        json = string.Empty;
        const string service = "Codex Auth";

        var targets = new[]
        {
            $"{service}:{account}",
            $"{service}/{account}",
            account,
            service
        };

        foreach (var target in targets)
        {
            if (TryCredReadJson(target, out json))
                return true;
        }

        return TryCredEnumerateJson(account, out json);
    }

    private static bool TryCredReadJson(string targetName, out string json)
    {
        json = string.Empty;
        if (!CredRead(targetName, CRED_TYPE_GENERIC, 0, out var credPtr) || credPtr == IntPtr.Zero)
            return false;

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            var payload = CredentialBlobToString(
                credential.CredentialBlob,
                credential.CredentialBlobSize);
            if (!string.IsNullOrWhiteSpace(payload) &&
                StartsWithOpenBrace(payload.TrimStart()))
            {
                json = payload;
                return true;
            }
        }
        finally
        {
            CredFree(credPtr);
        }

        return false;
    }

    private static bool TryCredEnumerateJson(string account, out string json)
    {
        json = string.Empty;
        if (!CredEnumerate(null, 0, out var count, out var credsPtr) || credsPtr == IntPtr.Zero)
            return false;

        try
        {
            for (var i = 0; i < count; i++)
            {
                var credPtr = Marshal.ReadIntPtr(credsPtr, i * IntPtr.Size);
                if (credPtr == IntPtr.Zero)
                    continue;

                var credential = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                if (credential.Type != CRED_TYPE_GENERIC)
                    continue;

                var target = Marshal.PtrToStringUni(credential.TargetName) ?? string.Empty;
                if (!target.Contains("Codex", StringComparison.OrdinalIgnoreCase) &&
                    !target.Contains(account, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var payload = CredentialBlobToString(
                    credential.CredentialBlob,
                    credential.CredentialBlobSize);
                if (string.IsNullOrWhiteSpace(payload))
                    continue;

                var trimmed = payload.TrimStart();
                if (!StartsWithOpenBrace(trimmed))
                    continue;

                json = payload;
                return true;
            }
        }
        finally
        {
            CredFree(credsPtr);
        }

        return false;
    }

    private static string CredentialBlobToString(IntPtr blobPtr, uint blobSize)
    {
        if (blobPtr == IntPtr.Zero || blobSize == 0)
            return string.Empty;

        var bytes = new byte[blobSize];
        Marshal.Copy(blobPtr, bytes, 0, (int)blobSize);

        var utf8 = System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        if (StartsWithOpenBrace(utf8))
            return utf8;

        if (bytes.Length % 2 == 0)
        {
            var utf16 = System.Text.Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            if (StartsWithOpenBrace(utf16))
                return utf16;
        }

        return utf8;
    }

    private static byte[] HashSha256(byte[] data)
    {
#if NET5_0_OR_GREATER
        return SHA256.HashData(data);
#else
        using var sha = SHA256.Create();
        return sha.ComputeHash(data);
#endif
    }

    private static string ToLowerHex(byte[] bytes)
    {
        var chars = new char[bytes.Length * 2];
        var i = 0;

        foreach (var b in bytes)
        {
            chars[i++] = ToHexNibble(b >> 4);
            chars[i++] = ToHexNibble(b & 0xF);
        }

        return new string(chars);
    }

    private static char ToHexNibble(int value) =>
        (char)(value < 10 ? '0' + value : 'a' + (value - 10));

    private static bool StartsWithOpenBrace(string value)
    {
#if NETSTANDARD2_0
        return value.StartsWith("{", StringComparison.Ordinal);
#else
        return value.StartsWith('{');
#endif
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
            : Path.Combine(ResolveCodexHomePath(), "auth.json");

    private enum AuthStoreMode
    {
        File,
        Keyring,
        Auto
    }

    private const uint CRED_TYPE_GENERIC = 1;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint flags,
        out IntPtr credentialPtr);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredEnumerate(
        string? filter,
        uint flags,
        out uint count,
        out IntPtr credentialsPtr);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    /// <inheritdoc/>
    public void Dispose() => _cacheLock.Dispose();
}
