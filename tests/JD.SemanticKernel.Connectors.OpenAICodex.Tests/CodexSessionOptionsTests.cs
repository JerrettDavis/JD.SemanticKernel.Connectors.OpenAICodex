namespace JD.SemanticKernel.Connectors.OpenAICodex.Tests;

public class CodexSessionOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new CodexSessionOptions();

        Assert.Null(options.ApiKey);
        Assert.Null(options.AccessToken);
        Assert.Null(options.CredentialsPath);
        Assert.Null(options.ClientId);
        Assert.Equal("https://auth.openai.com", options.Issuer);
        Assert.Equal("https://api.openai.com/v1", options.ApiBaseUrl);
        Assert.False(options.EnableInteractiveLogin);
        Assert.True(options.AutoRefreshTokens);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var options = new CodexSessionOptions
        {
            ApiKey = "sk-test",
            AccessToken = "at-test",
            CredentialsPath = "/tmp/creds.json",
            Issuer = "https://custom.auth.com",
            ApiBaseUrl = "https://custom.api.com/v1",
            ClientId = "client-123",
            EnableInteractiveLogin = true,
            AutoRefreshTokens = false
        };

        Assert.Equal("sk-test", options.ApiKey);
        Assert.Equal("at-test", options.AccessToken);
        Assert.Equal("/tmp/creds.json", options.CredentialsPath);
        Assert.Equal("https://custom.auth.com", options.Issuer);
        Assert.Equal("https://custom.api.com/v1", options.ApiBaseUrl);
        Assert.Equal("client-123", options.ClientId);
        Assert.True(options.EnableInteractiveLogin);
        Assert.False(options.AutoRefreshTokens);
    }

    [Fact]
    public void SectionName_IsCodexSession()
    {
        Assert.Equal("CodexSession", CodexSessionOptions.SectionName);
    }
}
