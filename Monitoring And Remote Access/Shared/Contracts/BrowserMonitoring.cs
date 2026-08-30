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
    string? Detail = null);
