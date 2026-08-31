using Shared.Contracts;

namespace Server.Services;

public interface ITelemetryService
{
    Task RecordApplicationUsageAsync(string connectionId, string studentId, string pcName,
        string applicationName, DateTime timestamp, CancellationToken cancellationToken = default);

    Task RecordIdleStatusAsync(string connectionId, string studentId, string pcName,
        bool isIdle, DateTime timestamp, CancellationToken cancellationToken = default);

    Task RecordDisconnectedAsync(string connectionId, string studentId, string pcName,
        string? details = null, DateTime? timestamp = null, CancellationToken cancellationToken = default);

    Task RecordActivityEventAsync(string connectionId, string studentId, string pcName,
        string eventType, string? applicationName = null, string? details = null,
        DateTime? timestamp = null, CancellationToken cancellationToken = default);

    Task RecordWebsiteUsageAsync(string connectionId, string studentId, string pcName,
        string domain, string browser, DateTime timestamp, CancellationToken cancellationToken = default);

    Task RecordBrowserMonitoringStatusAsync(BrowserMonitoringStatusMessage status,
        CancellationToken cancellationToken = default);

    Task RecordBatchAsync(IReadOnlyList<TelemetryBatchItem> items,
        CancellationToken cancellationToken = default);
}
