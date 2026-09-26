namespace Shared.Contracts;

public static class PolicyPatternMatcher
{
    public static bool MatchesApplication(string? applicationName, string? pattern) =>
        MatchesWildcard(NormalizeApplication(applicationName), NormalizeApplication(pattern), requireDomainBoundary: false);

    public static bool MatchesDomain(string? domain, string? pattern) =>
        WebsiteDomainNormalizer.TryNormalize(domain, out var host) &&
        MatchesWildcard(host, NormalizeDomainPattern(pattern), requireDomainBoundary: true);

    public static string? NormalizeApplication(string? value)
    {
        var candidate = value?.Trim().Trim('"');
        if (string.IsNullOrEmpty(candidate)) return null;
        // Process.ProcessName has no path or .exe suffix, unlike common rule inputs.
        var separator = candidate.LastIndexOfAny(new[] { '/', '\\' });
        if (separator >= 0) candidate = candidate[(separator + 1)..];
        return candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? candidate[..^4] : candidate;
    }

    public static string? NormalizeDomainPattern(string? value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrEmpty(candidate)) return null;
        if (!candidate.Contains('*'))
            return WebsiteDomainNormalizer.TryNormalize(candidate, out var domain) ? domain : null;

        // Keep wildcard host rules intact while removing URL scheme/path/query.
        if (candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) candidate = candidate[8..];
        else if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) candidate = candidate[7..];
        var end = candidate.IndexOfAny(new[] { '/', '?', '#' });
        if (end >= 0) candidate = candidate[..end];
        var port = candidate.LastIndexOf(':');
        if (port >= 0)
        {
            if (!int.TryParse(candidate[(port + 1)..], out var number) || number < 1 || number > 65535) return null;
            candidate = candidate[..port];
        }
        if (candidate.Length == 0 || candidate.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || candidate.Contains('@')) return null;
        return candidate.TrimEnd('.').ToLowerInvariant();
    }

    // Generic suffixes seen on school and everyday sites; any two-letter
    // suffix counts as a country code.
    private static readonly HashSet<string> CommonDomainSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "net", "org", "edu", "gov", "mil", "int", "info", "biz", "app", "dev", "io", "ai",
        "online", "site", "xyz", "tech", "store", "shop", "blog", "news", "live", "media", "cloud",
        "games", "school", "academy", "education", "art", "page", "world", "top"
    };

    /// <summary>
    /// True when a rule target is plainly a web address - a scheme, a "www."
    /// prefix, or a dotted name ending in a domain suffix - so it cannot mean an
    /// application. "facebook.com" saved as an application whitelist entry did
    /// nothing at all, and read as a whitelist that did not work.
    /// Program names such as "chrome.exe", "notepad" or "Microsoft.Photos" are not.
    /// </summary>
    public static bool LooksLikeWebsite(string? target)
    {
        var candidate = target?.Trim();
        if (string.IsNullOrEmpty(candidate)) return false;
        if (candidate.Contains("://", StringComparison.Ordinal) ||
            candidate.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return true;
        if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            candidate.IndexOfAny(new[] { '\\', ' ' }) >= 0) return false;

        var host = candidate.TrimStart('*', '.').Split('/', '?', '#')[0];
        var lastDot = host.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == host.Length - 1) return false;
        var suffix = host[(lastDot + 1)..];
        return suffix.All(char.IsLetter) && (suffix.Length == 2 || CommonDomainSuffixes.Contains(suffix));
    }

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
