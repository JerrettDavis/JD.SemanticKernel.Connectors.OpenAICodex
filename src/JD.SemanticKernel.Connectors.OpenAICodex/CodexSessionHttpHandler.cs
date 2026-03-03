using System.Net.Http.Headers;

namespace JD.SemanticKernel.Connectors.OpenAICodex;

/// <summary>
/// A <see cref="DelegatingHandler"/> that injects OpenAI API credentials into every
/// outgoing HTTP request.
/// Uses <c>Authorization: Bearer {apiKey}</c> header format.
/// </summary>
public sealed class CodexSessionHttpHandler : DelegatingHandler
{
    private readonly CodexSessionProvider _provider;

    /// <summary>
    /// Initialises a new handler backed by the given <paramref name="provider"/>.
    /// </summary>
    public CodexSessionHttpHandler(
        CodexSessionProvider provider,
        bool dangerouslyDisableSslValidation = false)
        : base(CreateInnerHandler(dangerouslyDisableSslValidation))
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Initialises a handler with an explicit inner handler — intended for unit testing.
    /// </summary>
    internal CodexSessionHttpHandler(CodexSessionProvider provider, HttpMessageHandler innerHandler)
        : base(innerHandler ?? throw new ArgumentNullException(nameof(innerHandler)))
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is null)
            throw new InvalidOperationException(
                "Request URI must not be null when using Codex authentication.");

        if (!string.Equals(request.RequestUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            && !string.Equals(request.RequestUri.Host, "localhost", StringComparison.Ordinal)
            && !string.Equals(request.RequestUri.Host, "127.0.0.1", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Only HTTPS requests are allowed when using Codex authentication.");

        var apiKey = await _provider
            .GetApiKeyAsync(cancellationToken)
            .ConfigureAwait(false);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static HttpClientHandler CreateInnerHandler(bool disableSsl)
    {
        var handler = new HttpClientHandler();
        if (disableSsl)
        {
#pragma warning disable MA0039 // Intentional: enterprise proxy support
#if NET5_0_OR_GREATER
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
#else
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
#endif
#pragma warning restore MA0039
        }

        return handler;
    }
}
