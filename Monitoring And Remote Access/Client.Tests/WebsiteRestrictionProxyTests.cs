using Client.Services;
using Shared.Contracts;

namespace Client.Tests;

// The proxy is what actually refuses a connection, so the whitelist bug was felt
// here first: with any allow rule present every other site stopped loading.
public class WebsiteRestrictionProxyTests
{
    private static RestrictionRuleMessage Rule(string target, string mode) => new(1, "Website", target, mode);

    [Fact]
    public void WhitelistingOneSiteDoesNotBlockTheRestOfTheWeb()
    {
        using var proxy = new WebsiteRestrictionProxy();
        proxy.UpdateRules(new[] { Rule("learning.example.org", "Allow") });

        Assert.False(proxy.IsBlocked("wikipedia.org"));
        Assert.False(proxy.IsBlocked("learning.example.org"));
    }

    [Fact]
    public void BlockedSitesAndTheirSubdomainsAreStillRefused()
    {
        using var proxy = new WebsiteRestrictionProxy();
        proxy.UpdateRules(new[] { Rule("games.example.com", "Block"), Rule("learning.example.org", "Allow") });

        Assert.True(proxy.IsBlocked("games.example.com"));
        Assert.True(proxy.IsBlocked("arcade.games.example.com"));
        Assert.False(proxy.IsBlocked("example.com"));
    }

    [Fact]
    public void ATargetedBlockSurvivesABroaderAllow()
    {
        using var proxy = new WebsiteRestrictionProxy();
        proxy.UpdateRules(new[] { Rule("example.com", "Allow"), Rule("bad.example.com", "Block") });

        Assert.True(proxy.IsBlocked("bad.example.com"));
        Assert.False(proxy.IsBlocked("docs.example.com"));
    }

    [Fact]
    public void NoRulesBlocksNothing()
    {
        using var proxy = new WebsiteRestrictionProxy();
        proxy.UpdateRules(Array.Empty<RestrictionRuleMessage>());

        Assert.False(proxy.IsBlocked("example.com"));
        Assert.False(proxy.IsBlocked(string.Empty));
    }

    // With a website whitelisted, the server sends the rules with a catch-all
    // block beneath them: only the whitelisted site and its subdomains pass.
    [Fact]
    public void WithTheServersWhitelistRules_OnlyTheWhitelistedSitePasses()
    {
        using var proxy = new WebsiteRestrictionProxy();
        proxy.UpdateRules(PolicyDecision.WithWebsiteAllowlist(new[] { Rule("facebook.com", "Allow"), Rule("youtube.com", "Block") }));

        Assert.False(proxy.IsBlocked("facebook.com"));
        Assert.False(proxy.IsBlocked("www.facebook.com"));
        Assert.True(proxy.IsBlocked("www.roblox.com"));
        Assert.True(proxy.IsBlocked("youtube.com"));
    }

    // A website whitelist blocks everything else - but never CAMS itself.
    [Fact]
    public void TheCamsServerIsNeverBlocked()
    {
        using var proxy = new WebsiteRestrictionProxy("cams-server.lab.local");
        proxy.UpdateRules(PolicyDecision.WithWebsiteAllowlist(new[] { Rule("facebook.com", "Allow") }));

        Assert.False(proxy.IsBlocked("cams-server.lab.local"));
        Assert.True(proxy.IsBlocked("www.roblox.com"));
        Assert.True(proxy.IsBlocked("localhost"));   // a different machine's loopback is not CAMS
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public void AServerOnThisPcKeepsEveryLoopbackNameOpen(string serverHost)
    {
        using var proxy = new WebsiteRestrictionProxy(serverHost);
        proxy.UpdateRules(PolicyDecision.WithWebsiteAllowlist(new[] { Rule("facebook.com", "Allow") }));

        Assert.False(proxy.IsBlocked("localhost"));
        Assert.False(proxy.IsBlocked("127.0.0.1"));
        Assert.False(proxy.IsBlocked("[::1]"));
        Assert.True(proxy.IsBlocked("www.roblox.com"));
    }

    // ---------- Connecting for the browser ----------

    [Fact]
    public void AddressesAreTriedIPv4First_ThenAlternating()
    {
        var v6a = System.Net.IPAddress.Parse("2a03:2880:f17e:180:face:b00c:0:25de");
        var v6b = System.Net.IPAddress.Parse("2001:db8::2");
        var v4a = System.Net.IPAddress.Parse("163.70.130.35");
        var v4b = System.Net.IPAddress.Parse("198.51.100.7");

        var order = WebsiteRestrictionProxy.ConnectionOrder(new[] { v6a, v6b, v4a, v4b });

        Assert.Equal(new[] { v4a, v6a, v4b, v6b }, order);
    }

    // The bug: on Wi-Fi that hands out IPv6 but cannot route it, facebook.com's
    // IPv6 address never answered, the proxy waited out its setup budget on it,
    // and the browser got an empty response for a whitelisted site. An address
    // that does not answer now gets a few seconds before the next is tried.
    [Fact]
    public async Task AnAddressThatNeverAnswersIsSkipped()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var silent = System.Net.IPAddress.Parse("192.0.2.1");   // TEST-NET-1: nothing answers there
        var clock = System.Diagnostics.Stopwatch.StartNew();

        using var socket = await WebsiteRestrictionProxy.ConnectToFirstAvailableAsync(
            new[] { silent, System.Net.IPAddress.Loopback }, port, TimeSpan.FromMilliseconds(400), CancellationToken.None);

        Assert.True(socket.Connected);
        Assert.Equal(System.Net.IPAddress.Loopback, ((System.Net.IPEndPoint)socket.RemoteEndPoint!).Address);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task WhenNoAddressAnswers_TheConnectFails()
    {
        await Assert.ThrowsAsync<System.Net.Sockets.SocketException>(() =>
            WebsiteRestrictionProxy.ConnectToFirstAvailableAsync(
                new[] { System.Net.IPAddress.Parse("192.0.2.1"), System.Net.IPAddress.Parse("192.0.2.2") },
                443, TimeSpan.FromMilliseconds(200), CancellationToken.None));
    }
}
