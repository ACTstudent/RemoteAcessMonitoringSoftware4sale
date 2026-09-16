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
}
