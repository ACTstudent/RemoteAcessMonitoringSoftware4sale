using Client.Services;

namespace Client.Tests;

public class ManagedBrowserCollectorTests
{
    [Fact]
    public void BuildsIsolatedCommand()
    {
        var command = ManagedBrowserCollector.BuildArguments(new ManagedBrowserDefinition("chrome", "chrome.exe", 9222), "C:\\CAMS\\Profiles");
        Assert.Contains("--remote-debugging-address=127.0.0.1", command);
        Assert.Contains("--remote-debugging-port=9222", command);
        Assert.Contains("--user-data-dir=\"C:\\CAMS\\Profiles\\chrome\"", command);
    }

    [Theory]
    [InlineData("Google Chrome 123", "chrome", true)]
    [InlineData("Brave/123", "brave", true)]
    [InlineData("Brave/123", "chrome", false)]
    [InlineData("", "chrome", false)]
    public void ParsesBrowserIdentity(string metadata, string identity, bool expected) => Assert.Equal(expected, ManagedBrowserCollector.IsExpectedIdentity(metadata, identity));

    [Fact]
    public async Task UnavailableEndpointReturnsNoObservation()
    {
        using var collector = new ManagedBrowserCollector(new ManagedBrowserOptions(ChromePort: 1, ManageBrave: false));

        var result = await collector.TryGetActiveWebsiteAsync();

        Assert.Null(result);
    }

    [Fact]
    public void BrowserArgumentsUseDistinctProfilesAndPorts()
    {
        var chrome = ManagedBrowserCollector.BuildArguments(new("chrome", "chrome.exe", 9222), "C:\\CAMS\\Profiles");
        var brave = ManagedBrowserCollector.BuildArguments(new("brave", "brave.exe", 9223), "C:\\CAMS\\Profiles");
        Assert.NotEqual(chrome, brave);
        Assert.Contains("\\chrome\"", chrome);
        Assert.Contains("\\brave\"", brave);
    }

    [Fact]
    public void ChromeExecutableLookupDoesNotUseBravePath()
    {
        var path = ManagedBrowserCollector.FindExecutable("chrome.exe");
        Assert.True(path is null || !path.Contains("BraveSoftware", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("CAMS Dashboard", "chrome - CAMS Dashboard - Google Chrome", true)]
    [InlineData("Other tab", "brave - CAMS Dashboard - Brave", false)]
    [InlineData("", "chrome - CAMS Dashboard", false)]
    public void MatchesForegroundWindowTitle(string tabTitle, string windowTitle, bool expected)
    {
        Assert.Equal(expected, ManagedBrowserCollector.IsForegroundTitleMatch(tabTitle, windowTitle));
    }
}
