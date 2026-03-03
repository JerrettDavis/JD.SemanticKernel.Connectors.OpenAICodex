namespace JD.SemanticKernel.Connectors.OpenAICodex.Tests;

public class CodexModelDiscoveryTests
{
    [Fact]
    public async Task DiscoverModels_ReturnsKnownModels()
    {
        var discovery = new CodexModelDiscovery();
        var models = await discovery.DiscoverModelsAsync();

        Assert.NotEmpty(models);
        Assert.Contains(models, m => string.Equals(m.Id, CodexModels.O3, StringComparison.Ordinal));
        Assert.Contains(models, m => string.Equals(m.Id, CodexModels.O4Mini, StringComparison.Ordinal));
        Assert.Contains(models, m => string.Equals(m.Id, CodexModels.CodexMini, StringComparison.Ordinal));
        Assert.Contains(models, m => string.Equals(m.Id, CodexModels.Gpt4Point1, StringComparison.Ordinal));
        Assert.Contains(models, m => string.Equals(m.Id, CodexModels.Gpt4Point1Mini, StringComparison.Ordinal));
        Assert.Contains(models, m => string.Equals(m.Id, CodexModels.Gpt4Point1Nano, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscoverModels_AllFromOpenAI()
    {
        var discovery = new CodexModelDiscovery();
        var models = await discovery.DiscoverModelsAsync();

        Assert.All(models, m => Assert.Equal("openai", m.Provider));
    }

    [Fact]
    public async Task DiscoverModels_ReturnsSameInstance()
    {
        var discovery = new CodexModelDiscovery();
        var first = await discovery.DiscoverModelsAsync();
        var second = await discovery.DiscoverModelsAsync();

        Assert.Same(first, second);
    }
}
