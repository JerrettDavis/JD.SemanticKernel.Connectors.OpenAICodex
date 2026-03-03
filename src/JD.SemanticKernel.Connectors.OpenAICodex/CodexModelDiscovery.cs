using JD.SemanticKernel.Connectors.Abstractions;

namespace JD.SemanticKernel.Connectors.OpenAICodex;

/// <summary>
/// Returns the well-known Codex model catalogue.
/// </summary>
public sealed class CodexModelDiscovery : IModelDiscoveryProvider
{
    private static readonly IReadOnlyList<ModelInfo> KnownModels =
    [
        new(CodexModels.O3, "o3", "openai"),
        new(CodexModels.O4Mini, "o4-mini", "openai"),
        new(CodexModels.CodexMini, "codex-mini", "openai"),
        new(CodexModels.Gpt4Point1, "GPT-4.1", "openai"),
        new(CodexModels.Gpt4Point1Mini, "GPT-4.1-mini", "openai"),
        new(CodexModels.Gpt4Point1Nano, "GPT-4.1-nano", "openai"),
    ];

    /// <inheritdoc/>
    public Task<IReadOnlyList<ModelInfo>> DiscoverModelsAsync(CancellationToken ct = default) =>
        Task.FromResult(KnownModels);
}
