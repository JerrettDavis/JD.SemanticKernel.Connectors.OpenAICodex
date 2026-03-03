using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JD.SemanticKernel.Connectors.OpenAICodex;

/// <summary>
/// Creates pre-configured <see cref="HttpClient"/> instances that resolve OpenAI Codex
/// credentials and set the appropriate Authorization header.
/// </summary>
public static class CodexHttpClientFactory
{
    /// <summary>
    /// Creates an <see cref="HttpClient"/> wired with Codex auto-resolved credentials.
    /// </summary>
    public static HttpClient Create() => Create(configure: null);

    /// <summary>
    /// Creates an <see cref="HttpClient"/> authenticated with the supplied <paramref name="apiKey"/>.
    /// </summary>
    public static HttpClient Create(string apiKey) =>
        Create(o => o.ApiKey = apiKey);

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with options configured by <paramref name="configure"/>.
    /// </summary>
    public static HttpClient Create(Action<CodexSessionOptions>? configure)
    {
        var options = new CodexSessionOptions();
        configure?.Invoke(options);

        var provider = new CodexSessionProvider(
            Options.Create(options),
            NullLogger<CodexSessionProvider>.Instance);

        return new HttpClient(new CodexSessionHttpHandler(provider, options.DangerouslyDisableSslValidation));
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> backed by an existing <paramref name="provider"/>.
    /// </summary>
    public static HttpClient Create(
        CodexSessionProvider provider,
        bool dangerouslyDisableSslValidation = false) =>
        new(new CodexSessionHttpHandler(
            provider ?? throw new ArgumentNullException(nameof(provider)),
            dangerouslyDisableSslValidation));
}
