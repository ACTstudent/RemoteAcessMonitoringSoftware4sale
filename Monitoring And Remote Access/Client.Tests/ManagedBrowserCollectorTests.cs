using Client.Services;

namespace Client.Tests;

public class ManagedBrowserCollectorTests
{
    [Fact]
    public async Task UnavailableEndpointReturnsNoObservation()
    {
        using var collector = new ManagedBrowserCollector([1]);

        var result = await collector.TryGetActiveWebsiteAsync();

        Assert.Null(result);
    }
}
