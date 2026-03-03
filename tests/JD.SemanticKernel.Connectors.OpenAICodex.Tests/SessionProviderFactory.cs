using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JD.SemanticKernel.Connectors.OpenAICodex.Tests;

/// <summary>Helper to create <see cref="CodexSessionProvider"/> for testing.</summary>
internal static class SessionProviderFactory
{
    public static CodexSessionProvider Create(Action<CodexSessionOptions>? configure = null)
    {
        var options = new CodexSessionOptions();
        configure?.Invoke(options);

        return new CodexSessionProvider(
            Options.Create(options),
            NullLogger<CodexSessionProvider>.Instance);
    }
}
