using System.Net;

namespace JD.SemanticKernel.Connectors.OpenAICodex.Tests;

public class CodexSessionHttpHandlerTests
{
    [Fact]
    public async Task SendAsync_InjectsAuthorizationHeader()
    {
        using var provider = SessionProviderFactory.Create(o => o.ApiKey = "sk-test-key");
        HttpRequestMessage? capturedRequest = null;

        var innerHandler = new StubHandler((req, _) =>
        {
            capturedRequest = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var handler = new CodexSessionHttpHandler(provider, innerHandler);
        using var client = new HttpClient(handler);

        await client.GetAsync("https://api.openai.com/v1/chat/completions");

        Assert.NotNull(capturedRequest);
        Assert.Equal("Bearer", capturedRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("sk-test-key", capturedRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SendAsync_RejectsHttpRequests()
    {
        using var provider = SessionProviderFactory.Create(o => o.ApiKey = "sk-test");
        var innerHandler = new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        using var handler = new CodexSessionHttpHandler(provider, innerHandler);
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync("http://insecure.example.com/api"));
    }

    [Fact]
    public async Task SendAsync_AllowsLocalhost()
    {
        using var provider = SessionProviderFactory.Create(o => o.ApiKey = "sk-test");
        var innerHandler = new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        using var handler = new CodexSessionHttpHandler(provider, innerHandler);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("http://localhost:8080/api");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_NullUri_Throws()
    {
        using var provider = SessionProviderFactory.Create(o => o.ApiKey = "sk-test");
        var innerHandler = new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        using var handler = new CodexSessionHttpHandler(provider, innerHandler);
        using var client = new HttpClient(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, (Uri?)null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(request));
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
