using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;

namespace Server.Services;

public interface IAnalyticsService
{
    Task<StudentAnalyticsReport?> GetStudentReportAsync(int studentId, int teacherId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityTimelineItem>> GetActivityTimelineAsync(int studentId, int teacherId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MonitoringAlert>> GetAlertsAsync(int teacherId, bool includeAcknowledged = false, CancellationToken cancellationToken = default);
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
        var timeline = await GetActivityTimelineAsync(studentId, teacherId, range.From, range.To, cancellationToken);
        var alerts = await _db.MonitoringAlerts.AsNoTracking()
            .Where(a => a.StudentId == studentId.ToString() && a.CreatedAt >= range.From && a.CreatedAt <= range.To)
            .OrderByDescending(a => a.CreatedAt).ToListAsync(cancellationToken);
        var durations = await CalculateDurationsAsync(studentId, range.From, range.To, cancellationToken);
        return new StudentAnalyticsReport(student, range.From, range.To, durations, timeline, alerts);
    }

    public async Task<IReadOnlyList<ActivityTimelineItem>> GetActivityTimelineAsync(int studentId, int teacherId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (await GetAccessibleStudentAsync(studentId, teacherId, cancellationToken) is null) return Array.Empty<ActivityTimelineItem>();
        var range = NormalizeRange(from, to);
        return await _db.ActivityEvents.AsNoTracking()
            .Where(e => e.StudentId == studentId.ToString() && e.Timestamp >= range.From && e.Timestamp <= range.To)
            .OrderByDescending(e => e.Timestamp)
            .Select(e => new ActivityTimelineItem(e.Timestamp, e.EventType, e.ApplicationName, e.Details, e.PcName))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MonitoringAlert>> GetAlertsAsync(int teacherId, bool includeAcknowledged = false, CancellationToken cancellationToken = default)
    {
        var studentIds = await AccessibleStudentIds(teacherId, cancellationToken);
        var query = _db.MonitoringAlerts.AsNoTracking().Where(a => studentIds.Contains(a.StudentId));
        if (!includeAcknowledged) query = query.Where(a => !a.IsAcknowledged);
        return await query.OrderByDescending(a => a.CreatedAt).Take(500).ToListAsync(cancellationToken);
    }

    public async Task<bool> SetAlertAcknowledgedAsync(int alertId, int teacherId, bool acknowledged, CancellationToken cancellationToken = default)
    {
        var studentIds = await AccessibleStudentIds(teacherId, cancellationToken);
        var alert = await _db.MonitoringAlerts.FirstOrDefaultAsync(a => a.MonitoringAlertId == alertId && studentIds.Contains(a.StudentId), cancellationToken);
        if (alert is null) return false;
        alert.IsAcknowledged = acknowledged;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<DurationSummary> CalculateDurationsAsync(int studentId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var sessions = await _db.LabSessions.AsNoTracking().Where(s => s.StudentId == studentId && s.StartTime < to && (s.EndTime ?? to) > from).ToListAsync(cancellationToken);
        var idle = await _db.IdleIntervals.AsNoTracking().Where(i => i.StudentId == studentId.ToString() && i.StartedAt < to && (i.EndedAt ?? to) > from).ToListAsync(cancellationToken);
        var events = await _db.ActivityEvents.AsNoTracking().Where(e => e.StudentId == studentId.ToString() && e.Timestamp >= from && e.Timestamp <= to).OrderBy(e => e.Timestamp).ToListAsync(cancellationToken);
        var sessionDuration = sessions.Sum(s => Overlap(s.StartTime, s.EndTime ?? to, from, to).TotalSeconds);
        var idleDuration = idle.Sum(i => Overlap(i.StartedAt, i.EndedAt ?? to, from, to).TotalSeconds);
        var application = DurationFor(events, "ApplicationUsed", from, to);
        var website = DurationFor(events, "WebsiteUsed", from, to);
        var idleSpan = TimeSpan.FromSeconds(Math.Min(sessionDuration, idleDuration));
        return new DurationSummary(TimeSpan.FromSeconds(application), TimeSpan.FromSeconds(website), idleSpan, TimeSpan.FromSeconds(Math.Max(0, sessionDuration - idleDuration)));
    }

    private static double DurationFor(IReadOnlyList<ActivityEvent> events, string type, DateTime from, DateTime to)
    {
        double seconds = 0;
        var matching = events.Where(e => e.EventType == type).ToList();
        for (var i = 0; i < matching.Count; i++)
        {
            var end = i + 1 < matching.Count ? matching[i + 1].Timestamp : to;
            seconds += Math.Max(0, (Overlap(matching[i].Timestamp, end, from, to)).TotalSeconds);
        }
        return seconds;
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
