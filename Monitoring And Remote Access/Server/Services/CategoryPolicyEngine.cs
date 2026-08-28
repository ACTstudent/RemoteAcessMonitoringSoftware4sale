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
        var target = application?.Trim() ?? string.Empty;
        var explicitDecision = EvaluateExplicit(target, "Application", rules, blacklist);
        if (explicitDecision is not null)
            return explicitDecision;

        var category = categories.FirstOrDefault(c => c.IsActive && Matches(target, c.Pattern));
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

        var category = categories.FirstOrDefault(c => c.IsActive && MatchesDomain(target, c.DomainPattern));
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
        var activeRules = rules?.Where(r => r.IsActive &&
            (r.RuleType.Equals(targetType, StringComparison.OrdinalIgnoreCase) ||
             (targetType == "Website" && r.RuleType.Equals("BlockWebsite", StringComparison.OrdinalIgnoreCase)) ||
             (targetType == "Application" && r.RuleType.Equals("BlockApplication", StringComparison.OrdinalIgnoreCase)))) ?? Enumerable.Empty<RestrictionRule>();

        var allowRule = activeRules.FirstOrDefault(r => Matches(target, r.Target) && !IsBlock(r.Mode));
        if (allowRule is not null)
            return new CategoryPolicyDecision(true, null, allowRule.Target);

        var blockRule = activeRules.FirstOrDefault(r => Matches(target, r.Target) && IsBlock(r.Mode));
        if (blockRule is not null)
            return new CategoryPolicyDecision(false, null, blockRule.Target);

        var blockedItem = blacklist?.FirstOrDefault(item => item.IsActive &&
            (item.TargetType.Equals(targetType, StringComparison.OrdinalIgnoreCase) ||
             (targetType == "Website" && item.TargetType.Equals("Domain", StringComparison.OrdinalIgnoreCase))) &&
            Matches(target, item.Value));
        return blockedItem is null ? null : new CategoryPolicyDecision(false, null, blockedItem.Value);
    }

    private static bool IsBlock(string? mode) => !string.Equals(mode, "Allow", StringComparison.OrdinalIgnoreCase);

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
