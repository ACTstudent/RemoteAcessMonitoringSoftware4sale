namespace Shared.Contracts;

public static class WebsiteDomainNormalizer
{
    public static bool TryNormalize(string? value, out string domain)
    {
        domain = string.Empty;
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 2048)
            return false;

        Uri? uri;
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteUri))
        {
            if (absoluteUri.Scheme != Uri.UriSchemeHttp && absoluteUri.Scheme != Uri.UriSchemeHttps)
                return false;
            uri = absoluteUri;
        }
        else
        {
            if (candidate.Contains(':') ||
                !Uri.TryCreate($"https://{candidate}", UriKind.Absolute, out uri))
                return false;
        }

        if (string.IsNullOrWhiteSpace(uri.DnsSafeHost))
            return false;

        var host = uri.DnsSafeHost.TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || host.Any(char.IsControl) || host.Contains('/'))
            return false;

        domain = host;
        return true;
    }
}
