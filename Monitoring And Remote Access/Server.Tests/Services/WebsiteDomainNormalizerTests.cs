using Shared.Contracts;

namespace Server.Tests.Services;

public class WebsiteDomainNormalizerTests
{
    [Theory]
    [InlineData(" HTTPS://user:secret@Example.COM:443/path?q=1#fragment ", "example.com")]
    [InlineData("https://example.com./", "example.com")]
    [InlineData("http://localhost:8080/tool", "localhost")]
    public void TryNormalize_StripsUrlDetails(string value, string expected)
    {
        Assert.True(WebsiteDomainNormalizer.TryNormalize(value, out var domain));
        Assert.Equal(expected, domain);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a URL")]
    [InlineData("")]
    public void TryNormalize_RejectsNonWebValues(string value)
    {
        Assert.False(WebsiteDomainNormalizer.TryNormalize(value, out _));
    }
}
