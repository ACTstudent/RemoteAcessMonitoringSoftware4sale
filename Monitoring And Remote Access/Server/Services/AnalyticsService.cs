using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;

namespace Server.Services;

public interface IAnalyticsService
{
    Task<StudentAnalyticsReport?> GetStudentReportAsync(int studentId, int teacherId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<PagedResult<ActivityTimelineItem>> GetActivityTimelineAsync(int studentId, int teacherId, DateTime from, DateTime to, int page = 1, int pageSize = 100, string? eventType = null, CancellationToken cancellationToken = default);
    Task<PagedResult<MonitoringAlert>> GetAlertsAsync(int teacherId, bool includeAcknowledged = false, DateTime? from = null, DateTime? to = null, string? severity = null, string? studentId = null, int page = 1, int pageSize = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AlertHistoryItem>> GetAlertHistoryAsync(int alertId, int teacherId, CancellationToken cancellationToken = default);
    Task<bool> SetAlertAcknowledgedAsync(int alertId, int teacherId, bool acknowledged, CancellationToken cancellationToken = default);
}

public sealed class AnalyticsService : IAnalyticsService
{
    private readonly ApplicationDbContext _db;

    public AnalyticsService(ApplicationDbContext db) => _db = db;

    public async Task<StudentAnalyticsReport?> GetStudentReportAsync(int studentId, int teacherId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var student = await GetAccessibleStudentAsync(studentId, teacherId, cancellationToken);
        if (student is null) return null;
        var range = NormalizeRange(from, to);
        var timeline = await GetActivityTimelineAsync(studentId, teacherId, range.From, range.To, 1, 5000, null, cancellationToken);
        var alerts = await _db.MonitoringAlerts.AsNoTracking()
            .Where(a => a.StudentId == studentId.ToString() && a.CreatedAt >= range.From && a.CreatedAt < range.To)
            .OrderByDescending(a => a.CreatedAt).ToListAsync(cancellationToken);
        var durations = await CalculateDurationsAsync(studentId, range.From, range.To, cancellationToken);
        return new StudentAnalyticsReport(student, range.From, range.To, durations, timeline.Items, alerts);
    }

    public async Task<PagedResult<ActivityTimelineItem>> GetActivityTimelineAsync(int studentId, int teacherId, DateTime from, DateTime to, int page = 1, int pageSize = 100, string? eventType = null, CancellationToken cancellationToken = default)
    {
        if (await GetAccessibleStudentAsync(studentId, teacherId, cancellationToken) is null) return new PagedResult<ActivityTimelineItem>(Array.Empty<ActivityTimelineItem>(), 1, pageSize, 0);
        var range = NormalizeRange(from, to);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 500);
        var query = _db.ActivityEvents.AsNoTracking()
            .Where(e => e.StudentId == studentId.ToString() && e.Timestamp >= range.From && e.Timestamp < range.To)
            .Where(e => string.IsNullOrEmpty(eventType) || e.EventType == eventType);
        var total = await query.CountAsync(cancellationToken);
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var items = await query.OrderByDescending(e => e.Timestamp).ThenByDescending(e => e.ActivityEventId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new ActivityTimelineItem(e.Timestamp, e.EventType, e.ApplicationName, e.Details, e.PcName)).ToListAsync(cancellationToken);
        return new PagedResult<ActivityTimelineItem>(items, page, pageSize, total);
    }

    public async Task<PagedResult<MonitoringAlert>> GetAlertsAsync(int teacherId, bool includeAcknowledged = false, DateTime? from = null, DateTime? to = null, string? severity = null, string? studentId = null, int page = 1, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        var studentIds = await AccessibleStudentIds(teacherId, cancellationToken);
        var query = _db.MonitoringAlerts.AsNoTracking().Where(a => studentIds.Contains(a.StudentId));
        if (!includeAcknowledged) query = query.Where(a => !a.IsAcknowledged);
        if (from.HasValue) query = query.Where(a => a.CreatedAt >= from.Value.Date);
        if (to.HasValue) query = query.Where(a => a.CreatedAt < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(severity)) query = query.Where(a => a.Severity == severity);
        if (!string.IsNullOrWhiteSpace(studentId)) query = query.Where(a => a.StudentId == studentId);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 500);
        var total = await query.CountAsync(cancellationToken);
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var items = await query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.MonitoringAlertId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<MonitoringAlert>(items, page, pageSize, total);
    }

