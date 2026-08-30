using Shared.Contracts;

namespace Server.Tests.Contracts;

public sealed class PolicyPatternMatcherTests
{
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("portal.example.com", "example.com")]
    [InlineData("portal.example.com", "*.example.com")]
    [InlineData("school.example.org", "school.*")]
    public void MatchesDomain_AcceptsExactSubdomainAndWildcardMatches(string domain, string pattern)
    {
        Assert.True(PolicyPatternMatcher.MatchesDomain(domain, pattern));
    }

    [Theory]
    [InlineData("notexample.com", "example.com")]
    [InlineData("example.org", "*.example.com")]
    [InlineData("example.com.evil.test", "example.com")]
    public void MatchesDomain_RejectsLookalikeDomains(string domain, string pattern)
    {
        Assert.False(PolicyPatternMatcher.MatchesDomain(domain, pattern));
    }

    [Theory]
    [InlineData("CHROME.EXE", "chrome*")]
    [InlineData("student-editor.exe", "editor")]
    public void MatchesApplication_IsCaseInsensitiveAndSupportsWildcards(string application, string pattern)
    {
        Assert.True(PolicyPatternMatcher.MatchesApplication(application, pattern));
    }
}
