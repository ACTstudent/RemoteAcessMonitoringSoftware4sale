using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Server.Data;
using Server.Models;

namespace Server.Services;

public sealed class TelemetryRetentionCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<TelemetryRetentionOptions> _options;
    private readonly ILogger<TelemetryRetentionCleanupService> _logger;

    public TelemetryRetentionCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<TelemetryRetentionOptions> options,
        ILogger<TelemetryRetentionCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            if (options.Enabled)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var deleted = await TelemetryRetentionCleaner.CleanupAsync(
                        db, options, DateTime.UtcNow, stoppingToken);
                    if (deleted > 0)
                        _logger.LogInformation("Deleted {Count} expired telemetry and remote-log records.", deleted);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Telemetry retention cleanup failed.");
                }
            }

            try
            {
                var interval = TimeSpan.FromMinutes(Math.Clamp(options.CleanupIntervalMinutes, 1, 24 * 60));
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

public static class TelemetryRetentionCleaner
{
    public static async Task<int> CleanupAsync(
        ApplicationDbContext db,
        TelemetryRetentionOptions options,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
            return 0;

        utcNow = utcNow.Kind switch
        {
            DateTimeKind.Local => utcNow.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
            _ => utcNow
        };
        var batchSize = Math.Clamp(options.BatchSize, 1, 500);
        var deleted = 0;

        if (options.ActivityEventDays > 0 || options.ConnectionLogDays > 0)
        {
            var activityCutoff = utcNow.AddDays(-Math.Max(1, options.ActivityEventDays));
            var connectionCutoff = utcNow.AddDays(-Math.Max(1, options.ConnectionLogDays));
            Expression<Func<ActivityEvent, bool>> predicate = options.ActivityEventDays > 0 && options.ConnectionLogDays > 0
                ? activity => ((activity.EventType == "Connected" || activity.EventType == "Disconnected") &&
                        activity.Timestamp < connectionCutoff) ||
                    (activity.EventType != "Connected" && activity.EventType != "Disconnected" &&
                        activity.Timestamp < activityCutoff)
                : options.ActivityEventDays > 0
                    ? activity => activity.EventType != "Connected" && activity.EventType != "Disconnected" &&
                        activity.Timestamp < activityCutoff
                    : activity => (activity.EventType == "Connected" || activity.EventType == "Disconnected") &&
                        activity.Timestamp < connectionCutoff;
            deleted += await DeleteInBatchesAsync(
                db.ActivityEvents, predicate, nameof(ActivityEvent.ActivityEventId), batchSize, cancellationToken);
        }

        if (options.UsageLogDays > 0)
        {
            var cutoff = utcNow.AddDays(-options.UsageLogDays);
            deleted += await DeleteInBatchesAsync(db.UsageLogs, log => log.Timestamp < cutoff,
                nameof(UsageLog.UsageLogId), batchSize, cancellationToken);
        }

        if (options.WebsiteUsageLogDays > 0)
        {
            var cutoff = utcNow.AddDays(-options.WebsiteUsageLogDays);
            deleted += await DeleteInBatchesAsync(db.WebsiteUsageLogs, log => log.Timestamp < cutoff,
                nameof(WebsiteUsageLog.WebsiteUsageLogId), batchSize, cancellationToken);
        }

        if (options.BrowserMonitoringDays > 0)
        {
            var cutoff = utcNow.AddDays(-options.BrowserMonitoringDays);
            deleted += await DeleteInBatchesAsync(db.BrowserMonitoringRecords, record => record.Timestamp < cutoff,
                nameof(BrowserMonitoringRecord.BrowserMonitoringRecordId), batchSize, cancellationToken);
        }

        if (options.IdleIntervalDays > 0)
        {
            var cutoff = utcNow.AddDays(-options.IdleIntervalDays);
            deleted += await DeleteInBatchesAsync(db.IdleIntervals,
                interval => (interval.EndedAt ?? interval.StartedAt) < cutoff,
                nameof(IdleInterval.IdleIntervalId), batchSize, cancellationToken);
        }

        if (options.MonitoringAlertDays > 0)
        {
            var cutoff = utcNow.AddDays(-options.MonitoringAlertDays);
            deleted += await DeleteInBatchesAsync(db.MonitoringAlerts, alert => alert.LastSeenAt < cutoff,
                nameof(MonitoringAlert.MonitoringAlertId), batchSize, cancellationToken);
        }

        if (options.RemoteLogDays > 0)
        {
            var cutoff = utcNow.AddDays(-options.RemoteLogDays);
            deleted += await DeleteInBatchesAsync(db.RemoteCommandLogs, log => log.Timestamp < cutoff,
                nameof(RemoteCommandLog.RemoteCommandLogId), batchSize, cancellationToken);
            deleted += await DeleteInBatchesAsync(db.RemoteControlSessions,
                session => !session.IsActive && (session.EndedAt ?? session.StartedAt) < cutoff,
                nameof(RemoteControlSession.RemoteControlSessionId), batchSize, cancellationToken);
        }

        return deleted;
    }

    private static async Task<int> DeleteInBatchesAsync<TEntity>(
        DbSet<TEntity> set,
        Expression<Func<TEntity, bool>> predicate,
        string keyProperty,
        int batchSize,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var deleted = 0;
        while (true)
        {
            var ids = await set.AsNoTracking()
                .Where(predicate)
                .OrderBy(entity => EF.Property<int>(entity, keyProperty))
                .Select(entity => EF.Property<int>(entity, keyProperty))
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0)
                return deleted;

            var batchDeleted = await set
                .Where(entity => ids.Contains(EF.Property<int>(entity, keyProperty)))
                .ExecuteDeleteAsync(cancellationToken);
            deleted += batchDeleted;
            if (batchDeleted == 0)
                return deleted;
        }
    }
}
