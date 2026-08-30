using Microsoft.EntityFrameworkCore;
using Server.Models;
using Server.Services;

namespace Server.Tests.Services;

public class CategoryPolicyEngineTests
{
    private readonly CategoryPolicyEngine _engine = new();

    [Fact]
    public void ApplicationCategory_BlocksMatchingApplication()
    {
        var result = _engine.EvaluateApplication("game.exe", [new ApplicationCategory { Name = "Games", Pattern = "*.exe" }]);

        Assert.False(result.IsAllowed);
        Assert.Equal("Games", result.CategoryName);
    }

    [Fact]
    public void WebsiteCategory_MatchesSubdomainsAndUrls()
    {
        var result = _engine.EvaluateWebsite("https://www.example.test/path", [new WebsiteCategory { Name = "Example", DomainPattern = "example.test" }]);

        Assert.False(result.IsAllowed);
        Assert.Equal("Example", result.CategoryName);
    }

    [Fact]
    public void ExplicitAllowRule_TakesPrecedenceOverCategory()
    {
        var result = _engine.EvaluateApplication(
            "editor.exe",
            [new ApplicationCategory { Name = "Blocked tools", Pattern = "editor.exe" }],
            [new RestrictionRule { RuleType = "Application", Target = "editor.exe", Mode = "Allow", IsActive = true }]);

        Assert.True(result.IsAllowed);
        Assert.Equal("editor.exe", result.MatchedTarget);
    }

    [Fact]
    public void NoMatch_AllowsTargetAndInactiveCategoryIsIgnored()
    {
        var result = _engine.EvaluateApplication("notepad.exe", [new ApplicationCategory { Name = "Disabled", Pattern = "notepad.exe", IsActive = false }]);

        Assert.True(result.IsAllowed);
        Assert.Null(result.CategoryName);
    }

    [Fact]
    public void MoreSpecificAllowRuleBeatsBroadBlockRule()
    {
        var result = _engine.EvaluateWebsite("safe.example.test", [], [
            new RestrictionRule { RuleType = "Website", Target = "*.example.test", Mode = "Block" },
            new RestrictionRule { RuleType = "Website", Target = "safe.example.test", Mode = "Allow" }]);
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task CategoryDbSets_PersistCategories()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Server.Data.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new Server.Data.ApplicationDbContext(options);
        db.ApplicationCategories.Add(new ApplicationCategory { Name = "Games", Pattern = "game.exe" });
        db.WebsiteCategories.Add(new WebsiteCategory { Name = "Social", DomainPattern = "social.test" });
        await db.SaveChangesAsync();

        Assert.Equal("Games", (await db.ApplicationCategories.SingleAsync()).Name);
        Assert.Equal("social.test", (await db.WebsiteCategories.SingleAsync()).DomainPattern);
    }
}
