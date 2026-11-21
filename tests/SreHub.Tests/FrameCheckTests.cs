using Aras.SreHub.Services;

namespace Aras.SreHub.Tests;

public sealed class FrameCheckTests
{
    private const string Origin = "http://localhost:5280";

    private static HttpResponseMessage Response(params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage();
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    [Theory]
    [InlineData("DENY")]
    [InlineData("SAMEORIGIN")]
    public void FrameOptions_BlockEmbedding(string value)
    {
        var (embeddable, reason) = FrameCheckService.Evaluate(Response(("X-Frame-Options", value)), Origin);

        Assert.False(embeddable);
        Assert.Contains(value, reason);
    }

    [Theory]
    [InlineData("default-src 'self'; frame-ancestors 'none'")]
    [InlineData("frame-ancestors https://*.microsoft.com 'self'")]
    public void FrameAncestorsWithoutThisApp_BlockEmbedding(string policy)
    {
        Assert.False(FrameCheckService.Evaluate(Response(("Content-Security-Policy", policy)), Origin).Embeddable);
    }

    [Theory]
    [InlineData("frame-ancestors *")]
    [InlineData("frame-ancestors http://localhost:5280")]
    [InlineData("frame-ancestors 'self' http://localhost:*")]
    [InlineData("default-src 'self'")]
    public void OpenOrAbsentFramePolicy_AllowsEmbedding(string policy)
    {
        Assert.True(FrameCheckService.Evaluate(Response(("Content-Security-Policy", policy)), Origin).Embeddable);
    }

    [Fact]
    public void NoRestrictingHeaders_AllowsEmbedding()
    {
        Assert.True(FrameCheckService.Evaluate(Response(), Origin).Embeddable);
    }
}
