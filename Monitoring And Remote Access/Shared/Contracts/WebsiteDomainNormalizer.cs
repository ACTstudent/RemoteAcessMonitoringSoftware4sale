namespace Shared.Contracts;

public static class WebsiteDomainNormalizer
{
    public static bool TryNormalize(string? value, out string domain)
    {
        domain = string.Empty;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.DnsSafeHost))
            return false;

        var host = uri.DnsSafeHost.TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || host.Any(char.IsControl) || host.Contains('/'))
            return false;

        domain = host;
        return true;
    }
}
