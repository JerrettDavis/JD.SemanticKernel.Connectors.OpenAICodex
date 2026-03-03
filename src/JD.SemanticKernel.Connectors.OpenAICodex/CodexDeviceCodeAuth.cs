using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JD.SemanticKernel.Connectors.OpenAICodex;

/// <summary>
/// Result of a device code initiation request.
/// </summary>
public sealed record DeviceCodeResponse
{
    /// <summary>The server-assigned device auth ID.</summary>
    public string DeviceAuthId { get; init; } = string.Empty;

    /// <summary>The user code to enter at the verification URL.</summary>
    public string UserCode { get; init; } = string.Empty;

    /// <summary>The URL where the user should enter the code.</summary>
    public string VerificationUrl { get; init; } = string.Empty;

    /// <summary>Polling interval in seconds.</summary>
    public int Interval { get; init; } = 5;
}

/// <summary>
/// Implements the OAuth 2.0 Device Code flow for OpenAI Codex authentication.
/// Equivalent to <c>codex login</c>.
/// </summary>
public static class CodexDeviceCodeAuth
{
    /// <summary>Default timeout for the device code polling loop.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initiates the device code flow and returns the user code + verification URL.
    /// </summary>
    /// <param name="issuer">The OpenAI auth issuer URL.</param>
    /// <param name="clientId">The OAuth client ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="DeviceCodeResponse"/> with the user code and URL.</returns>
    /// <exception cref="CodexSessionException">Thrown when the initiation request fails.</exception>
    public static async Task<DeviceCodeResponse> RequestUserCodeAsync(
        string issuer, string clientId, CancellationToken ct = default)
    {
#if NETSTANDARD2_0
        if (issuer is null) throw new ArgumentNullException(nameof(issuer));
        if (clientId is null) throw new ArgumentNullException(nameof(clientId));
#else
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(clientId);
#endif

        using var client = new HttpClient();
        var url = $"{issuer.TrimEnd('/')}/api/accounts/deviceauth/usercode";

        var payload = JsonSerializer.Serialize(new { client_id = clientId });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(url, content, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
#if NETSTANDARD2_0
            var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
#endif
            throw new CodexSessionException(
                $"Failed to initiate device code flow (HTTP {(int)response.StatusCode}): {errorBody}");
        }

#if NETSTANDARD2_0
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
#endif

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        return new DeviceCodeResponse
        {
            DeviceAuthId = root.TryGetProperty("device_auth_id", out var id) ? id.GetString() ?? "" : "",
            UserCode = root.TryGetProperty("user_code", out var code) ? code.GetString() ?? "" : "",
            VerificationUrl = $"{issuer.TrimEnd('/')}/codex/device",
            Interval = root.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 5
        };
    }

    /// <summary>
    /// Polls for token completion after the user has entered the device code.
    /// </summary>
    /// <param name="issuer">The OpenAI auth issuer URL.</param>
    /// <param name="deviceCodeResponse">The response from <see cref="RequestUserCodeAsync"/>.</param>
    /// <param name="timeout">Maximum time to poll. Defaults to <see cref="DefaultTimeout"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="CodexCredentialsFile"/> with the obtained tokens.</returns>
    /// <exception cref="CodexSessionException">Thrown on timeout or auth error.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancelled.</exception>
    public static async Task<CodexCredentialsFile> PollForTokenAsync(
        string issuer,
        DeviceCodeResponse deviceCodeResponse,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
#if NETSTANDARD2_0
        if (issuer is null) throw new ArgumentNullException(nameof(issuer));
        if (deviceCodeResponse is null) throw new ArgumentNullException(nameof(deviceCodeResponse));
#else
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(deviceCodeResponse);
#endif

        var deadline = DateTimeOffset.UtcNow + (timeout ?? DefaultTimeout);
        using var client = new HttpClient();
        var url = $"{issuer.TrimEnd('/')}/api/accounts/deviceauth/token";

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            await Task.Delay(
                TimeSpan.FromSeconds(deviceCodeResponse.Interval), ct)
                .ConfigureAwait(false);

            var payload = JsonSerializer.Serialize(new
            {
                device_auth_id = deviceCodeResponse.DeviceAuthId,
                user_code = deviceCodeResponse.UserCode
            });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, content, ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden
                || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // User hasn't entered the code yet — keep polling
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
#if NETSTANDARD2_0
                var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
#endif
                throw new CodexSessionException(
                    $"Device code authentication failed (HTTP {(int)response.StatusCode}): {errorBody}");
            }

