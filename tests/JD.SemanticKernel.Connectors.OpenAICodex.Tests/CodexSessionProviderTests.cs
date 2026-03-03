using System.Text.Json;

namespace JD.SemanticKernel.Connectors.OpenAICodex.Tests;

public class CodexSessionProviderTests
{
    [Fact]
    public async Task GetApiKey_ExplicitApiKey_ReturnsIt()
    {
        using var provider = SessionProviderFactory.Create(o => o.ApiKey = "sk-test-key");
        var key = await provider.GetApiKeyAsync();

        Assert.Equal("sk-test-key", key);
    }

    [Fact]
    public async Task GetApiKey_ExplicitAccessToken_LooksLikeApiKey_ReturnsDirectly()
    {
        using var provider = SessionProviderFactory.Create(o => o.AccessToken = "sk-already-an-api-key");
        // Stub exchanger to verify it's not called for sk- prefixed tokens
        provider.TokenExchanger = (_, _, _) =>
            Task.FromResult<string?>("should-not-be-used");

        var key = await provider.GetApiKeyAsync();
        Assert.Equal("sk-already-an-api-key", key);
    }

    [Fact]
    public async Task GetApiKey_ExplicitAccessToken_ExchangesForApiKey()
    {
        using var provider = SessionProviderFactory.Create(o => o.AccessToken = "oauth-token-123");
        provider.TokenExchanger = (_, token, _) =>
            Task.FromResult<string?>($"exchanged-{token}");

        var key = await provider.GetApiKeyAsync();
        Assert.Equal("exchanged-oauth-token-123", key);
    }

    [Fact]
    public async Task GetApiKey_EnvVarOpenAiApiKey_ReturnsIt()
    {
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-from-env");
            using var provider = SessionProviderFactory.Create();

            var key = await provider.GetApiKeyAsync();
            Assert.Equal("sk-from-env", key);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        }
    }

    [Fact]
    public async Task GetApiKey_EnvVarCodexToken_ExchangesForApiKey()
    {
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", "oauth-env-token");

            using var provider = SessionProviderFactory.Create();
            provider.TokenExchanger = (_, token, _) =>
                Task.FromResult<string?>($"exchanged-{token}");

            var key = await provider.GetApiKeyAsync();
            Assert.Equal("exchanged-oauth-env-token", key);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);
        }
    }

    [Fact]
    public async Task GetApiKey_CredentialsFile_ReadsAndExchanges()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var credPath = Path.Combine(tmpDir, "auth.json");

        try
        {
            var creds = new CodexCredentialsFile
            {
                AccessToken = "at-from-file",
                IdToken = "id-token-from-file",
                RefreshToken = "rt-from-file",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
            };
            await File.WriteAllTextAsync(credPath, JsonSerializer.Serialize(creds));

            using var provider = SessionProviderFactory.Create(o => o.CredentialsPath = credPath);
            provider.TokenExchanger = (_, token, _) =>
                Task.FromResult<string?>($"api-key-from-{token}");

            var key = await provider.GetApiKeyAsync();
            Assert.Equal("api-key-from-id-token-from-file", key);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task GetApiKey_CredentialsFile_NoIdToken_UsesAccessToken()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var credPath = Path.Combine(tmpDir, "auth.json");

        try
        {
            var creds = new CodexCredentialsFile
            {
                AccessToken = "at-direct",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
            };
            await File.WriteAllTextAsync(credPath, JsonSerializer.Serialize(creds));

            using var provider = SessionProviderFactory.Create(o => o.CredentialsPath = credPath);
            provider.TokenExchanger = (_, _, _) => Task.FromResult<string?>(null);

            var key = await provider.GetApiKeyAsync();
            Assert.Equal("at-direct", key);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task GetApiKey_NoCredentials_ThrowsCodexSessionException()
    {
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);

            using var provider = SessionProviderFactory.Create(o =>
                o.CredentialsPath = Path.Combine(Path.GetTempPath(), "nonexistent", "auth.json"));

            await Assert.ThrowsAsync<CodexSessionException>(
                () => provider.GetApiKeyAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);
        }
    }

    [Fact]
    public async Task GetApiKey_ExpiredCredentials_ThrowsCodexSessionException()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var credPath = Path.Combine(tmpDir, "auth.json");

        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);

            var creds = new CodexCredentialsFile
            {
                AccessToken = "expired-token",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()
            };
            await File.WriteAllTextAsync(credPath, JsonSerializer.Serialize(creds));

            using var provider = SessionProviderFactory.Create(o =>
            {
                o.CredentialsPath = credPath;
                o.AutoRefreshTokens = false;
            });

            await Assert.ThrowsAsync<CodexSessionException>(
                () => provider.GetApiKeyAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task IsAuthenticated_WithApiKey_ReturnsTrue()
    {
        using var provider = SessionProviderFactory.Create(o => o.ApiKey = "sk-test");
        Assert.True(await provider.IsAuthenticatedAsync());
    }

    [Fact]
    public async Task IsAuthenticated_NoCredentials_ReturnsFalse()
    {
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);

            using var provider = SessionProviderFactory.Create(o =>
                o.CredentialsPath = Path.Combine(Path.GetTempPath(), "nonexistent", "auth.json"));

            Assert.False(await provider.IsAuthenticatedAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);
        }
    }

    [Fact]
    public async Task GetApiKey_MalformedJson_ThrowsCodexSessionException()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var credPath = Path.Combine(tmpDir, "auth.json");

        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);

            await File.WriteAllTextAsync(credPath, "not valid json {{{");

            using var provider = SessionProviderFactory.Create(o => o.CredentialsPath = credPath);

            // Malformed file is treated as no credentials
            await Assert.ThrowsAsync<CodexSessionException>(
                () => provider.GetApiKeyAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task GetCredentials_CachesResult()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var credPath = Path.Combine(tmpDir, "auth.json");

        try
        {
            var creds = new CodexCredentialsFile
            {
                AccessToken = "cached-token",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
            };
            await File.WriteAllTextAsync(credPath, JsonSerializer.Serialize(creds));

            using var provider = SessionProviderFactory.Create(o => o.CredentialsPath = credPath);

            var first = await provider.GetCredentialsAsync();
            var second = await provider.GetCredentialsAsync();

            Assert.Same(first, second);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ResolveCredentialsPath_Default()
    {
        using var provider = SessionProviderFactory.Create();
        var path = provider.ResolveCredentialsPath();

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex", "auth.json");
        Assert.Equal(expected, path);
    }

    [Fact]
    public void ResolveCredentialsPath_Custom()
    {
        using var provider = SessionProviderFactory.Create(o =>
            o.CredentialsPath = "/custom/path/auth.json");
        var path = provider.ResolveCredentialsPath();

        Assert.Equal("/custom/path/auth.json", path);
    }
}