    public async Task<bool> SetAlertAcknowledgedAsync(int alertId, int teacherId, bool acknowledged, CancellationToken cancellationToken = default)
    {
        var studentIds = await AccessibleStudentIds(teacherId, cancellationToken);
        var alert = await _db.MonitoringAlerts.FirstOrDefaultAsync(a => a.MonitoringAlertId == alertId && studentIds.Contains(a.StudentId), cancellationToken);
        if (alert is null) return false;
        if (alert.IsAcknowledged == acknowledged) return true;
        alert.IsAcknowledged = acknowledged;
        _db.AuditLogs.Add(new AuditLog
        {
            UserType = "Teacher", UserId = teacherId,
            Action = acknowledged ? "AcknowledgeAlert" : "ReopenAlert",
            Details = $"Alert {alertId} for student {alert.StudentId} changed to {(acknowledged ? "Acknowledged" : "Open")}",
            Timestamp = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AlertHistoryItem>> GetAlertHistoryAsync(int alertId, int teacherId, CancellationToken cancellationToken = default)
    {
        var studentIds = await AccessibleStudentIds(teacherId, cancellationToken);
        if (!await _db.MonitoringAlerts.AnyAsync(a => a.MonitoringAlertId == alertId && studentIds.Contains(a.StudentId), cancellationToken)) return Array.Empty<AlertHistoryItem>();
        return await _db.AuditLogs.AsNoTracking().Where(l => l.UserType == "Teacher" && l.UserId == teacherId &&
                (l.Action == "AcknowledgeAlert" || l.Action == "ReopenAlert") && l.Details.Contains($"Alert {alertId} "))
            .OrderByDescending(l => l.Timestamp)
            .Select(l => new AlertHistoryItem(alertId, l.Action, l.UserId, l.Timestamp, l.Details)).ToListAsync(cancellationToken);
    }

    private async Task<DurationSummary> CalculateDurationsAsync(int studentId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var sessions = await _db.LabSessions.AsNoTracking().Where(s => s.StudentId == studentId && s.StartTime < to && (s.EndTime ?? to) > from).ToListAsync(cancellationToken);
        var idle = await _db.IdleIntervals.AsNoTracking().Where(i => i.StudentId == studentId.ToString() && i.StartedAt < to && (i.EndedAt ?? to) > from).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var sessionRanges = Merge(sessions.Select(s => (s.StartTime, s.EndTime ?? (s.Status == "Paused" && s.PauseTime.HasValue ? s.PauseTime.Value : now))));
        var idleRanges = Merge(idle.Select(i => (i.StartedAt, i.EndedAt ?? now)));
        var events = await _db.ActivityEvents.AsNoTracking().Where(e => e.StudentId == studentId.ToString() && e.Timestamp <= to).OrderBy(e => e.Timestamp).ToListAsync(cancellationToken);
        var sessionDuration = sessionRanges.Sum(r => Overlap(r.Start, r.End, from, to).TotalSeconds);
        var idleDuration = idleRanges.Sum(r => Overlap(r.Start, r.End, from, to).TotalSeconds);
        var application = DurationFor(events, "ApplicationUsed", from, to, sessionRanges);
        var website = DurationFor(events, "WebsiteUsed", from, to, sessionRanges);
        var idleSpan = TimeSpan.FromSeconds(Math.Min(sessionDuration, idleDuration));
        return new DurationSummary(TimeSpan.FromSeconds(application), TimeSpan.FromSeconds(website), idleSpan, TimeSpan.FromSeconds(Math.Max(0, sessionDuration - idleDuration)));
    }

    private static double DurationFor(IReadOnlyList<ActivityEvent> events, string type, DateTime from, DateTime to, IReadOnlyList<(DateTime Start, DateTime End)> activeRanges)
    {
        double seconds = 0;
        var matching = events.Where(e => e.EventType == type).ToList();
        for (var i = 0; i < matching.Count; i++)
        {
            var end = i + 1 < matching.Count ? matching[i + 1].Timestamp : to;
            seconds += activeRanges.Sum(r => Overlap(matching[i].Timestamp, end, r.Start, r.End).TotalSeconds);
        }
        return seconds;
    }

    private static IReadOnlyList<(DateTime Start, DateTime End)> Merge(IEnumerable<(DateTime Start, DateTime End)> ranges)
    {
        var result = new List<(DateTime Start, DateTime End)>();
        foreach (var range in ranges.Where(r => r.End > r.Start).OrderBy(r => r.Start))
        {
            if (result.Count == 0 || range.Start > result[^1].End) result.Add(range);
            else if (range.End > result[^1].End) result[^1] = (result[^1].Start, range.End);
        }
        return result;
    }

    private async Task<Student?> GetAccessibleStudentAsync(int studentId, int teacherId, CancellationToken cancellationToken) =>
        await _db.Students.AsNoTracking().Include(s => s.Class).FirstOrDefaultAsync(s => s.Id == studentId && (s.AdviserId == teacherId || _db.Classes.Any(c => c.TeacherId == teacherId && !c.IsArchived && (c.ClassId == s.ClassId || _db.ClassStudents.Any(cs => cs.ClassId == c.ClassId && cs.StudentId == s.Id)))), cancellationToken);

    private async Task<List<string>> AccessibleStudentIds(int teacherId, CancellationToken cancellationToken) =>
        await _db.Students.Where(s => s.AdviserId == teacherId || _db.Classes.Any(c => c.TeacherId == teacherId && !c.IsArchived && (c.ClassId == s.ClassId || _db.ClassStudents.Any(cs => cs.ClassId == c.ClassId && cs.StudentId == s.Id)))).Select(s => s.Id.ToString()).ToListAsync(cancellationToken);

    private static (DateTime From, DateTime To) NormalizeRange(DateTime from, DateTime to)
    {
        if (from == default) from = DateTime.UtcNow.Date;
        if (to == default) to = from.Date.AddDays(1).AddTicks(-1);
        if (to < from) (from, to) = (to, from);
        return (DateTime.SpecifyKind(from, DateTimeKind.Utc), DateTime.SpecifyKind(to, DateTimeKind.Utc));
    }

    private static TimeSpan Overlap(DateTime start, DateTime end, DateTime from, DateTime to)
    {
        var clippedStart = start < from ? from : start;
        var clippedEnd = end > to ? to : end;
        return clippedEnd > clippedStart ? clippedEnd - clippedStart : TimeSpan.Zero;
    }
}
