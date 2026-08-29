using Server.Models;

namespace Server.Services;

public sealed record CategoryPolicyDecision(bool IsAllowed, string? CategoryName = null, string? MatchedTarget = null);

/// <summary>
/// Evaluates category policies without changing the existing monitoring pipeline.
/// Callers provide the already-loaded records, making this usable with any persistence strategy.
/// </summary>
public sealed class CategoryPolicyEngine
{
    public CategoryPolicyDecision EvaluateApplication(
        string application,
        IEnumerable<ApplicationCategory> categories,
        IEnumerable<RestrictionRule>? rules = null,
        IEnumerable<BlacklistItem>? blacklist = null)
    {
        var target = NormalizeApplication(application);
        var explicitDecision = EvaluateExplicit(target, "Application", rules, blacklist);
        if (explicitDecision is not null)
            return explicitDecision;

        var category = categories
            .Where(c => c.IsActive && Matches(target, NormalizeApplication(c.Pattern)))
            .OrderByDescending(c => Specificity(NormalizeApplication(c.Pattern)))
            .ThenByDescending(c => IsAllow(c.Mode))
            .FirstOrDefault();
        return category is null
            ? new CategoryPolicyDecision(true)
            : new CategoryPolicyDecision(!IsBlock(category.Mode), category.Name, category.Pattern);
    }

    public CategoryPolicyDecision EvaluateWebsite(
        string website,
        IEnumerable<WebsiteCategory> categories,
        IEnumerable<RestrictionRule>? rules = null,
        IEnumerable<BlacklistItem>? blacklist = null)
    {
        var target = NormalizeDomain(website);
        var explicitDecision = EvaluateExplicit(target, "Website", rules, blacklist);
        if (explicitDecision is not null)
            return explicitDecision;

        var category = categories
            .Where(c => c.IsActive && MatchesDomain(target, c.DomainPattern))
            .OrderByDescending(c => Specificity(NormalizeDomain(c.DomainPattern)))
            .ThenByDescending(c => IsAllow(c.Mode))
            .FirstOrDefault();
        return category is null
            ? new CategoryPolicyDecision(true)
            : new CategoryPolicyDecision(!IsBlock(category.Mode), category.Name, category.DomainPattern);
    }

    private static CategoryPolicyDecision? EvaluateExplicit(
        string target,
        string targetType,
        IEnumerable<RestrictionRule>? rules,
        IEnumerable<BlacklistItem>? blacklist)
    {
        var matchingRule = rules?
            .Where(r => r.IsActive && IsRuleType(r.RuleType, targetType) && MatchesTarget(target, targetType, r.Target))
            .OrderByDescending(r => Specificity(NormalizedTarget(targetType, r.Target)))
            .ThenByDescending(r => IsAllow(r.Mode))
            .FirstOrDefault();
        if (matchingRule is not null)
            return new CategoryPolicyDecision(!IsBlock(matchingRule.Mode), null, matchingRule.Target);

        var blockedItem = blacklist?.FirstOrDefault(item => item.IsActive &&
            IsBlacklistType(item.TargetType, targetType) &&
            MatchesTarget(target, targetType, item.Value));
        return blockedItem is null ? null : new CategoryPolicyDecision(false, null, blockedItem.Value);
    }

    private static bool IsBlock(string? mode) => !string.Equals(mode, "Allow", StringComparison.OrdinalIgnoreCase);
    private static bool IsAllow(string? mode) => string.Equals(mode?.Trim(), "Allow", StringComparison.OrdinalIgnoreCase);

    private static bool IsRuleType(string? ruleType, string targetType) =>
        string.Equals(ruleType?.Trim(), targetType, StringComparison.OrdinalIgnoreCase) ||
        (targetType == "Website" && string.Equals(ruleType?.Trim(), "BlockWebsite", StringComparison.OrdinalIgnoreCase)) ||
        (targetType == "Application" && string.Equals(ruleType?.Trim(), "BlockApplication", StringComparison.OrdinalIgnoreCase));

    private static bool IsBlacklistType(string? itemType, string targetType) =>
        string.Equals(itemType?.Trim(), targetType, StringComparison.OrdinalIgnoreCase) ||
        (targetType == "Website" && string.Equals(itemType?.Trim(), "Domain", StringComparison.OrdinalIgnoreCase)) ||
        (targetType == "Application" && string.Equals(itemType?.Trim(), "Process", StringComparison.OrdinalIgnoreCase));

    private static bool Matches(string value, string pattern)
    {
        var normalizedPattern = pattern.Trim();
        if (normalizedPattern.Length == 0)
            return false;
        if (!normalizedPattern.Contains('*'))
            return value.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase);

        var parts = normalizedPattern.Split('*');
        return value.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase) &&
            value.EndsWith(parts[^1], StringComparison.OrdinalIgnoreCase) &&
            value.Length >= parts[0].Length + parts[^1].Length;
    }

    private static bool MatchesTarget(string target, string targetType, string? pattern) =>
        targetType == "Website" ? MatchesDomain(target, pattern ?? string.Empty) : Matches(target, NormalizeApplication(pattern));

    private static string NormalizedTarget(string targetType, string? pattern) =>
        targetType == "Website" ? NormalizeDomain(pattern ?? string.Empty) : NormalizeApplication(pattern);

    private static string NormalizeApplication(string? value) => (value ?? string.Empty).Trim();

    private static int Specificity(string? pattern) =>
        (pattern ?? string.Empty).Count(c => c != '*' && !char.IsWhiteSpace(c));

    private static bool MatchesDomain(string domain, string pattern)
    {
        var normalizedPattern = NormalizeDomain(pattern);
        return Matches(domain, normalizedPattern) ||
            (normalizedPattern.Length > 0 && domain.EndsWith("." + normalizedPattern, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeDomain(string value)
    {
        var candidate = (value ?? string.Empty).Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            candidate = uri.Host;
        else
            candidate = candidate.Split('/')[0].Split(':')[0];
        return candidate.TrimEnd('.').ToLowerInvariant();
    }
}