            // Success — parse the authorization code and exchange for tokens
#if NETSTANDARD2_0
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
#endif

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // The response may contain tokens directly or an authorization_code to exchange
            if (root.TryGetProperty("access_token", out var accessToken))
            {
                return new CodexCredentialsFile
                {
                    AccessToken = accessToken.GetString(),
                    IdToken = root.TryGetProperty("id_token", out var idTok) ? idTok.GetString() : null,
                    RefreshToken = root.TryGetProperty("refresh_token", out var refTok) ? refTok.GetString() : null,
                    ExpiresAt = root.TryGetProperty("expires_at", out var exp) ? exp.GetInt64() :
                        DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
                };
            }

            // If we got an authorization_code, we need to exchange it
            if (root.TryGetProperty("authorization_code", out var authCode))
            {
                return await ExchangeAuthCodeForTokensAsync(
                    issuer,
                    authCode.GetString()!,
                    root.TryGetProperty("code_verifier", out var cv) ? cv.GetString() : null,
                    ct).ConfigureAwait(false);
            }

            throw new CodexSessionException(
                "Device code response contained neither tokens nor authorization_code.");
        }

        throw new CodexSessionException(
            "Device code authentication timed out. Please try again.");
    }

    /// <summary>
    /// Performs the full device code login flow, from initiation through polling.
    /// </summary>
    /// <param name="issuer">The OpenAI auth issuer URL.</param>
    /// <param name="clientId">The OAuth client ID.</param>
    /// <param name="onUserCode">
    /// Callback invoked with the user code and verification URL.
    /// The implementation should display these to the user.
    /// </param>
    /// <param name="timeout">Maximum polling timeout.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="CodexCredentialsFile"/> with the obtained tokens.</returns>
    public static async Task<CodexCredentialsFile> LoginAsync(
        string issuer,
        string clientId,
        Action<string, string> onUserCode,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
#if NETSTANDARD2_0
        if (onUserCode is null) throw new ArgumentNullException(nameof(onUserCode));
#else
        ArgumentNullException.ThrowIfNull(onUserCode);
#endif

        var deviceCode = await RequestUserCodeAsync(issuer, clientId, ct)
            .ConfigureAwait(false);

        onUserCode(deviceCode.UserCode, deviceCode.VerificationUrl);

        return await PollForTokenAsync(issuer, deviceCode, timeout, ct)
            .ConfigureAwait(false);
    }

    private static async Task<CodexCredentialsFile> ExchangeAuthCodeForTokensAsync(
        string issuer,
        string authorizationCode,
        string? codeVerifier,
        CancellationToken ct)
    {
        using var client = new HttpClient();
        var url = $"{issuer.TrimEnd('/')}/oauth/token";

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode
        };

        if (!string.IsNullOrWhiteSpace(codeVerifier))
            parameters["code_verifier"] = codeVerifier!;

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await client.PostAsync(url, content, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
#if NETSTANDARD2_0
            var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
#endif
            throw new CodexSessionException(
                $"Token exchange failed (HTTP {(int)response.StatusCode}): {errorBody}");
        }

#if NETSTANDARD2_0
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
#endif

        var result = JsonSerializer.Deserialize<CodexCredentialsFile>(body);
        return result ?? throw new CodexSessionException("Token exchange returned empty response.");
    }
}
