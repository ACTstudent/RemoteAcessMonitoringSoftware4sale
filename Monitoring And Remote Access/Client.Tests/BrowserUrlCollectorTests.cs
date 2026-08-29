using Client.Services;

namespace Client.Tests;

public class BrowserUrlCollectorTests
{
    [Theory]
    [InlineData(" HTTPS://user:secret@Example.COM:443/path?q=1#fragment ", "example.com")]
    [InlineData("https://example.com./", "example.com")]
    [InlineData("http://localhost:8080/tool", "localhost")]
    public void TryGetHttpHost_ReturnsCanonicalHost(string value, string expected)
    {
        Assert.True(BrowserUrlCollector.TryGetHttpHost(value, out var host));
        Assert.Equal(expected, host);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a URL")]
    [InlineData("")]
    public void TryGetHttpHost_RejectsNonWebValues(string value)
    {
        Assert.False(BrowserUrlCollector.TryGetHttpHost(value, out _));
    }
}
