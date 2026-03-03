using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JD.SemanticKernel.Connectors.OpenAICodex;

/// <summary>
/// <see cref="IServiceCollection"/> extensions for registering Codex authentication services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CodexSessionProvider"/> and binds
    /// <see cref="CodexSessionOptions"/> from the <c>"CodexSession"</c> configuration section.
    /// </summary>
    public static IServiceCollection AddCodexAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
#if NETSTANDARD2_0
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
#else
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
#endif

        services.Configure<CodexSessionOptions>(
            configuration.GetSection(CodexSessionOptions.SectionName));

        services.TryAddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.TryAddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<CodexSessionProvider>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="CodexSessionProvider"/> and configures
    /// <see cref="CodexSessionOptions"/> via the provided <paramref name="configure"/> delegate.
    /// </summary>
    public static IServiceCollection AddCodexAuthentication(
        this IServiceCollection services,
        Action<CodexSessionOptions>? configure = null)
    {
#if NETSTANDARD2_0
        if (services is null) throw new ArgumentNullException(nameof(services));
#else
        ArgumentNullException.ThrowIfNull(services);
#endif

        if (configure is not null)
            services.Configure<CodexSessionOptions>(configure);
        else
            services.Configure<CodexSessionOptions>(static _ => { });

        services.TryAddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.TryAddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<CodexSessionProvider>();
        return services;
    }
}
