namespace Shared.Contracts;

/// <summary>
/// One place to decide what a set of restriction rules says about an
/// application or a domain.
/// </summary>
/// <remarks>
/// The rule is the one the Whitelist page states: an allow rule is an exception
/// that takes precedence over matching categories and blacklist entries.
/// Nothing is blocked merely for being absent from the rules.
///
/// This decision existed as three copies - application matching, website
/// matching and the session proxy - and they drifted. Two of them read a single
/// allow rule as "deny everything else", so one allowed application closed every
/// other window on the desktop, and one allowed site blocked the rest of the web.
/// </remarks>
public static class PolicyDecision
{
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
