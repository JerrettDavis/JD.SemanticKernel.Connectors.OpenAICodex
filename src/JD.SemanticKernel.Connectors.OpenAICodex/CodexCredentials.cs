using System.Text.Json.Serialization;

namespace JD.SemanticKernel.Connectors.OpenAICodex;

/// <summary>Maps the structure of <c>~/.codex/auth.json</c>.</summary>
public sealed record CodexCredentialsFile
{
    /// <summary>The OAuth access token.</summary>
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    /// <summary>The OAuth ID token (JWT).</summary>
    [JsonPropertyName("id_token")]
    public string? IdToken { get; init; }

    /// <summary>The refresh token for obtaining new access tokens.</summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    /// <summary>Unix epoch seconds at which the access token expires.</summary>
    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; init; }

    /// <summary>The token type (typically <c>"Bearer"</c>).</summary>
    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; }

    /// <summary>The expiry time as a <see cref="DateTimeOffset"/>.</summary>
    [JsonIgnore]
    public DateTimeOffset ExpiresAtUtc => DateTimeOffset.FromUnixTimeSeconds(ExpiresAt);

    /// <summary>
    /// <see langword="true"/> if the access token has passed its expiry time
    /// (with a 60-second safety margin for clock skew).
    /// </summary>
    [JsonIgnore]
    public bool IsExpired => DateTimeOffset.UtcNow.AddSeconds(60) >= ExpiresAtUtc;
}
