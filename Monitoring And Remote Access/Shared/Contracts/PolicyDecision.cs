namespace Shared.Contracts;

/// <summary>
/// One place to decide what a set of restriction rules says about an
/// application or a domain.
/// </summary>
/// <remarks>
/// Within a rule set the most specific matching rule decides, and nothing is
/// blocked merely for being absent from it. The website whitelist ("allow only
/// these") is not a second decision here: the server adds a catch-all block
/// beneath the rules - see <see cref="WithWebsiteAllowlist"/> - so this one
/// decision covers both.
///
/// This decision existed as three copies - application matching, website
/// matching and the session proxy - and they drifted. Two of them read a single
/// allow rule as "deny everything else" for applications too, so one allowed
/// application closed every other window on the desktop.
/// </remarks>
public static class PolicyDecision
{
    /// <summary>The catch-all a website whitelist adds; it matches every domain and is the least specific rule there is.</summary>
    public const string EveryWebsite = "*";

    /// <summary>
    /// Domains a well-known site cannot display without: its images, styles,
    /// scripts and video are served from elsewhere. Whitelisting facebook.com
    /// alone gave a page of bare links, because everything it draws with comes
    /// from fbcdn.net. A whitelisted site brings these along; a site not listed
    /// here may need its helper domains whitelisted by hand.
    /// </summary>
    private static readonly Dictionary<string, string[]> Companions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["facebook.com"] = new[] { "fbcdn.net", "facebook.net", "fbsbx.com" },
        ["messenger.com"] = new[] { "fbcdn.net", "facebook.net", "fbsbx.com" },
        ["instagram.com"] = new[] { "cdninstagram.com", "fbcdn.net", "facebook.net" },
        ["youtube.com"] = new[] { "ytimg.com", "googlevideo.com", "ggpht.com", "youtube-nocookie.com", "gstatic.com", "googleapis.com" },
        ["google.com"] = new[] { "gstatic.com", "googleapis.com", "googleusercontent.com", "ggpht.com" },
        ["khanacademy.org"] = new[] { "kastatic.org" },
        ["wikipedia.org"] = new[] { "wikimedia.org" }
    };

    /// <summary>The helper domains whitelisting <paramref name="target"/> also allows; empty for sites not in the list.</summary>
    public static IReadOnlyList<string> CompanionsFor(string? target)
    {
        var domain = PolicyPatternMatcher.NormalizeDomainPattern(target)?.TrimStart('*', '.');
        if (string.IsNullOrEmpty(domain)) return Array.Empty<string>();
        foreach (var (site, helpers) in Companions)
        {
            if (domain.Equals(site, StringComparison.OrdinalIgnoreCase) ||
                domain.EndsWith("." + site, StringComparison.OrdinalIgnoreCase))
                return helpers;
        }
        return Array.Empty<string>();
    }

    /// <summary>
    /// The whitelist as a teacher reads it: once any website is allowed, only
    /// allowed websites open. Returns <paramref name="rules"/> with a catch-all
    /// website block appended when an allow rule for a website is among them,
    /// and unchanged otherwise.
    /// </summary>
    /// <remarks>
    /// Expressed as a rule rather than a flag so every client enforces it with
    /// the precedence it already has - clients installed before this existed
    /// included. A whitelisted site is more specific than "*" and opens; a
    /// blacklist entry more specific than a whitelisted site still blocks it.
    /// Applications are never allow-listed this way: application rules are
    /// recorded but not enforced.
    /// </remarks>
    public static List<RestrictionRuleMessage> WithWebsiteAllowlist(IEnumerable<RestrictionRuleMessage> rules)
    {
        var result = rules.ToList();
        var allowedSites = result
            .Where(rule => string.Equals(rule.RuleType, "Website", StringComparison.OrdinalIgnoreCase) &&
                IsAllow(rule.Mode) &&
                PolicyPatternMatcher.NormalizeDomainPattern(rule.Target) is not null)
            .Select(rule => rule.Target)
            .ToList();
        if (allowedSites.Count == 0) return result;

        // The helper domains of whitelisted sites - unless a rule names that
        // domain itself, in which case the rule stands as written.
        var named = new HashSet<string>(
            result.Select(rule => PolicyPatternMatcher.NormalizeDomainPattern(rule.Target) ?? ""),
            StringComparer.OrdinalIgnoreCase);
        foreach (var helper in allowedSites.SelectMany(CompanionsFor).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (named.Add(helper))
                result.Add(new RestrictionRuleMessage(0, "Website", helper, "Allow"));
        }

        if (!result.Any(IsEveryWebsiteBlock))
            result.Add(new RestrictionRuleMessage(0, "Website", EveryWebsite, "Block"));
        return result;
    }

    private static bool IsEveryWebsiteBlock(RestrictionRuleMessage rule) =>
        string.Equals(rule.RuleType, "Website", StringComparison.OrdinalIgnoreCase) &&
        rule.Target?.Trim() == EveryWebsite && !IsAllow(rule.Mode);

    /// <summary>The rule that governs <paramref name="target"/>, or null when no rule mentions it.</summary>
    public static RestrictionRuleMessage? FindMatch(
        IEnumerable<RestrictionRuleMessage> rules, string? target, bool isDomain)
    {
        if (rules is null || string.IsNullOrWhiteSpace(target)) return null;

        return rules
            .Where(rule => isDomain
                ? PolicyPatternMatcher.MatchesDomain(target, rule.Target)
                : PolicyPatternMatcher.MatchesApplication(target, rule.Target))
            // The most specific rule wins, and an allow beats a block of equal
            // specificity. Putting allow first instead would let a broad allow
            // such as "example.com" unblock a targeted "bad.example.com".
            // CategoryPolicyEngine on the server orders the same way.
            .OrderByDescending(rule => rule.Target?.Count(character => character != '*') ?? 0)
            .ThenByDescending(rule => IsAllow(rule.Mode))
            .FirstOrDefault();
    }

    /// <summary>True only when a rule matches and that rule blocks.</summary>
    public static bool IsBlocked(IEnumerable<RestrictionRuleMessage> rules, string? target, bool isDomain) =>
        FindMatch(rules, target, isDomain) is { } match && !IsAllow(match.Mode);

    private static bool IsAllow(string? mode) =>
        string.Equals(mode?.Trim(), "Allow", StringComparison.OrdinalIgnoreCase);
}
