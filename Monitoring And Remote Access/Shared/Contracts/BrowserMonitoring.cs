namespace Shared.Contracts;

public enum BrowserMonitoringMode
{
    ManagedProtocol,
    WindowTitleFallback,
    Unavailable
}

public sealed record BrowserMonitoringStatusMessage(
    string Browser,
    BrowserMonitoringMode Mode,
    DateTime Timestamp,
    string? Detail = null);
