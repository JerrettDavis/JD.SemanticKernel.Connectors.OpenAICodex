#if !NETSTANDARD2_0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace JD.SemanticKernel.Connectors.OpenAICodex;

/// <summary>
/// <see cref="IKernelBuilder"/> extensions for wiring Codex authentication into
/// Semantic Kernel using the built-in OpenAI connector.
/// </summary>
public static class KernelBuilderExtensions
{
    /// <summary>
    /// Registers an OpenAI chat completion service backed by Codex session authentication.
    /// Credentials are resolved automatically from <c>~/.codex/auth.json</c>,
    /// environment variables, or the options delegate — in that priority order.
    /// </summary>
    /// <param name="builder">The kernel builder to configure.</param>
    /// <param name="modelId">
    /// The OpenAI model to target. Defaults to <see cref="CodexModels.Default"/>.
    /// See <see cref="CodexModels"/> for well-known identifiers.
    /// </param>
    /// <param name="apiKey">
    /// Optional explicit API key override. When supplied, credential file
    /// and environment variable lookup is skipped.
    /// </param>
    /// <param name="configure">
    /// Optional delegate for fine-grained control over <see cref="CodexSessionOptions"/>.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// // Minimal — reads from ~/.codex/auth.json or OPENAI_API_KEY
    /// var kernel = Kernel.CreateBuilder()
    ///     .UseCodexChatCompletion()
    ///     .Build();
    ///
    /// // With explicit API key
    /// var kernel = Kernel.CreateBuilder()
    ///     .UseCodexChatCompletion(apiKey: "sk-...")
    ///     .Build();
    ///
    /// // Custom model
    /// var kernel = Kernel.CreateBuilder()
    ///     .UseCodexChatCompletion("o3")
    ///     .Build();
    /// </code>
    /// </example>
    public static IKernelBuilder UseCodexChatCompletion(
        this IKernelBuilder builder,
        string modelId = CodexModels.Default,
        string? apiKey = null,
        Action<CodexSessionOptions>? configure = null)
    {
        var options = new CodexSessionOptions();
        if (apiKey is not null) options.ApiKey = apiKey;
        configure?.Invoke(options);

        var provider = new CodexSessionProvider(
            Options.Create(options),
            NullLogger<CodexSessionProvider>.Instance);

        // Resolve the API key eagerly-ish — SK's OpenAI connector needs a key at registration time.
        // We use a factory to defer resolution until the service is first used.
        builder.Services.AddSingleton(provider);

        builder.Services.AddKeyedSingleton<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>(
            null,
            (sp, _) =>
            {
                var p = sp.GetRequiredService<CodexSessionProvider>();
                // Resolve synchronously — the provider caches after first call.
                // In async scenarios, use AddCodexAuthentication + manual wiring.
                var key = p.GetApiKeyAsync().GetAwaiter().GetResult();

                var endpoint = options.ApiBaseUrl.TrimEnd('/');
                if (!endpoint.EndsWith("/v1", StringComparison.Ordinal))
                    endpoint += "/v1";

#pragma warning disable SKEXP0010 // OpenAI connector experimental API
                return new OpenAIChatCompletionService(
                    modelId: modelId,
                    apiKey: key,
                    endpoint: new Uri(endpoint));
#pragma warning restore SKEXP0010
            });

        return builder;
    }
}

#endif
