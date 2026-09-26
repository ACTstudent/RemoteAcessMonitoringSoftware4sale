using Shared.Contracts;

namespace Server.Tests.Contracts;

public sealed class PolicyDecisionTests
{
    private static RestrictionRuleMessage Allow(string target, string type = "Website") => new(1, type, target, "Allow");
    private static RestrictionRuleMessage Block(string target, string type = "Website") => new(2, type, target, "Block");

    // The decision itself never reads an allow rule as "deny everything else":
    // that once closed every other window on the desktop when one application
    // was allowed. The website whitelist is a separate, explicit step -
    // WithWebsiteAllowlist, below - that adds a catch-all block for websites only.
    [Fact]
    public void AnUnmatchedDomainIsNotBlockedJustBecauseAllowRulesExist()
    {
        var rules = new[] { Allow("learning.example.org"), Allow("exam.example.org") };

        Assert.False(PolicyDecision.IsBlocked(rules, "wikipedia.org", isDomain: true));
        Assert.Null(PolicyDecision.FindMatch(rules, "wikipedia.org", isDomain: true));
    }

    [Fact]
    public void AnUnmatchedApplicationIsNotBlockedJustBecauseAllowRulesExist()
    {
        var rules = new[] { Allow("chrome", "Application") };

        Assert.False(PolicyDecision.IsBlocked(rules, "explorer", isDomain: false));
        Assert.False(PolicyDecision.IsBlocked(rules, "winword", isDomain: false));
    }

    [Fact]
    public void ABlockingRuleStillBlocks()
    {
        var rules = new[] { Allow("learning.example.org"), Block("games.example.com") };

        Assert.True(PolicyDecision.IsBlocked(rules, "games.example.com", isDomain: true));
        Assert.True(PolicyDecision.IsBlocked(rules, "arcade.games.example.com", isDomain: true));
        Assert.False(PolicyDecision.IsBlocked(rules, "learning.example.org", isDomain: true));
    }

    [Fact]
    public void AllowWinsAgainstABlockOfEqualSpecificity()
    {
        var rules = new[] { Block("example.com"), Allow("example.com") };

        Assert.False(PolicyDecision.IsBlocked(rules, "example.com", isDomain: true));
    }

    // A broad allow must not reopen something blocked by name, or a whitelist
    // entry would quietly undo the blacklist it is meant to sit beside.
    [Fact]
    public void AMoreSpecificBlockBeatsABroaderAllow()
    {
        var rules = new[] { Allow("example.com"), Block("bad.example.com") };

        Assert.True(PolicyDecision.IsBlocked(rules, "bad.example.com", isDomain: true));
        Assert.False(PolicyDecision.IsBlocked(rules, "docs.example.com", isDomain: true));
    }

    // ---------- Web addresses versus program names ----------

    [Theory]
    [InlineData("facebook.com", true)]
    [InlineData("www.youtube.com", true)]
    [InlineData("https://classroom.google.com/u/0", true)]
    [InlineData("deped.gov.ph", true)]
    [InlineData("zoom.us", true)]
    [InlineData("*.example.org", true)]
    [InlineData("chrome.exe", false)]
    [InlineData("notepad", false)]
    [InlineData("Microsoft.Photos", false)]
    [InlineData(@"C:\Program Files\Game\game.exe", false)]
    [InlineData("", false)]
    public void LooksLikeWebsite_TellsAddressesFromProgramNames(string target, bool website)
    {
        Assert.Equal(website, PolicyPatternMatcher.LooksLikeWebsite(target));
    }

    // ---------- The website whitelist ----------

    private static bool BlockedUnderWhitelist(IEnumerable<RestrictionRuleMessage> rules, string domain) =>
        PolicyDecision.IsBlocked(PolicyDecision.WithWebsiteAllowlist(rules), domain, isDomain: true);

    // What a teacher means by a whitelist: only the listed websites open. It had
    // been an exception list, so a student could still open anything nobody had
    // blacklisted.
    [Theory]
    [InlineData("khanacademy.org", false)]
    [InlineData("www.khanacademy.org", false)]
    [InlineData("deped.gov.ph", false)]
    [InlineData("facebook.com", true)]
    [InlineData("wikipedia.org", true)]
    [InlineData("youtube.com", true)]
    public void OnceAWebsiteIsWhitelisted_OnlyWhitelistedWebsitesOpen(string domain, bool blocked)
    {
        var rules = new[] { Allow("khanacademy.org"), Allow("deped.gov.ph"), Block("youtube.com") };

        Assert.Equal(blocked, BlockedUnderWhitelist(rules, domain));
    }

