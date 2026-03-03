using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JD.SemanticKernel.Connectors.OpenAICodex.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCodexAuthentication_WithConfiguration_RegistersProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["CodexSession:ApiKey"] = "sk-config-key"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCodexAuthentication(config);

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetService<CodexSessionProvider>();

        Assert.NotNull(provider);
    }

    [Fact]
    public void AddCodexAuthentication_WithDelegate_RegistersProvider()
    {
        var services = new ServiceCollection();
        services.AddCodexAuthentication(o => o.ApiKey = "sk-delegate-key");

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetService<CodexSessionProvider>();

        Assert.NotNull(provider);
    }

    [Fact]
    public void AddCodexAuthentication_NoArgs_RegistersProvider()
    {
        var services = new ServiceCollection();
        services.AddCodexAuthentication();

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetService<CodexSessionProvider>();

        Assert.NotNull(provider);
    }

    [Fact]
    public void AddCodexAuthentication_NullServices_Throws()
    {
        IServiceCollection? services = null;
        Assert.Throws<ArgumentNullException>(
            () => services!.AddCodexAuthentication());
    }

    [Fact]
    public void AddCodexAuthentication_NullConfiguration_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(
            () => services.AddCodexAuthentication((IConfiguration)null!));
    }
}
