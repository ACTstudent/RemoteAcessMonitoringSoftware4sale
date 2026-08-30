using System.Text.Json.Serialization;

namespace Shared.Contracts;

public sealed record TelemetryBatchItem(
    IdleStatusMessage? IdleStatus = null,
    ActiveAppMessage? ActiveApp = null,
    WebsiteActivityMessage? WebsiteActivity = null,
    BrowserMonitoringStatusMessage? BrowserMonitoringStatus = null)
{
    [JsonIgnore]
    public int PayloadCount =>
        (IdleStatus is null ? 0 : 1) +
        (ActiveApp is null ? 0 : 1) +
        (WebsiteActivity is null ? 0 : 1) +
        (BrowserMonitoringStatus is null ? 0 : 1);

    public static TelemetryBatchItem From(IdleStatusMessage status) => new(IdleStatus: status);
    public static TelemetryBatchItem From(ActiveAppMessage app) => new(ActiveApp: app);
    public static TelemetryBatchItem From(WebsiteActivityMessage website) => new(WebsiteActivity: website);
    public static TelemetryBatchItem From(BrowserMonitoringStatusMessage status) => new(BrowserMonitoringStatus: status);
}

public sealed record TelemetryBatchMessage(IReadOnlyList<TelemetryBatchItem> Items);

public sealed record TelemetryBatchResult(int ProcessedCount);

public static class TelemetryValueNormalizer
{
    public static bool TryNormalizeApplicationName(string? value, out string applicationName)
    {
        applicationName = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();
        var titleSeparator = candidate.IndexOf(" - ", StringComparison.Ordinal);
        if (titleSeparator >= 0)
            candidate = candidate[..titleSeparator].Trim();

        var pathSeparator = Math.Max(candidate.LastIndexOf('/'), candidate.LastIndexOf('\\'));
        if (pathSeparator >= 0)
            candidate = candidate[(pathSeparator + 1)..].Trim();

        if (candidate.Length is 0 or > 300 || candidate.Any(char.IsControl))
            return false;

        applicationName = candidate;
        return true;
    }
}