    // A blacklist entry for a narrower address still closes it inside the whitelist.
    [Fact]
    public void ABlockInsideTheWhitelistStillBlocks()
    {
        var rules = new[] { Allow("example.com"), Block("games.example.com") };

        Assert.True(BlockedUnderWhitelist(rules, "games.example.com"));
        Assert.False(BlockedUnderWhitelist(rules, "docs.example.com"));
        Assert.True(BlockedUnderWhitelist(rules, "other.org"));
    }

    // No website is allowed, so nothing changes - including when the only allow
    // rule is an application rule, which is how "facebook.com" had been saved.
    [Fact]
    public void WithoutAWebsiteAllowRule_TheRulesAreLeftAlone()
    {
        var rules = new[] { Allow("facebook.com", "Application"), Block("youtube.com") };

        var result = PolicyDecision.WithWebsiteAllowlist(rules);

        Assert.Equal(rules, result);
        Assert.False(PolicyDecision.IsBlocked(result, "wikipedia.org", isDomain: true));
        Assert.True(PolicyDecision.IsBlocked(result, "youtube.com", isDomain: true));
    }

    [Fact]
    public void TheCatchAllIsAddedOnce_AndOnlyForWebsites()
    {
        var once = PolicyDecision.WithWebsiteAllowlist(new[] { Allow("example.com") });
        var twice = PolicyDecision.WithWebsiteAllowlist(once);

        var catchAll = Assert.Single(twice, rule => rule.Target == PolicyDecision.EveryWebsite);
        Assert.Equal("Website", catchAll.RuleType);
        Assert.Equal("Block", catchAll.Mode);
    }

    // Whitelisting facebook.com alone gave a page of bare links: its styles,
    // images and scripts come from fbcdn.net. A well-known site brings the
    // helper domains it cannot display without.
    [Theory]
    [InlineData("static.xx.fbcdn.net", false)]
    [InlineData("scontent.fmnl4-1.fna.fbcdn.net", false)]
    [InlineData("connect.facebook.net", false)]
    [InlineData("www.roblox.com", true)]
    [InlineData("ytimg.com", true)]            // YouTube's helper, and YouTube is not whitelisted
    public void AWhitelistedSiteBringsItsHelperDomains(string domain, bool blocked)
    {
        var rules = new[] { Allow("facebook.com"), Block("youtube.com") };

        Assert.Equal(blocked, BlockedUnderWhitelist(rules, domain));
    }

    [Fact]
    public void AHelperDomainThatIsBlacklistedStaysBlocked()
    {
        var rules = new[] { Allow("facebook.com"), Block("fbcdn.net") };

        Assert.True(BlockedUnderWhitelist(rules, "static.xx.fbcdn.net"));
        Assert.False(BlockedUnderWhitelist(rules, "www.facebook.com"));
    }

    [Theory]
    [InlineData("facebook.com", "fbcdn.net")]
    [InlineData("www.facebook.com", "fbcdn.net")]
    [InlineData("classroom.google.com", "gstatic.com")]
    [InlineData("*.khanacademy.org", "kastatic.org")]
    public void CompanionsFollowTheSiteAndItsSubdomains(string target, string helper)
    {
        Assert.Contains(helper, PolicyDecision.CompanionsFor(target));
    }

    [Fact]
    public void AnUnknownSiteHasNoCompanions()
    {
        Assert.Empty(PolicyDecision.CompanionsFor("deped.gov.ph"));
        Assert.Empty(PolicyDecision.CompanionsFor("notfacebook.com"));
    }

    // An allow rule whose target cannot be matched opens nothing, so it must not
    // close everything else either.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://")]
    [InlineData("not a website")]
    public void AnUnusableAllowTargetDoesNotSwitchTheWhitelistOn(string target)
    {
        var result = PolicyDecision.WithWebsiteAllowlist(new[] { Allow(target) });

        Assert.DoesNotContain(result, rule => rule.Target == PolicyDecision.EveryWebsite);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoTargetMatchesNothing(string? target)
    {
        Assert.Null(PolicyDecision.FindMatch(new[] { Block("example.com") }, target, isDomain: true));
        Assert.False(PolicyDecision.IsBlocked(new[] { Block("example.com") }, target, isDomain: true));
    }
}
