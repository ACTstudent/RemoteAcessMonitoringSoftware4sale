namespace Shared.Contracts;

public enum BrowserMonitoringMode
{
    ManagedProtocol,
    WindowTitleFallback,
    Unavailable
}

/// <summary>
/// How a collection mode reads to a person.
///
/// Here rather than in the server because the agent shows this too, on the
/// student's own screen. They had drifted: the portal said "Page titles only"
/// while the agent said "fallback" for the same state, so a teacher and a
/// student looking at the same thing could not describe it to each other.
/// </summary>
public static class BrowserMonitoringLabels
{
    public static string For(BrowserMonitoringMode mode) => mode switch
    {
        BrowserMonitoringMode.ManagedProtocol => "Full addresses",
        BrowserMonitoringMode.WindowTitleFallback => "Page titles only",
        BrowserMonitoringMode.Unavailable => "Not being recorded",
        _ => "Unknown"
    };
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
