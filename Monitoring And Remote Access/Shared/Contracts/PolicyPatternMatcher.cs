namespace Shared.Contracts;

public static class PolicyPatternMatcher
{
    public static bool MatchesApplication(string? applicationName, string? pattern) =>
        MatchesWildcard(applicationName, pattern, requireDomainBoundary: false);

    public static bool MatchesDomain(string? domain, string? pattern) =>
        MatchesWildcard(domain, pattern, requireDomainBoundary: true);

    private static bool MatchesWildcard(string? value, string? pattern, bool requireDomainBoundary)
    {
        value = value?.Trim().ToLowerInvariant();
        pattern = pattern?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(pattern)) return false;

        if (!pattern.Contains('*'))
        {
            return requireDomainBoundary
                ? value == pattern || value.EndsWith($".{pattern}", StringComparison.Ordinal)
                : value.Contains(pattern, StringComparison.Ordinal);
        }

        var parts = pattern.Split('*', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var position = 0;
        foreach (var part in parts)
        {
            var match = value.IndexOf(part, position, StringComparison.Ordinal);
            if (match < 0) return false;
            position = match + part.Length;
        }
        if (!pattern.StartsWith('*') && !value.StartsWith(parts.FirstOrDefault() ?? string.Empty, StringComparison.Ordinal)) return false;
        return pattern.EndsWith('*') || value.EndsWith(parts.LastOrDefault() ?? string.Empty, StringComparison.Ordinal);
    }
}
