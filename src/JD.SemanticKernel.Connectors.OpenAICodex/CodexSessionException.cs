namespace JD.SemanticKernel.Connectors.OpenAICodex;

/// <summary>
/// Thrown when Codex credentials are unavailable, expired, or not configured.
/// The <see cref="Exception.Message"/> is safe to display directly to end users.
/// </summary>
public sealed class CodexSessionException : InvalidOperationException
{
    /// <inheritdoc cref="InvalidOperationException()"/>
    public CodexSessionException() { }

    /// <inheritdoc cref="InvalidOperationException(string)"/>
    public CodexSessionException(string message) : base(message) { }

    /// <inheritdoc cref="InvalidOperationException(string, Exception)"/>
    public CodexSessionException(string message, Exception innerException)
        : base(message, innerException) { }
}
