namespace JD.SemanticKernel.Connectors.OpenAICodex.Tests;

public class CodexHttpClientFactoryTests
{
    [Fact]
    public void Create_WithApiKey_ReturnsHttpClient()
    {
        using var client = CodexHttpClientFactory.Create("sk-test-factory");
        Assert.NotNull(client);
    }

    [Fact]
    public void Create_WithDelegate_ReturnsHttpClient()
    {
        using var client = CodexHttpClientFactory.Create(o => o.ApiKey = "sk-delegate");
        Assert.NotNull(client);
    }

    [Fact]
    public void Create_Default_ReturnsHttpClient()
    {
        using var client = CodexHttpClientFactory.Create();
        Assert.NotNull(client);
    }

    [Fact]
    public void Create_WithProvider_ReturnsHttpClient()
    {
        using var provider = SessionProviderFactory.Create(o => o.ApiKey = "sk-test");
        using var client = CodexHttpClientFactory.Create(provider);
        Assert.NotNull(client);
    }

    [Fact]
    public void Create_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CodexHttpClientFactory.Create((CodexSessionProvider)null!));
    }
}
