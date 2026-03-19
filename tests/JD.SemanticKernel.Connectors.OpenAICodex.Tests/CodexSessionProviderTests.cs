using System.Text.Json;

namespace JD.SemanticKernel.Connectors.OpenAICodex.Tests;

public class CodexSessionProviderTests
{
    private static readonly object s_envLock = new();
    private static readonly SemaphoreSlim s_envGate = new(1, 1);

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
        var nonexistentCreds = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "auth.json");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-from-env");
            using var provider = SessionProviderFactory.Create(o => o.CredentialsPath = nonexistentCreds);

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
        var nonexistentCreds = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "auth.json");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", "oauth-env-token");

            using var provider = SessionProviderFactory.Create(o => o.CredentialsPath = nonexistentCreds);
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

    [Fact]
    public void ResolveCredentialsPath_UsesCodexHomeEnvVar()
    {
        var tempCodexHome = Path.Combine(Path.GetTempPath(), $"codex-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempCodexHome);
        lock (s_envLock)
        {
            var previous = Environment.GetEnvironmentVariable("CODEX_HOME");
            try
            {
                Environment.SetEnvironmentVariable("CODEX_HOME", tempCodexHome);
                using var provider = SessionProviderFactory.Create();

                var path = provider.ResolveCredentialsPath();
                Assert.Equal(Path.Combine(tempCodexHome, "auth.json"), path);
            }
            finally
            {
                Environment.SetEnvironmentVariable("CODEX_HOME", previous);
                Directory.Delete(tempCodexHome, true);
            }
        }
    }

    [Fact]
    public async Task GetApiKey_ChatGptModeInAuthStorage_PrefersManagedTokensOverEnvApiKey()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var credPath = Path.Combine(tmpDir, "auth.json");

        await s_envGate.WaitAsync();
        try
        {
            var prevOpenAi = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var prevCodex = Environment.GetEnvironmentVariable("CODEX_TOKEN");
            try
            {
                Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-stale-env-key");
                Environment.SetEnvironmentVariable("CODEX_TOKEN", null);

                var json = """
                    {
                        "auth_mode": "chatgpt",
                        "tokens": {
                            "id_token": "id-token-from-chatgpt-auth-mode"
                        }
                    }
                    """;
                await File.WriteAllTextAsync(credPath, json);

                using var provider = SessionProviderFactory.Create(o => o.CredentialsPath = credPath);
                provider.TokenExchanger = (_, token, _) =>
                    Task.FromResult<string?>($"api-key-from-{token}");

                var key = await provider.GetApiKeyAsync();
                Assert.Equal("api-key-from-id-token-from-chatgpt-auth-mode", key);
            }
            finally
            {
                Environment.SetEnvironmentVariable("OPENAI_API_KEY", prevOpenAi);
                Environment.SetEnvironmentVariable("CODEX_TOKEN", prevCodex);
                Directory.Delete(tmpDir, true);
            }
        }
        finally
        {
            s_envGate.Release();
        }
    }

    [Fact]
    public void ComputeCodexKeyringAccountKey_IsDeterministic()
    {
        var a = CodexSessionProvider.ComputeCodexKeyringAccountKey(@"/home/alice/.codex");
        var b = CodexSessionProvider.ComputeCodexKeyringAccountKey(@"/home/alice/.codex");
        var c = CodexSessionProvider.ComputeCodexKeyringAccountKey(@"/home/bob/.codex");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.StartsWith("cli|", a, StringComparison.Ordinal);
        Assert.Equal(20, a.Length);
    }

    [Fact]
    public async Task GetApiKey_NestedTokens_ExchangesIdToken()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var credPath = Path.Combine(tmpDir, "auth.json");

        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);

            // Write Codex CLI nested format
            var json = """
                {
                    "last_refresh": "2026-02-26T20:09:00Z",
                    "OPENAI_API_KEY": "",
                    "tokens": {
                        "id_token": "eyJ.nested-id-token",
                        "access_token": "eyJ.nested-access-token",
                        "refresh_token": "rt_nested",
                        "account_id": "acc-123"
                    }
                }
                """;
            await File.WriteAllTextAsync(credPath, json);

            using var provider = SessionProviderFactory.Create(o => o.CredentialsPath = credPath);
            provider.TokenExchanger = (_, token, _) =>
                Task.FromResult<string?>($"api-key-from-{token}");

            var key = await provider.GetApiKeyAsync();
            Assert.Equal("api-key-from-eyJ.nested-id-token", key);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task GetApiKey_NestedTokens_NoIdToken_UsesAccessToken()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var credPath = Path.Combine(tmpDir, "auth.json");

        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);

            var json = """
                {
                    "tokens": {
                        "access_token": "nested-access-direct"
                    }
                }
                """;
            await File.WriteAllTextAsync(credPath, json);

            using var provider = SessionProviderFactory.Create(o => o.CredentialsPath = credPath);
            provider.TokenExchanger = (_, _, _) => Task.FromResult<string?>(null);

            var key = await provider.GetApiKeyAsync();
            Assert.Equal("nested-access-direct", key);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task GetApiKey_OpenAIApiKeyFieldInFile_UsedDirectly()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var credPath = Path.Combine(tmpDir, "auth.json");

        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);

            var json = """
                {
                    "OPENAI_API_KEY": "sk-file-key-123",
                    "tokens": {
                        "access_token": "should-not-be-used"
                    }
                }
                """;
            await File.WriteAllTextAsync(credPath, json);

            using var provider = SessionProviderFactory.Create(o => o.CredentialsPath = credPath);

            var key = await provider.GetApiKeyAsync();
            Assert.Equal("sk-file-key-123", key);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task IsAuthenticated_NestedTokens_ReturnsTrue()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var credPath = Path.Combine(tmpDir, "auth.json");

        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);

            var json = """
                {
                    "tokens": {
                        "access_token": "nested-token"
                    }
                }
                """;
            await File.WriteAllTextAsync(credPath, json);

            using var provider = SessionProviderFactory.Create(o => o.CredentialsPath = credPath);
            provider.TokenExchanger = (_, _, _) => Task.FromResult<string?>(null);

            Assert.True(await provider.IsAuthenticatedAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CODEX_TOKEN", null);
            Directory.Delete(tmpDir, true);
        }
    }
}
