using JD.SemanticKernel.Connectors.Abstractions;

namespace JD.SemanticKernel.Connectors.OpenAICodex;

/// <summary>
/// Configuration options for OpenAI Codex session authentication.
/// Bind from configuration section <c>"CodexSession"</c> or configure via
/// <see cref="ServiceCollectionExtensions.AddCodexAuthentication(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{CodexSessionOptions})"/>.
/// </summary>
public sealed class CodexSessionOptions : SessionOptionsBase
{
    /// <summary>The default configuration section name.</summary>
    public const string SectionName = "CodexSession";

    /// <summary>
    /// Override: use an explicit OpenAI API key instead of session extraction.
    /// Takes priority over all other credential sources.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Override: use an explicit OAuth access token instead of reading from file.
    /// Useful for CI/CD environments where the token is injected as a secret.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Explicit path to <c>auth.json</c>.
    /// When <see langword="null"/>, defaults to <c>~/.codex/auth.json</c>.
    /// </summary>
    public string? CredentialsPath { get; set; }

    /// <summary>
    /// The OpenAI auth issuer URL.
    /// Defaults to <c>https://auth.openai.com</c>.
    /// </summary>
    public string Issuer { get; set; } = "https://auth.openai.com";

    /// <summary>
    /// The OpenAI API base URL.
    /// Defaults to <c>https://api.openai.com/v1</c>.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>
    /// The OAuth client ID used for device code authentication.
    /// When <see langword="null"/>, uses the default Codex CLI client ID.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// When <see langword="true"/>, enables interactive device code login
    /// if no credentials are found from other sources.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool EnableInteractiveLogin { get; set; }

    /// <summary>
    /// When <see langword="true"/>, automatically refreshes expired tokens
    /// using the stored refresh token.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool AutoRefreshTokens { get; set; } = true;
}
