using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Shared.Contracts;

namespace Server.Services;

public sealed class TelemetryService : ITelemetryService
{
    private const int MaxTimestampAgeDays = 7;
    private const int MaxBatchSize = 50;
    private readonly ApplicationDbContext _db;
    private readonly Dictionary<string, int?> _studentIds = new(StringComparer.OrdinalIgnoreCase);

    public TelemetryService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task RecordApplicationUsageAsync(string connectionId, string studentId, string pcName,
        string applicationName, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var values = ValidateIdentity(connectionId, studentId, pcName, timestamp);
        var app = NormalizeApplicationName(applicationName);

        await AddApplicationUsageCoreAsync(values, app, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordIdleStatusAsync(string connectionId, string studentId, string pcName,
        bool isIdle, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var values = ValidateIdentity(connectionId, studentId, pcName, timestamp);
        await AddIdleStatusCoreAsync(values, isIdle, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordActivityEventAsync(string connectionId, string studentId, string pcName,
        string eventType, string? applicationName = null, string? details = null,
        DateTime? timestamp = null, CancellationToken cancellationToken = default)
    {
        var values = ValidateIdentity(connectionId, studentId, pcName, timestamp ?? DateTime.UtcNow);
        var type = Required(eventType, 50, nameof(eventType));
        var app = applicationName is null ? null : NormalizeApplicationName(applicationName);
        var boundedDetails = Optional(details, 1000, nameof(details));
        await RecordActivityEventCoreAsync(values, type, app, boundedDetails, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordWebsiteUsageAsync(string connectionId, string studentId, string pcName,
        string domain, string browser, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var values = ValidateIdentity(connectionId, studentId, pcName, timestamp);
        var normalizedDomain = NormalizeDomain(domain);
        var normalizedBrowser = Required(browser, 50, nameof(browser)).ToLowerInvariant();
        await AddWebsiteUsageCoreAsync(values, normalizedDomain, normalizedBrowser, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordBrowserMonitoringStatusAsync(
        BrowserMonitoringStatusMessage status,
        CancellationToken cancellationToken = default)
    {
        AddBrowserMonitoringStatus(status);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordBatchAsync(IReadOnlyList<TelemetryBatchItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count > MaxBatchSize)
            throw new ArgumentException($"Telemetry batches are limited to {MaxBatchSize} items.", nameof(items));

        foreach (var item in items)
            ValidateBatchItem(item);

        foreach (var item in items)
        {
            if (item.IdleStatus is { } idle)
            {
                var values = ValidateIdentity(idle.ConnectionId, idle.StudentId, idle.PcName, idle.Timestamp);
                await AddIdleStatusCoreAsync(values, idle.IsIdle, cancellationToken);
            }
            else if (item.ActiveApp is { } app)
            {
                var values = ValidateIdentity(app.ConnectionId, app.StudentId, app.PcName, app.Timestamp);
                await AddApplicationUsageCoreAsync(values, NormalizeApplicationName(app.ApplicationName), cancellationToken);
            }
            else if (item.WebsiteActivity is { } website)
            {
                var values = ValidateIdentity(website.ConnectionId, website.StudentId, website.PcName, website.Timestamp);
                await AddWebsiteUsageCoreAsync(values, NormalizeDomain(website.Domain),
                    Required(website.Browser, 50, nameof(website.Browser)).ToLowerInvariant(), cancellationToken);
            }
            else if (item.BrowserMonitoringStatus is { } browserStatus)
            {
                AddBrowserMonitoringStatus(browserStatus);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private void AddBrowserMonitoringStatus(BrowserMonitoringStatusMessage status)
    {
        var values = ValidateIdentity(status.ConnectionId, status.StudentId, status.PcName, status.Timestamp);
        var browser = Required(status.Browser, 50, nameof(status.Browser)).ToLowerInvariant();
        if (!Enum.IsDefined(status.Mode)) throw new ArgumentException("The browser monitoring mode is invalid.", nameof(status));
        _db.BrowserMonitoringRecords.Add(new BrowserMonitoringRecord
        {
            ConnectionId = values.ConnectionId,
            StudentId = values.StudentId,
            PcName = values.PcName,
            Browser = browser,
            Mode = status.Mode,
            Detail = BrowserMonitoringStatusMessage.NormalizeDetail(status.Detail),
            Timestamp = values.Timestamp
        });
    }

    private async Task AddApplicationUsageCoreAsync(Identity values, string applicationName,
        CancellationToken cancellationToken)
    {
        _db.UsageLogs.Add(new UsageLog
        {
            StudentId = await ResolveStudentIdAsync(values.StudentId, cancellationToken),
            PcName = values.PcName,
            AppName = applicationName,
            Timestamp = values.Timestamp
        });
        await RecordActivityEventCoreAsync(values, "ApplicationUsed", applicationName, null, cancellationToken);
    }

    private async Task AddIdleStatusCoreAsync(Identity values, bool isIdle, CancellationToken cancellationToken)
    {
        var tracked = _db.IdleIntervals.Local
            .Where(interval => interval.ConnectionId == values.ConnectionId &&
                _db.Entry(interval).State != EntityState.Deleted)
            .ToList();
        var open = tracked
            .Where(interval => interval.EndedAt == null)
            .OrderByDescending(interval => interval.StartedAt)
            .FirstOrDefault();
        if (open is null && tracked.Count == 0)
        {
            open = await _db.IdleIntervals
                .Where(interval => interval.ConnectionId == values.ConnectionId && interval.EndedAt == null)
                .OrderByDescending(interval => interval.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var stateChanged = false;
        if (isIdle && open is null)
        {
            _db.IdleIntervals.Add(new IdleInterval
            {
                ConnectionId = values.ConnectionId,
                StudentId = values.StudentId,
                PcName = values.PcName,
                StartedAt = values.Timestamp
            });
            stateChanged = true;
        }
        else if (!isIdle && open is not null && values.Timestamp >= open.StartedAt)
        {
            open.EndedAt = values.Timestamp;
            stateChanged = true;
        }

        if (stateChanged)
            await RecordActivityEventCoreAsync(values, isIdle ? "IdleStarted" : "IdleEnded", null, null, cancellationToken);
    }

    private async Task AddWebsiteUsageCoreAsync(Identity values, string domain, string browser,
        CancellationToken cancellationToken)
    {
        _db.WebsiteUsageLogs.Add(new WebsiteUsageLog
        {
            StudentId = await ResolveStudentIdAsync(values.StudentId, cancellationToken),
            Domain = domain,
            Browser = browser,
            Timestamp = values.Timestamp
        });
        await RecordActivityEventCoreAsync(values, "WebsiteUsed", null, domain, cancellationToken);
    }

    private async Task<int?> ResolveStudentIdAsync(string studentNumber, CancellationToken cancellationToken)
    {
        if (_studentIds.TryGetValue(studentNumber, out var cached)) return cached;
        var id = await _db.Students.AsNoTracking()
            .Where(student => student.StudentNumber == studentNumber)
            .Select(student => (int?)student.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!id.HasValue && int.TryParse(studentNumber, out var legacyId)) id = legacyId;
        _studentIds[studentNumber] = id;
        return id;
    }

    private static void ValidateBatchItem(TelemetryBatchItem? item)
    {
        if (item is null || item.PayloadCount != 1)
            throw new ArgumentException("Each telemetry batch item must contain exactly one payload.", nameof(item));

        if (item.IdleStatus is { } idle)
            _ = ValidateIdentity(idle.ConnectionId, idle.StudentId, idle.PcName, idle.Timestamp);
        else if (item.ActiveApp is { } app)
        {
            _ = ValidateIdentity(app.ConnectionId, app.StudentId, app.PcName, app.Timestamp);
            _ = NormalizeApplicationName(app.ApplicationName);
        }
        else if (item.WebsiteActivity is { } website)
        {
            _ = ValidateIdentity(website.ConnectionId, website.StudentId, website.PcName, website.Timestamp);
            _ = NormalizeDomain(website.Domain);
            _ = Required(website.Browser, 50, nameof(website.Browser));
        }
        else if (item.BrowserMonitoringStatus is { } browserStatus)
        {
            _ = ValidateIdentity(browserStatus.ConnectionId, browserStatus.StudentId, browserStatus.PcName, browserStatus.Timestamp);
            _ = Required(browserStatus.Browser, 50, nameof(browserStatus.Browser));
            if (!Enum.IsDefined(browserStatus.Mode)) throw new ArgumentException("The browser monitoring mode is invalid.", nameof(item));
            _ = BrowserMonitoringStatusMessage.NormalizeDetail(browserStatus.Detail);
        }
    }

    private Task RecordActivityEventCoreAsync(Identity values, string eventType, string? applicationName,
        string? details, CancellationToken cancellationToken)
    {
        _db.ActivityEvents.Add(new ActivityEvent
        {
            ConnectionId = values.ConnectionId,
            StudentId = values.StudentId,
            PcName = values.PcName,
            EventType = eventType,
            ApplicationName = applicationName,
            Details = details,
            Timestamp = values.Timestamp
        });
        return Task.CompletedTask;
    }

    private static Identity ValidateIdentity(string connectionId, string studentId, string pcName, DateTime timestamp)
    {
        var now = DateTime.UtcNow;
        if (timestamp.Kind == DateTimeKind.Local)
            timestamp = timestamp.ToUniversalTime();
        else if (timestamp.Kind == DateTimeKind.Unspecified)
            timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
        if (timestamp > now.AddMinutes(5) || timestamp < now.AddDays(-MaxTimestampAgeDays))
            throw new ArgumentOutOfRangeException(nameof(timestamp), "Telemetry timestamps must be recent and not in the future.");

        return new Identity(Required(connectionId, 100, nameof(connectionId)),
            Required(studentId, 100, nameof(studentId)), Required(pcName, 100, nameof(pcName)), timestamp);
    }

    private static string Required(string value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new ArgumentException($"{name} is required and must be at most {maxLength} characters.", name);
        return value.Trim();
    }

    private static string? Optional(string? value, int maxLength, string name)
    {
        if (value is null) return null;
        if (value.Length > maxLength)
            throw new ArgumentException($"{name} must be at most {maxLength} characters.", name);
        return value.Trim();
    }

    private static string NormalizeApplicationName(string value)
    {
        if (!TelemetryValueNormalizer.TryNormalizeApplicationName(value, out var applicationName))
            throw new ArgumentException("applicationName is required and must identify an application, not window content.", nameof(value));
        return applicationName;
    }

    private static string NormalizeDomain(string value)
    {
        if (!WebsiteDomainNormalizer.TryNormalize(value, out var domain) || domain.Length > 300)
            throw new ArgumentException("domain must be a valid website domain.", nameof(value));
        return domain;
    }

    private sealed record Identity(string ConnectionId, string StudentId, string PcName, DateTime Timestamp);
}
