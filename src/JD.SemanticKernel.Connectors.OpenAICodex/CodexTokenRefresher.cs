using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JD.SemanticKernel.Connectors.OpenAICodex;

/// <summary>
/// Handles OAuth token refresh and token exchange for the OpenAI Codex auth flow.
/// </summary>
public static class CodexTokenRefresher
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Exchanges an OAuth id_token for an OpenAI API key via the token exchange endpoint.
    /// </summary>
    /// <param name="issuer">The OpenAI auth issuer URL (e.g. <c>https://auth.openai.com</c>).</param>
    /// <param name="idToken">The OAuth id_token to exchange.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The API key string, or <see langword="null"/> if the exchange fails.</returns>
    public static async Task<string?> ExchangeForApiKeyAsync(
        string issuer, string idToken, CancellationToken ct = default)
    {
#if NETSTANDARD2_0
        if (issuer is null) throw new ArgumentNullException(nameof(issuer));
        if (idToken is null) throw new ArgumentNullException(nameof(idToken));
#else
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(idToken);
#endif

        try
        {
            using var client = new HttpClient();
            var tokenUrl = $"{issuer.TrimEnd('/')}/oauth/token";

            var content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
                ["subject_token"] = idToken,
                ["subject_token_type"] = "urn:ietf:params:oauth:token-type:id_token",
                ["requested_token_type"] = "openai-api-key"
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
            {
                Content = content
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

#if NETSTANDARD2_0
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
#endif

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("access_token", out var tokenElement))
                return tokenElement.GetString();

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Intentional: best-effort exchange
        catch
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>
    /// Refreshes an expired access token using the refresh token.
    /// </summary>
    /// <param name="issuer">The OpenAI auth issuer URL.</param>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="clientId">The OAuth client ID, or <see langword="null"/> for the default.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new <see cref="CodexCredentialsFile"/>, or <see langword="null"/> on failure.</returns>
    public static async Task<CodexCredentialsFile?> RefreshAsync(
        string issuer, string refreshToken, string? clientId = null, CancellationToken ct = default)
    {
#if NETSTANDARD2_0
        if (issuer is null) throw new ArgumentNullException(nameof(issuer));
        if (refreshToken is null) throw new ArgumentNullException(nameof(refreshToken));
#else
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(refreshToken);
#endif

        try
        {
            using var client = new HttpClient();
            var tokenUrl = $"{issuer.TrimEnd('/')}/oauth/token";

            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            };

            if (!string.IsNullOrWhiteSpace(clientId))
                parameters["client_id"] = clientId!;

            var content = new FormUrlEncodedContent(parameters);

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
            {
                Content = content
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

#if NETSTANDARD2_0
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
#endif

            return JsonSerializer.Deserialize<CodexCredentialsFile>(body, s_jsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Intentional: best-effort refresh
        catch
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
