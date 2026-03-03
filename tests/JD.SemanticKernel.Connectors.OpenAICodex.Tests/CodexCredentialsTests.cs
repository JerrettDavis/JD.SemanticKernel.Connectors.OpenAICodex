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
}
