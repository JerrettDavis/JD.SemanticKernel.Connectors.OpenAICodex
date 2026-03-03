namespace JD.SemanticKernel.Connectors.OpenAICodex;

/// <summary>
/// Well-known OpenAI model identifiers available through Codex subscriptions.
/// </summary>
public static class CodexModels
{
    /// <summary>o3 — most capable reasoning model.</summary>
    public const string O3 = "o3";

    /// <summary>o4-mini — fast, cost-effective reasoning model.</summary>
    public const string O4Mini = "o4-mini";

    /// <summary>codex-mini — optimised for code generation tasks.</summary>
    public const string CodexMini = "codex-mini-latest";

    /// <summary>GPT-4.1 — high-capability general model.</summary>
    public const string Gpt4Point1 = "gpt-4.1";

    /// <summary>GPT-4.1-mini — fast general model.</summary>
    public const string Gpt4Point1Mini = "gpt-4.1-mini";

    /// <summary>GPT-4.1-nano — fastest, most cost-effective general model.</summary>
    public const string Gpt4Point1Nano = "gpt-4.1-nano";

    /// <summary>
    /// The recommended default model (<see cref="O4Mini"/>).
    /// </summary>
    public const string Default = O4Mini;
}
