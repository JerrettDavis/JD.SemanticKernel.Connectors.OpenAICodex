namespace JD.SemanticKernel.Connectors.OpenAICodex.Tests;

public class CodexSessionExceptionTests
{
    [Fact]
    public void DefaultConstructor_CreatesException()
    {
        var ex = new CodexSessionException();
        Assert.NotNull(ex);
    }

    [Fact]
    public void MessageConstructor_SetsMessage()
    {
        var ex = new CodexSessionException("test message");
        Assert.Equal("test message", ex.Message);
    }

    [Fact]
    public void InnerExceptionConstructor_SetsInner()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new CodexSessionException("outer", inner);

        Assert.Equal("outer", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void IsInvalidOperationException()
    {
        Assert.IsAssignableFrom<InvalidOperationException>(new CodexSessionException());
    }
}
