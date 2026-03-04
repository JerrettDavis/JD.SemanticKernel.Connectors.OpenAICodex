using System.Text.Json.Serialization;

namespace JD.SemanticKernel.Connectors.OpenAICodex;

/// <summary>Maps the structure of <c>~/.codex/auth.json</c>.</summary>
/// <remarks>
/// Supports both flat credential layout and Codex CLI's nested format:
/// <code>
/// { "OPENAI_API_KEY": "sk-...", "tokens": { "access_token": "...", "id_token": "...", "refresh_token": "..." } }
/// </code>
/// When tokens are nested under a <c>tokens</c> property, values are
/// promoted to top-level via <see cref="EffectiveAccessToken"/> etc.
/// </remarks>
public sealed record CodexCredentialsFile
{
    /// <summary>The OAuth access token (flat layout).</summary>
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    /// <summary>The OAuth ID token — JWT (flat layout).</summary>
    [JsonPropertyName("id_token")]
    public string? IdToken { get; init; }

    /// <summary>The refresh token for obtaining new access tokens (flat layout).</summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    /// <summary>Unix epoch seconds at which the access token expires.</summary>
    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; init; }

    /// <summary>The token type (typically <c>"Bearer"</c>).</summary>
    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; }

    /// <summary>
    /// An explicit API key stored at the root of the credentials file
    /// (used by newer Codex CLI versions in the <c>OPENAI_API_KEY</c> field).
    /// </summary>
    [JsonPropertyName("OPENAI_API_KEY")]
    public string? OpenAIApiKey { get; init; }

    /// <summary>
    /// Nested tokens object used by Codex CLI (v0.1+) to store OAuth tokens.
    /// </summary>
    [JsonPropertyName("tokens")]
    public CodexTokensBlock? Tokens { get; init; }

    // --- Effective (merged) accessors ---

    /// <summary>
    /// Returns the access token from either the flat layout or nested <see cref="Tokens"/> block.
    /// </summary>
    [JsonIgnore]
    public string? EffectiveAccessToken => AccessToken ?? Tokens?.AccessToken;

    /// <summary>
    /// Returns the ID token from either the flat layout or nested <see cref="Tokens"/> block.
    /// </summary>
    [JsonIgnore]
    public string? EffectiveIdToken => IdToken ?? Tokens?.IdToken;

    /// <summary>
    /// Returns the refresh token from either the flat layout or nested <see cref="Tokens"/> block.
    /// </summary>
    [JsonIgnore]
    public string? EffectiveRefreshToken => RefreshToken ?? Tokens?.RefreshToken;

    /// <summary>The expiry time as a <see cref="DateTimeOffset"/>.</summary>
    [JsonIgnore]
    public DateTimeOffset ExpiresAtUtc => ExpiresAt > 0
        ? DateTimeOffset.FromUnixTimeSeconds(ExpiresAt)
        : DateTimeOffset.MaxValue; // no expiry known — treat as not expired

    /// <summary>
    /// <see langword="true"/> if the access token has passed its expiry time
    /// (with a 60-second safety margin for clock skew).
    /// When no expiry is set (<see cref="ExpiresAt"/> == 0), returns <see langword="false"/>.
    /// </summary>
    [JsonIgnore]
    public bool IsExpired => ExpiresAt > 0
        && DateTimeOffset.UtcNow.AddSeconds(60) >= ExpiresAtUtc;
}

/// <summary>
/// Nested token block found in Codex CLI's <c>auth.json</c> under the <c>"tokens"</c> key.
/// </summary>
public sealed record CodexTokensBlock
{
    /// <summary>The OAuth access token.</summary>
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    /// <summary>The OAuth ID token (JWT).</summary>
    [JsonPropertyName("id_token")]
    public string? IdToken { get; init; }

    /// <summary>The refresh token.</summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    /// <summary>The account ID associated with the session.</summary>
    [JsonPropertyName("account_id")]
    public string? AccountId { get; init; }
}
