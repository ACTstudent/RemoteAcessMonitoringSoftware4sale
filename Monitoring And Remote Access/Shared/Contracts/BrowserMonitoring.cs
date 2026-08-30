namespace Shared.Contracts;

public enum BrowserMonitoringMode
{
    ManagedProtocol,
    WindowTitleFallback,
    Unavailable
}

public sealed record BrowserMonitoringStatusMessage(
    string ConnectionId,
    string StudentId,
    string PcName,
    string Browser,
    BrowserMonitoringMode Mode,
    DateTime Timestamp,
    string? Detail = null)
{
    public static string? NormalizeDetail(string? detail)
    {
        var value = detail?.Trim();
        return value is "Foreground URL captured" or
            "Foreground browser detected; URL unavailable" or
            "Managed endpoint available" or
            "Managed process starting" or
            "Managed browser unavailable"
            ? value
            : null;
    }
}
