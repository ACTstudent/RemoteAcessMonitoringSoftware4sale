using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;

namespace Server.Services;

public sealed class TelemetryService : ITelemetryService
{
    private const int MaxTimestampAgeDays = 7;
    private readonly ApplicationDbContext _db;

    public TelemetryService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task RecordApplicationUsageAsync(string connectionId, string studentId, string pcName,
        string applicationName, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var values = ValidateIdentity(connectionId, studentId, pcName, timestamp);
        var app = Required(applicationName, 300, nameof(applicationName));

        _db.UsageLogs.Add(new UsageLog
        {
            StudentId = int.TryParse(values.StudentId, out var parsedStudentId) ? parsedStudentId : null,
            PcName = values.PcName,
            AppName = app,
            Timestamp = values.Timestamp
        });
        await RecordActivityEventCoreAsync(values, "ApplicationUsed", app, null, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordIdleStatusAsync(string connectionId, string studentId, string pcName,
        bool isIdle, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var values = ValidateIdentity(connectionId, studentId, pcName, timestamp);
        var open = await _db.IdleIntervals
            .Where(interval => interval.ConnectionId == values.ConnectionId && interval.EndedAt == null)
            .OrderByDescending(interval => interval.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (isIdle && open is null)
        {
            _db.IdleIntervals.Add(new IdleInterval
            {
                ConnectionId = values.ConnectionId,
                StudentId = values.StudentId,
                PcName = values.PcName,
                StartedAt = values.Timestamp
            });
        }
        else if (!isIdle && open is not null)
        {
            open.EndedAt = values.Timestamp;
        }

        await RecordActivityEventCoreAsync(values, isIdle ? "IdleStarted" : "IdleEnded", null, null, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordActivityEventAsync(string connectionId, string studentId, string pcName,
        string eventType, string? applicationName = null, string? details = null,
        DateTime? timestamp = null, CancellationToken cancellationToken = default)
    {
        var values = ValidateIdentity(connectionId, studentId, pcName, timestamp ?? DateTime.UtcNow);
        var type = Required(eventType, 50, nameof(eventType));
        var app = Optional(applicationName, 300, nameof(applicationName));
        var boundedDetails = Optional(details, 1000, nameof(details));
        await RecordActivityEventCoreAsync(values, type, app, boundedDetails, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordWebsiteUsageAsync(string connectionId, string studentId, string pcName,
        string domain, string browser, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var values = ValidateIdentity(connectionId, studentId, pcName, timestamp);
        _db.WebsiteUsageLogs.Add(new WebsiteUsageLog
        {
            StudentId = int.TryParse(values.StudentId, out var id) ? id : null,
            Domain = Required(domain, 300, nameof(domain)),
            Browser = Required(browser, 50, nameof(browser)),
            Timestamp = values.Timestamp
        });
        await RecordActivityEventCoreAsync(values, "WebsiteUsed", null, domain, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
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

    private sealed record Identity(string ConnectionId, string StudentId, string PcName, DateTime Timestamp);
}
