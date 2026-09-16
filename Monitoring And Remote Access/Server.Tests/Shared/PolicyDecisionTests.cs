using Shared.Contracts;

namespace Server.Tests.Contracts;

public sealed class PolicyDecisionTests
{
    private static RestrictionRuleMessage Allow(string target, string type = "Website") => new(1, type, target, "Allow");
    private static RestrictionRuleMessage Block(string target, string type = "Website") => new(2, type, target, "Block");

    // The bug this guards: one allow rule used to mean "deny everything else", so
    // whitelisting a single site blocked the rest of the web, and whitelisting a
    // single application closed every other window on the desktop.
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
