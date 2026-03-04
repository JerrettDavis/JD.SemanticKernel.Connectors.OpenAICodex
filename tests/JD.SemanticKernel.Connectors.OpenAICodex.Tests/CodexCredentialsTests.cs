using System.Text.Json;

namespace JD.SemanticKernel.Connectors.OpenAICodex.Tests;

public class CodexCredentialsTests
{
    [Fact]
    public void Deserialize_FullCredentialsFile()
    {
        var json = """
            {
                "access_token": "at-abc123",
                "id_token": "eyJhbGciOiJSUzI1NiJ9.test",
                "refresh_token": "rt-xyz789",
                "expires_at": 1999999999,
                "token_type": "Bearer"
            }
            """;

        var creds = JsonSerializer.Deserialize<CodexCredentialsFile>(json);

        Assert.NotNull(creds);
        Assert.Equal("at-abc123", creds!.AccessToken);
        Assert.Equal("eyJhbGciOiJSUzI1NiJ9.test", creds.IdToken);
        Assert.Equal("rt-xyz789", creds.RefreshToken);
        Assert.Equal(1999999999L, creds.ExpiresAt);
        Assert.Equal("Bearer", creds.TokenType);
    }

    [Fact]
    public void IsExpired_FarFuture_ReturnsFalse()
    {
        var creds = new CodexCredentialsFile
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        };

        Assert.False(creds.IsExpired);
    }

    [Fact]
    public void IsExpired_PastTime_ReturnsTrue()
    {
        var creds = new CodexCredentialsFile
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()
        };

        Assert.True(creds.IsExpired);
    }

    [Fact]
    public void IsExpired_WithinSafetyMargin_ReturnsTrue()
    {
        // 30 seconds from now should be within the 60-second safety margin
        var creds = new CodexCredentialsFile
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds()
        };

        Assert.True(creds.IsExpired);
    }

    [Fact]
    public void ExpiresAtUtc_ConvertsCorrectly()
    {
        var creds = new CodexCredentialsFile { ExpiresAt = 1700000000 };
        var expected = DateTimeOffset.FromUnixTimeSeconds(1700000000);

        Assert.Equal(expected, creds.ExpiresAtUtc);
    }

    [Fact]
    public void Deserialize_MinimalFile()
    {
        var json = """{ "access_token": "test" }""";
        var creds = JsonSerializer.Deserialize<CodexCredentialsFile>(json);

        Assert.NotNull(creds);
        Assert.Equal("test", creds!.AccessToken);
        Assert.Null(creds.IdToken);
        Assert.Null(creds.RefreshToken);
    }

    [Fact]
    public void Deserialize_NestedTokensFormat()
    {
        // Codex CLI v0.1+ stores tokens under a "tokens" object
        var json = """
            {
                "last_refresh": "2026-02-26T20:09:00Z",
                "OPENAI_API_KEY": "",
                "tokens": {
                    "id_token": "eyJ.nested-id",
                    "access_token": "eyJ.nested-access",
                    "refresh_token": "rt_nested123",
                    "account_id": "abc-123"
                }
            }
            """;

        var creds = JsonSerializer.Deserialize<CodexCredentialsFile>(json);

        Assert.NotNull(creds);
        // Flat fields should be null
        Assert.Null(creds!.AccessToken);
        Assert.Null(creds.IdToken);
        Assert.Null(creds.RefreshToken);

        // Nested tokens should be populated
        Assert.NotNull(creds.Tokens);
        Assert.Equal("eyJ.nested-access", creds.Tokens!.AccessToken);
        Assert.Equal("eyJ.nested-id", creds.Tokens.IdToken);
        Assert.Equal("rt_nested123", creds.Tokens.RefreshToken);
        Assert.Equal("abc-123", creds.Tokens.AccountId);

        // Effective accessors should merge
        Assert.Equal("eyJ.nested-access", creds.EffectiveAccessToken);
        Assert.Equal("eyJ.nested-id", creds.EffectiveIdToken);
        Assert.Equal("rt_nested123", creds.EffectiveRefreshToken);
    }

    [Fact]
    public void Deserialize_NestedTokensWithApiKey()
    {
        var json = """
            {
                "OPENAI_API_KEY": "sk-proj-abc123",
                "tokens": {
                    "access_token": "eyJ.access"
                }
            }
            """;

        var creds = JsonSerializer.Deserialize<CodexCredentialsFile>(json);

        Assert.NotNull(creds);
        Assert.Equal("sk-proj-abc123", creds!.OpenAIApiKey);
        Assert.Equal("eyJ.access", creds.EffectiveAccessToken);
    }

    [Fact]
    public void EffectiveAccessors_PreferFlatOverNested()
    {
        // If both flat and nested are present, flat wins
        var creds = new CodexCredentialsFile
        {
            AccessToken = "flat-access",
            IdToken = "flat-id",
            RefreshToken = "flat-refresh",
            Tokens = new CodexTokensBlock
            {
                AccessToken = "nested-access",
                IdToken = "nested-id",
                RefreshToken = "nested-refresh",
            },
        };

        Assert.Equal("flat-access", creds.EffectiveAccessToken);
        Assert.Equal("flat-id", creds.EffectiveIdToken);
        Assert.Equal("flat-refresh", creds.EffectiveRefreshToken);
    }

    [Fact]
    public void IsExpired_ZeroExpiresAt_ReturnsFalse()
    {
        // When no expiry is set, should not be treated as expired
        var creds = new CodexCredentialsFile { ExpiresAt = 0 };

        Assert.False(creds.IsExpired);
    }

    [Fact]
    public void ExpiresAtUtc_ZeroExpiresAt_ReturnsMaxValue()
    {
        var creds = new CodexCredentialsFile { ExpiresAt = 0 };

        Assert.Equal(DateTimeOffset.MaxValue, creds.ExpiresAtUtc);
    }
}
