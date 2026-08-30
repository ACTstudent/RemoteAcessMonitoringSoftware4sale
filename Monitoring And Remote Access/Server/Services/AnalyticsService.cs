using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;

namespace Server.Services;

public interface IAnalyticsService
{
    Task<StudentAnalyticsReport?> GetStudentReportAsync(int studentId, int teacherId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<PagedResult<ActivityTimelineItem>> GetActivityTimelineAsync(int studentId, int teacherId, DateTime from, DateTime to, int page = 1, int pageSize = 100, string? eventType = null, CancellationToken cancellationToken = default);
    Task<PagedResult<MonitoringAlert>> GetAlertsAsync(int teacherId, bool includeAcknowledged = false, DateTime? from = null, DateTime? to = null, string? severity = null, string? studentId = null, int page = 1, int pageSize = 100, CancellationToken cancellationToken = default);
    Task<PagedResult<MonitoringAlert>> GetAlertsAsync(int teacherId, MonitoringAlertFilter filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AlertHistoryItem>> GetAlertHistoryAsync(int alertId, int teacherId, CancellationToken cancellationToken = default);
    Task<bool> SetAlertAcknowledgedAsync(int alertId, int teacherId, bool acknowledged, CancellationToken cancellationToken = default);
    Task<AlertBulkActionResult> AcknowledgeAlertsAsync(IReadOnlyCollection<int> alertIds, int teacherId, CancellationToken cancellationToken = default);
    Task<AlertBulkActionResult> DismissAlertsAsync(IReadOnlyCollection<int> alertIds, int teacherId, string? reason = null, CancellationToken cancellationToken = default);
    Task<AlertBulkActionResult> ReopenAlertsAsync(IReadOnlyCollection<int> alertIds, int teacherId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MonitoringAlert>> GetAlertExportAsync(int teacherId, DateTime? from = null, DateTime? to = null, string? severity = null, string? studentId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MonitoringAlert>> GetAlertExportAsync(int teacherId, MonitoringAlertFilter filter, CancellationToken cancellationToken = default);
    Task<ClassAnalyticsReport?> GetClassReportAsync(int classId, int teacherId, DateTime from, DateTime to, string? station = null, CancellationToken cancellationToken = default);
    Task<LabUtilizationReport?> GetLabUtilizationAsync(int teacherId, DateTime from, DateTime to, string? station = null, int? classId = null, CancellationToken cancellationToken = default);
    Task<UnifiedTimelineReport?> GetUnifiedTimelineAsync(int teacherId, UnifiedTimelineFilter filter, CancellationToken cancellationToken = default);
    Task<PagedResult<RemoteHistoryItem>> GetRemoteHistoryAsync(int teacherId, DateTime? from = null, DateTime? to = null, string? command = null, string? studentId = null, int page = 1, int pageSize = 100, CancellationToken cancellationToken = default);
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
        var alertRows = await _db.MonitoringAlerts.AsNoTracking()
            .Where(a => a.StudentId == student.StudentNumber)
            .ToListAsync(cancellationToken);
        var alerts = SummarizeAlerts(alertRows)
            .Where(a => a.LastSeenAt >= range.From && a.FirstSeenAt < range.To)
            .OrderByDescending(a => a.LastSeenAt)
            .ToList();
        var durations = await CalculateDurationsAsync(studentId, student.StudentNumber, range.From, range.To, cancellationToken);
        return new StudentAnalyticsReport(student, range.From, range.To, durations, timeline.Items, alerts);
    }

    public async Task<PagedResult<ActivityTimelineItem>> GetActivityTimelineAsync(int studentId, int teacherId, DateTime from, DateTime to, int page = 1, int pageSize = 100, string? eventType = null, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);
        var student = await GetAccessibleStudentAsync(studentId, teacherId, cancellationToken);
        if (student is null)
            return new PagedResult<ActivityTimelineItem>(Array.Empty<ActivityTimelineItem>(), 1, pageSize, 0);

        var range = NormalizeRange(from, to);
        var query = _db.ActivityEvents.AsNoTracking()
            .Where(e => e.StudentId == student.StudentNumber && e.Timestamp >= range.From && e.Timestamp < range.To)
            .Where(e => string.IsNullOrEmpty(eventType) || e.EventType == eventType);
        var total = await query.CountAsync(cancellationToken);
        page = NormalizePage(page, pageSize, total);
        var items = await query.OrderByDescending(e => e.Timestamp).ThenByDescending(e => e.ActivityEventId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new ActivityTimelineItem(e.Timestamp, e.EventType, e.ApplicationName, e.Details, e.PcName))
            .ToListAsync(cancellationToken);
        return new PagedResult<ActivityTimelineItem>(items, page, pageSize, total);
    }

    public Task<PagedResult<MonitoringAlert>> GetAlertsAsync(int teacherId, bool includeAcknowledged = false, DateTime? from = null, DateTime? to = null, string? severity = null, string? studentId = null, int page = 1, int pageSize = 100, CancellationToken cancellationToken = default) =>
        GetAlertsAsync(teacherId, new MonitoringAlertFilter(from, to, severity, studentId, Status: includeAcknowledged ? null : MonitoringAlertStatus.Open, Page: page, PageSize: pageSize), cancellationToken);

    public async Task<PagedResult<MonitoringAlert>> GetAlertsAsync(int teacherId, MonitoringAlertFilter filter, CancellationToken cancellationToken = default)
    {
        var groups = await QueryAlertGroupsAsync(teacherId, filter, cancellationToken);
        var pageSize = Math.Clamp(filter.PageSize, 1, 500);
        var page = NormalizePage(filter.Page, pageSize, groups.Count);
        var items = groups.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<MonitoringAlert>(items, page, pageSize, groups.Count);
    }

    public async Task<bool> SetAlertAcknowledgedAsync(int alertId, int teacherId, bool acknowledged, CancellationToken cancellationToken = default)
    {
        var result = acknowledged
            ? await AcknowledgeAlertsAsync(new[] { alertId }, teacherId, cancellationToken)
            : await ReopenAlertsAsync(new[] { alertId }, teacherId, cancellationToken);
        return result.MatchedGroupCount > 0;
    }

    public Task<AlertBulkActionResult> AcknowledgeAlertsAsync(IReadOnlyCollection<int> alertIds, int teacherId, CancellationToken cancellationToken = default) =>
        ChangeAlertStatusAsync(alertIds, teacherId, MonitoringAlertStatus.Acknowledged, null, cancellationToken);

    public Task<AlertBulkActionResult> DismissAlertsAsync(IReadOnlyCollection<int> alertIds, int teacherId, string? reason = null, CancellationToken cancellationToken = default)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason?.Length > 500) throw new ArgumentException("Dismissal reasons must be at most 500 characters.", nameof(reason));
        return ChangeAlertStatusAsync(alertIds, teacherId, MonitoringAlertStatus.Dismissed, reason, cancellationToken);
    }

    public Task<AlertBulkActionResult> ReopenAlertsAsync(IReadOnlyCollection<int> alertIds, int teacherId, CancellationToken cancellationToken = default) =>
        ChangeAlertStatusAsync(alertIds, teacherId, MonitoringAlertStatus.Open, null, cancellationToken);

    public Task<IReadOnlyList<MonitoringAlert>> GetAlertExportAsync(int teacherId, DateTime? from = null, DateTime? to = null, string? severity = null, string? studentId = null, CancellationToken cancellationToken = default) =>
        GetAlertExportAsync(teacherId, new MonitoringAlertFilter(from, to, severity, studentId, Status: null), cancellationToken);

    public async Task<IReadOnlyList<MonitoringAlert>> GetAlertExportAsync(int teacherId, MonitoringAlertFilter filter, CancellationToken cancellationToken = default) =>
        await QueryAlertGroupsAsync(teacherId, filter with { Page = 1, PageSize = 500 }, cancellationToken);

    public async Task<IReadOnlyList<AlertHistoryItem>> GetAlertHistoryAsync(int alertId, int teacherId, CancellationToken cancellationToken = default)
    {
        var accessibleIds = await AccessibleStudentIds(teacherId, cancellationToken);
        var alerts = await _db.MonitoringAlerts.AsNoTracking()
            .Where(a => accessibleIds.Contains(a.StudentId))
            .ToListAsync(cancellationToken);
        var selected = alerts.FirstOrDefault(a => a.MonitoringAlertId == alertId);
        if (selected is null) return Array.Empty<AlertHistoryItem>();

        var group = alerts.Where(a => a.GroupKey == selected.GroupKey).ToList();
        var groupIds = group.Select(a => a.MonitoringAlertId).ToArray();
        var lifecycleActions = new[] { "AcknowledgeAlert", "DismissAlert", "ReopenAlert" };
        var auditRows = await _db.AuditLogs.AsNoTracking()
            .Where(l => l.UserType == "Teacher" && lifecycleActions.Contains(l.Action))
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync(cancellationToken);
        var history = auditRows
            .Where(l => groupIds.Any(id => l.Details.Contains($"Alert {id} ", StringComparison.Ordinal)))
            .Select(l => new AlertHistoryItem(alertId, l.Action, l.UserId, l.Timestamp, l.Details))
            .ToList();
        var firstSeen = group.Min(a => a.FirstSeenAt);
        history.Add(new AlertHistoryItem(alertId, "Created", null, firstSeen,
            $"Alert group first seen for student {selected.StudentId} on {selected.PcName}."));
        return history.OrderByDescending(h => h.Timestamp).ToList();
    }

    public async Task<ClassAnalyticsReport?> GetClassReportAsync(int classId, int teacherId, DateTime from, DateTime to, string? station = null, CancellationToken cancellationToken = default)
    {
        var cls = await _db.Classes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClassId == classId && !c.IsArchived && c.TeacherId == teacherId &&
                                      (c.Status == "Active" || string.IsNullOrEmpty(c.Status)), cancellationToken);
        if (cls is null) return null;

        var range = NormalizeRange(from, to);
        var enrolledStudents = await _db.Students.AsNoTracking()
            .Where(s => s.ClassId == classId || _db.ClassStudents.Any(cs => cs.ClassId == classId && cs.StudentId == s.Id))
            .Select(s => new { s.Id, s.StudentNumber, s.FullName })
            .ToListAsync(cancellationToken);
        var enrolledStudentIds = enrolledStudents.Select(s => s.Id).ToList();
        var query = _db.LabSessions.AsNoTracking().Include(s => s.Student).Include(s => s.Computer)
            .Where(s => enrolledStudentIds.Contains(s.StudentId) && s.StartTime < range.To && (s.EndTime ?? range.To) > range.From);
        if (!string.IsNullOrWhiteSpace(station))
            query = query.Where(s => s.PCName == station || (s.Computer != null && s.Computer.LaboratoryStation == station));
        var sessions = await query.ToListAsync(cancellationToken);
        var studentNumbers = enrolledStudents.Select(student => student.StudentNumber).ToList();
        var alerts = await _db.MonitoringAlerts.AsNoTracking()
            .Where(a => studentNumbers.Contains(a.StudentId) && a.CreatedAt >= range.From && a.CreatedAt < range.To)
            .ToListAsync(cancellationToken);
        return new ClassAnalyticsReport(cls, range.From, range.To, sessions.Count,
            sessions.Sum(s => Overlap(s.StartTime, EffectiveSessionEnd(s, range.To), range.From, range.To).TotalMinutes),
            enrolledStudents.Select(s => new { s.Id, Name = string.IsNullOrWhiteSpace(s.FullName) ? s.Id.ToString() : s.FullName })
                .ToDictionary(x => x.Name, x => sessions.Count(s => s.StudentId == x.Id)), alerts);
    }

    public async Task<LabUtilizationReport?> GetLabUtilizationAsync(int teacherId, DateTime from, DateTime to, string? station = null, int? classId = null, CancellationToken cancellationToken = default)
    {
        var range = NormalizeRange(from, to);
        station = string.IsNullOrWhiteSpace(station) ? null : station.Trim();
        var classes = await GetClassOptionsAsync(teacherId, cancellationToken);
        var selectedClass = classId.HasValue ? classes.FirstOrDefault(c => c.ClassId == classId.Value) : null;
        if (classId.HasValue && selectedClass is null) return null;

        var accessibleStudents = await GetAccessibleStudentRowsAsync(teacherId, cancellationToken);
        var targetStudentIds = accessibleStudents.Select(s => s.Id).ToHashSet();
        if (classId.HasValue)
        {
            var classStudentIds = await GetClassStudentIdsAsync(classId.Value, cancellationToken);
            targetStudentIds.IntersectWith(classStudentIds);
        }

        var computers = await _db.Computers.AsNoTracking()
            .OrderBy(c => c.LaboratoryStation)
            .ToListAsync(cancellationToken);
        var sessions = targetStudentIds.Count == 0
            ? new List<LabSession>()
            : await _db.LabSessions.AsNoTracking().Include(s => s.Computer)
                .Where(s => targetStudentIds.Contains(s.StudentId) && s.StartTime < range.To && (s.EndTime ?? range.To) > range.From)
                .ToListAsync(cancellationToken);
        var slices = sessions.Select(s => ToSessionSlice(s, range.From, range.To))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

        var availableStations = computers.Select(c => c.LaboratoryStation)
            .Concat(slices.SelectMany(s => new[] { s.Station, s.PcName }))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (station is not null)
        {
            slices = slices.Where(s => Same(s.Station, station) || Same(s.PcName, station)).ToList();
        }

        var selectedCanonicalStations = slices.Select(s => s.Station).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (station is not null)
        {
            foreach (var computer in computers.Where(c => Same(c.LaboratoryStation, station)))
                selectedCanonicalStations.Add(computer.LaboratoryStation);
        }
        else
        {
            selectedCanonicalStations.UnionWith(computers.Select(c => c.LaboratoryStation));
        }

        var selectedComputers = computers
            .Where(c => selectedCanonicalStations.Contains(c.LaboratoryStation))
            .ToList();
        var studentNumberById = accessibleStudents
            .Where(student => targetStudentIds.Contains(student.Id))
            .ToDictionary(student => student.Id, student => student.StudentNumber);
        var targetStudentNumbers = studentNumberById.Values.ToList();
        var idleIntervals = targetStudentNumbers.Count == 0
            ? new List<IdleInterval>()
            : await _db.IdleIntervals.AsNoTracking()
                .Where(i => targetStudentNumbers.Contains(i.StudentId) && i.StartedAt < range.To && (i.EndedAt ?? range.To) > range.From)
                .ToListAsync(cancellationToken);

        var calculations = new List<StationCalculation>();
        foreach (var stationName in selectedCanonicalStations.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            var stationSlices = slices.Where(s => Same(s.Station, stationName)).ToList();
            var occupiedRanges = Merge(stationSlices.Select(s => (s.Start, s.End)));
            var idleRanges = new List<(DateTime Start, DateTime End)>();
            foreach (var interval in idleIntervals)
            {
                var end = interval.EndedAt ?? range.To;
                foreach (var session in stationSlices.Where(s =>
                             studentNumberById.TryGetValue(s.StudentId, out var studentNumber) &&
                             studentNumber == interval.StudentId &&
                             (Same(s.PcName, interval.PcName) || Same(s.Station, interval.PcName))))
                {
                    var overlap = Intersect(interval.StartedAt, end, session.Start, session.End);
                    if (overlap.HasValue) idleRanges.Add(overlap.Value);
                }
            }

            var mergedIdle = Merge(idleRanges);
            var registeredCount = selectedComputers.Count(c => Same(c.LaboratoryStation, stationName));
            var capacityUnits = Math.Max(registeredCount, stationSlices.Count > 0 ? 1 : 0);
            var capacity = TimeSpan.FromTicks((range.To - range.From).Ticks * capacityUnits);
            var occupied = SumRanges(occupiedRanges, range.From, range.To);
            var idle = SumRanges(mergedIdle, range.From, range.To);
            if (idle > occupied) idle = occupied;
            var item = new StationUtilizationItem(stationName, capacityUnits, stationSlices.Count,
                stationSlices.Select(s => s.StudentId).Distinct().Count(), capacity, occupied, idle);
            calculations.Add(new StationCalculation(item, occupiedRanges, mergedIdle));
        }

        var stationItems = calculations.Select(c => c.Item).ToList();
        var daily = BuildDailyUtilization(range.From, range.To, calculations);
        var capacityTotal = Sum(stationItems.Select(s => s.Capacity));
        var occupiedTotal = Sum(stationItems.Select(s => s.Occupied));
        var idleTotal = Sum(stationItems.Select(s => s.Idle));
        return new LabUtilizationReport(range.From, range.To, classId, selectedClass?.Name, station,
            selectedComputers.Count, stationItems.Count(s => s.Occupied > TimeSpan.Zero), slices.Count,
            slices.Select(s => s.StudentId).Distinct().Count(), capacityTotal, occupiedTotal, idleTotal,
            stationItems, daily, classes, availableStations);
    }

    public async Task<UnifiedTimelineReport?> GetUnifiedTimelineAsync(int teacherId, UnifiedTimelineFilter filter, CancellationToken cancellationToken = default)
    {
        var range = NormalizeRange(filter.From, filter.To);
        var pageSize = Math.Clamp(filter.PageSize, 1, 500);
        var classes = await GetClassOptionsAsync(teacherId, cancellationToken);
        if (filter.ClassId.HasValue && classes.All(c => c.ClassId != filter.ClassId.Value)) return null;

        var accessible = await GetAccessibleStudentRowsAsync(teacherId, cancellationToken);
        if (filter.StudentId.HasValue && accessible.All(s => s.Id != filter.StudentId.Value)) return null;

        var target = accessible;
        if (filter.ClassId.HasValue)
        {
            var classIds = await GetClassStudentIdsAsync(filter.ClassId.Value, cancellationToken);
            target = target.Where(s => classIds.Contains(s.Id)).ToList();
        }
        if (filter.StudentId.HasValue) target = target.Where(s => s.Id == filter.StudentId.Value).ToList();

        var targetIds = target.Select(s => s.Id).ToList();
        var targetStudentNumbers = target.Select(s => s.StudentNumber).ToList();
        var studentById = accessible.ToDictionary(s => s.Id);
        var studentByNumber = accessible.ToDictionary(s => s.StudentNumber, StringComparer.Ordinal);
        var items = new List<UnifiedTimelineItem>();

        if (targetIds.Count > 0 && SourceMatches(filter.Source, "Session"))
        {
            var sessions = await _db.LabSessions.AsNoTracking().Include(s => s.Computer)
                .Where(s => targetIds.Contains(s.StudentId) &&
                            ((s.StartTime >= range.From && s.StartTime < range.To) ||
                             (s.EndTime.HasValue && s.EndTime.Value >= range.From && s.EndTime.Value < range.To)))
                .ToListAsync(cancellationToken);
            foreach (var session in sessions)
            {
                var student = studentById[session.StudentId];
                var stationName = SessionStation(session);
                if (session.StartTime >= range.From && session.StartTime < range.To && EventMatches(filter.EventType, "SessionStarted"))
                {
                    items.Add(TimelineItem(session.StartTime, "Session", "SessionStarted", student, stationName,
                        "Lab session started", $"Session #{session.Id} started.", null, session.Status, $"session:{session.Id}:start"));
                }
                if (session.EndTime is DateTime ended && ended >= range.From && ended < range.To && EventMatches(filter.EventType, "SessionEnded"))
                {
                    items.Add(TimelineItem(ended, "Session", "SessionEnded", student, stationName,
                        "Lab session ended", $"Session #{session.Id} ran for {Math.Max(0, (ended - session.StartTime).TotalMinutes):0.#} minutes.", null, session.Status, $"session:{session.Id}:end"));
                }
            }
        }

        if (targetIds.Count > 0 && SourceMatches(filter.Source, "Activity"))
        {
            var activities = await _db.ActivityEvents.AsNoTracking()
                .Where(e => targetStudentNumbers.Contains(e.StudentId) && e.Timestamp >= range.From && e.Timestamp < range.To)
                .ToListAsync(cancellationToken);
            foreach (var activity in activities.Where(a => EventMatches(filter.EventType, a.EventType)))
            {
                if (!studentByNumber.TryGetValue(activity.StudentId, out var student)) continue;
                var title = string.IsNullOrWhiteSpace(activity.ApplicationName) ? activity.EventType : activity.ApplicationName;
                items.Add(TimelineItem(activity.Timestamp, "Activity", activity.EventType, student, activity.PcName,
                    title!, activity.Details, null, null, $"activity:{activity.ActivityEventId}"));
            }
        }

        if (targetIds.Count > 0 && SourceMatches(filter.Source, "Alert") && EventMatches(filter.EventType, "AlertRaised"))
        {
            var alertRows = await _db.MonitoringAlerts.AsNoTracking()
                .Where(a => targetStudentNumbers.Contains(a.StudentId))
                .ToListAsync(cancellationToken);
            foreach (var alert in SummarizeAlerts(alertRows).Where(a => a.LastSeenAt >= range.From && a.LastSeenAt < range.To))
            {
                if (!studentByNumber.TryGetValue(alert.StudentId, out var student)) continue;
                items.Add(TimelineItem(alert.LastSeenAt, "Alert", "AlertRaised", student, alert.PcName,
                    alert.Title, $"{alert.Message} ({alert.OccurrenceCount} occurrence{(alert.OccurrenceCount == 1 ? string.Empty : "s")})",
                    alert.Severity, alert.Status.ToString(), $"alert:{alert.MonitoringAlertId}"));
            }
        }

        if (targetIds.Count > 0 && SourceMatches(filter.Source, "RemoteCommand"))
        {
            var commands = await _db.RemoteCommandLogs.AsNoTracking()
                .Where(l => l.TeacherId == teacherId && targetStudentNumbers.Contains(l.StudentId) &&
                             l.Timestamp >= range.From && l.Timestamp < range.To)
                .ToListAsync(cancellationToken);
            foreach (var command in commands.Where(c => EventMatches(filter.EventType, c.Command)))
            {
                if (!studentByNumber.TryGetValue(command.StudentId, out var student)) continue;
                items.Add(TimelineItem(command.Timestamp, "RemoteCommand", command.Command, student, command.PcName,
                    command.Command, command.Details, null, null, $"command:{command.RemoteCommandLogId}"));
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.Station))
            items = items.Where(i => Same(i.Station, filter.Station)).ToList();

        items = items.OrderByDescending(i => i.Timestamp)
            .ThenByDescending(i => i.ReferenceId, StringComparer.Ordinal)
            .ToList();
        var page = NormalizePage(filter.Page, pageSize, items.Count);
        var paged = new PagedResult<UnifiedTimelineItem>(items.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, items.Count);
        var options = accessible.OrderBy(s => s.Name).Select(s => new AnalyticsStudentOption(s.Id, s.StudentNumber, s.Name)).ToList();
        var stationOptions = (await _db.Computers.AsNoTracking().Select(c => c.LaboratoryStation).ToListAsync(cancellationToken))
            .Concat(items.Select(i => i.Station))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var normalizedFilter = filter with { From = range.From, To = range.To, Page = page, PageSize = pageSize };
        return new UnifiedTimelineReport(normalizedFilter, paged, options, classes, stationOptions);
    }

    public async Task<PagedResult<RemoteHistoryItem>> GetRemoteHistoryAsync(int teacherId, DateTime? from = null, DateTime? to = null, string? command = null, string? studentId = null, int page = 1, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        var query = _db.RemoteCommandLogs.AsNoTracking().Where(l => l.TeacherId == teacherId);
        if (from.HasValue) query = query.Where(l => l.Timestamp >= UtcDate(from.Value));
        if (to.HasValue) query = query.Where(l => l.Timestamp < UtcDate(to.Value).AddDays(1));
        if (!string.IsNullOrWhiteSpace(command)) query = query.Where(l => l.Command == command);
        if (!string.IsNullOrWhiteSpace(studentId)) query = query.Where(l => l.StudentId == studentId);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var total = await query.CountAsync(cancellationToken);
        page = NormalizePage(page, pageSize, total);
        var items = await query.OrderByDescending(l => l.Timestamp).ThenByDescending(l => l.RemoteCommandLogId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new RemoteHistoryItem(l.Timestamp, l.StudentId, l.PcName, l.Command, l.Details, l.RemoteControlSessionId))
            .ToListAsync(cancellationToken);
        return new PagedResult<RemoteHistoryItem>(items, page, pageSize, total);
    }

    private async Task<AlertBulkActionResult> ChangeAlertStatusAsync(IReadOnlyCollection<int> alertIds, int teacherId, MonitoringAlertStatus status, string? reason, CancellationToken cancellationToken)
    {
        var requestedIds = alertIds.Where(id => id > 0).Distinct().ToHashSet();
        if (requestedIds.Count == 0) return new AlertBulkActionResult(0, 0, 0);

        var accessibleIds = await AccessibleStudentIds(teacherId, cancellationToken);
        var alerts = await _db.MonitoringAlerts
            .Where(a => accessibleIds.Contains(a.StudentId))
            .ToListAsync(cancellationToken);
        var selectedKeys = alerts.Where(a => requestedIds.Contains(a.MonitoringAlertId))
            .Select(a => a.GroupKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var now = DateTime.UtcNow;
        var changedGroups = 0;
        foreach (var key in selectedKeys)
        {
            var group = alerts.Where(a => a.GroupKey == key).ToList();
            var changed = group.Any(a => !HasStatus(a, status, reason));
            if (!changed) continue;

            foreach (var alert in group)
            {
                switch (status)
                {
                    case MonitoringAlertStatus.Acknowledged:
                        alert.IsAcknowledged = true;
                        alert.AcknowledgedAt = now;
                        alert.AcknowledgedByTeacherId = teacherId;
                        alert.DismissedAt = null;
                        alert.DismissedByTeacherId = null;
                        alert.DismissalReason = null;
                        break;
                    case MonitoringAlertStatus.Dismissed:
                        alert.IsAcknowledged = false;
                        alert.AcknowledgedAt = null;
                        alert.AcknowledgedByTeacherId = null;
                        alert.DismissedAt = now;
                        alert.DismissedByTeacherId = teacherId;
                        alert.DismissalReason = reason;
                        break;
                    default:
                        alert.IsAcknowledged = false;
                        alert.AcknowledgedAt = null;
                        alert.AcknowledgedByTeacherId = null;
                        alert.DismissedAt = null;
                        alert.DismissedByTeacherId = null;
                        alert.DismissalReason = null;
                        break;
                }
            }

            var representative = group.OrderByDescending(a => a.LastSeenAt).ThenByDescending(a => a.MonitoringAlertId).First();
            var action = status switch
            {
                MonitoringAlertStatus.Acknowledged => "AcknowledgeAlert",
                MonitoringAlertStatus.Dismissed => "DismissAlert",
                _ => "ReopenAlert"
            };
            var details = $"Alert {representative.MonitoringAlertId} group for student {representative.StudentId} changed to {status}.";
            if (status == MonitoringAlertStatus.Dismissed && reason is not null) details += $" Reason: {reason}";
            _db.AuditLogs.Add(new AuditLog
            {
                UserType = "Teacher",
                UserId = teacherId,
                Action = action,
                Details = details,
                Timestamp = now
            });
            changedGroups++;
        }

        if (changedGroups > 0) await _db.SaveChangesAsync(cancellationToken);
        return new AlertBulkActionResult(requestedIds.Count, selectedKeys.Count, changedGroups);
    }

    private async Task<List<MonitoringAlert>> QueryAlertGroupsAsync(int teacherId, MonitoringAlertFilter filter, CancellationToken cancellationToken)
    {
        var studentIds = await AccessibleStudentIds(teacherId, cancellationToken);
        var query = _db.MonitoringAlerts.AsNoTracking().Where(a => studentIds.Contains(a.StudentId));
        if (!string.IsNullOrWhiteSpace(filter.StudentId)) query = query.Where(a => a.StudentId == filter.StudentId.Trim());
        var rows = await query.ToListAsync(cancellationToken);
        IEnumerable<MonitoringAlert> groups = SummarizeAlerts(rows);
        if (filter.From.HasValue)
        {
            var from = UtcDate(filter.From.Value);
            groups = groups.Where(a => a.LastSeenAt >= from);
        }
        if (filter.To.HasValue)
        {
            var to = UtcDate(filter.To.Value).AddDays(1);
            groups = groups.Where(a => a.FirstSeenAt < to);
        }
        if (!string.IsNullOrWhiteSpace(filter.Severity)) groups = groups.Where(a => Same(a.Severity, filter.Severity));
        if (!string.IsNullOrWhiteSpace(filter.Station)) groups = groups.Where(a => Same(a.PcName, filter.Station));
        if (filter.Status.HasValue) groups = groups.Where(a => a.Status == filter.Status.Value);
        return groups.OrderByDescending(a => a.LastSeenAt).ThenByDescending(a => a.MonitoringAlertId).ToList();
    }

    private async Task<DurationSummary> CalculateDurationsAsync(int studentId, string studentNumber, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var sessions = await _db.LabSessions.AsNoTracking()
            .Where(s => s.StudentId == studentId && s.StartTime < to && (s.EndTime ?? to) > from)
            .ToListAsync(cancellationToken);
        var idle = await _db.IdleIntervals.AsNoTracking()
            .Where(i => i.StudentId == studentNumber && i.StartedAt < to && (i.EndedAt ?? to) > from)
            .ToListAsync(cancellationToken);
        var sessionRanges = Merge(sessions.Select(s => (s.StartTime, EffectiveSessionEnd(s, to))));
        var idleRanges = Merge(idle.SelectMany(i => sessionRanges
            .Select(r => Intersect(i.StartedAt, i.EndedAt ?? to, r.Start, r.End))
            .Where(r => r.HasValue)
            .Select(r => r!.Value)));
        var events = await _db.ActivityEvents.AsNoTracking()
            .Where(e => e.StudentId == studentNumber && e.Timestamp <= to)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(cancellationToken);
        var sessionDuration = SumRanges(sessionRanges, from, to).TotalSeconds;
        var idleDuration = SumRanges(idleRanges, from, to).TotalSeconds;
        var application = DurationFor(events, "ApplicationUsed", from, to, sessionRanges);
        var website = DurationFor(events, "WebsiteUsed", from, to, sessionRanges);
        return new DurationSummary(TimeSpan.FromSeconds(application), TimeSpan.FromSeconds(website),
            TimeSpan.FromSeconds(idleDuration), TimeSpan.FromSeconds(Math.Max(0, sessionDuration - idleDuration)));
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

    private static List<MonitoringAlert> SummarizeAlerts(IEnumerable<MonitoringAlert> alerts)
    {
        return alerts.GroupBy(a => a.GroupKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var latest = group.OrderByDescending(a => a.LastSeenAt).ThenByDescending(a => a.MonitoringAlertId).First();
                var firstSeen = group.Min(a => a.FirstSeenAt);
                var lastSeen = group.Max(a => a.LastSeenAt);
                var count = (int)Math.Min(int.MaxValue, group.Sum(a => (long)Math.Max(1, a.OccurrenceCount)));
                return new MonitoringAlert
                {
                    MonitoringAlertId = latest.MonitoringAlertId,
                    StudentId = latest.StudentId,
                    PcName = latest.PcName,
                    Severity = latest.Severity,
                    Title = latest.Title,
                    Message = latest.Message,
                    IsAcknowledged = latest.IsAcknowledged,
                    AcknowledgedAt = latest.AcknowledgedAt,
                    AcknowledgedByTeacherId = latest.AcknowledgedByTeacherId,
                    DismissedAt = latest.DismissedAt,
                    DismissedByTeacherId = latest.DismissedByTeacherId,
                    DismissalReason = latest.DismissalReason,
                    DedupeKey = latest.DedupeKey,
                    GroupKey = group.Key,
                    OccurrenceCount = count,
                    FirstSeenAt = firstSeen,
                    LastSeenAt = lastSeen,
                    CreatedAt = firstSeen
                };
            })
            .ToList();
    }

    private async Task<Student?> GetAccessibleStudentAsync(int studentId, int teacherId, CancellationToken cancellationToken) =>
        await AccessibleStudents(teacherId).AsNoTracking().Include(s => s.Class)
            .FirstOrDefaultAsync(s => s.Id == studentId, cancellationToken);

    private IQueryable<Student> AccessibleStudents(int teacherId) =>
        _db.Students.Where(s => s.AdviserId == teacherId ||
            _db.Classes.Any(c => c.TeacherId == teacherId && !c.IsArchived &&
                (c.Status == "Active" || string.IsNullOrEmpty(c.Status)) &&
                (c.ClassId == s.ClassId || _db.ClassStudents.Any(cs => cs.ClassId == c.ClassId && cs.StudentId == s.Id))));

    private async Task<List<string>> AccessibleStudentIds(int teacherId, CancellationToken cancellationToken) =>
        await AccessibleStudents(teacherId).AsNoTracking()
            .Select(s => s.StudentNumber)
            .ToListAsync(cancellationToken);

    private async Task<List<AccessibleStudentRow>> GetAccessibleStudentRowsAsync(int teacherId, CancellationToken cancellationToken)
    {
        var rows = await AccessibleStudents(teacherId).AsNoTracking()
            .Select(s => new { s.Id, s.StudentNumber, s.FullName })
            .ToListAsync(cancellationToken);
        return rows.Select(s => new AccessibleStudentRow(s.Id, s.StudentNumber,
            string.IsNullOrWhiteSpace(s.FullName) ? s.StudentNumber : s.FullName)).ToList();
    }

    private async Task<List<AnalyticsClassOption>> GetClassOptionsAsync(int teacherId, CancellationToken cancellationToken) =>
        await _db.Classes.AsNoTracking()
            .Where(c => c.TeacherId == teacherId && !c.IsArchived &&
                        (c.Status == "Active" || string.IsNullOrEmpty(c.Status)))
            .OrderBy(c => c.ClassName)
            .Select(c => new AnalyticsClassOption(c.ClassId, c.ClassName))
            .ToListAsync(cancellationToken);

    private async Task<HashSet<int>> GetClassStudentIdsAsync(int classId, CancellationToken cancellationToken) =>
        (await _db.Students.AsNoTracking()
            .Where(s => s.ClassId == classId || _db.ClassStudents.Any(cs => cs.ClassId == classId && cs.StudentId == s.Id))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken))
        .ToHashSet();

    private static List<DailyUtilizationItem> BuildDailyUtilization(DateTime from, DateTime to, IReadOnlyList<StationCalculation> stations)
    {
        var result = new List<DailyUtilizationItem>();
        for (var day = from.Date; day < to; day = day.AddDays(1))
        {
            var start = day > from ? day : from;
            var endOfDay = day.AddDays(1);
            var end = endOfDay < to ? endOfDay : to;
            if (end <= start) continue;
            var capacity = TimeSpan.FromTicks((end - start).Ticks * stations.Sum(s => s.Item.ComputerCount));
            var occupied = Sum(stations.Select(s => SumRanges(s.OccupiedRanges, start, end)));
            var idle = Sum(stations.Select(s => SumRanges(s.IdleRanges, start, end)));
            if (idle > occupied) idle = occupied;
            result.Add(new DailyUtilizationItem(day, capacity, occupied, idle));
        }
        return result;
    }

    private static SessionSlice? ToSessionSlice(LabSession session, DateTime from, DateTime to)
    {
        var start = session.StartTime < from ? from : session.StartTime;
        var effectiveEnd = EffectiveSessionEnd(session, to);
        var end = effectiveEnd > to ? to : effectiveEnd;
        return end <= start ? null : new SessionSlice(session.StudentId, SessionStation(session), session.PCName, start, end);
    }

    private static DateTime EffectiveSessionEnd(LabSession session, DateTime upperBound)
    {
        if (session.EndTime.HasValue) return session.EndTime.Value;
        if (session.Status == "Paused" && session.PauseTime.HasValue) return session.PauseTime.Value;
        var now = DateTime.UtcNow;
        return now < upperBound ? now : upperBound;
    }

    private static string SessionStation(LabSession session) =>
        string.IsNullOrWhiteSpace(session.Computer?.LaboratoryStation) ? session.PCName : session.Computer.LaboratoryStation;

    private static UnifiedTimelineItem TimelineItem(DateTime timestamp, string source, string eventType,
        AccessibleStudentRow student, string station, string title, string? details, string? severity,
        string? status, string referenceId) =>
        new(timestamp, source, eventType, student.Id, student.StudentNumber, student.Name, station, title,
            details, severity, status, referenceId);

    private static bool SourceMatches(string? requested, string source) =>
        string.IsNullOrWhiteSpace(requested) || Same(requested, source);

    private static bool EventMatches(string? requested, string eventType) =>
        string.IsNullOrWhiteSpace(requested) || Same(requested, eventType);

    private static bool HasStatus(MonitoringAlert alert, MonitoringAlertStatus status, string? reason) => status switch
    {
        MonitoringAlertStatus.Acknowledged => alert.Status == status,
        MonitoringAlertStatus.Dismissed => alert.Status == status && alert.DismissalReason == reason,
        _ => alert.Status == MonitoringAlertStatus.Open && alert.AcknowledgedAt is null &&
             alert.AcknowledgedByTeacherId is null && alert.DismissedAt is null &&
             alert.DismissedByTeacherId is null && alert.DismissalReason is null
    };

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

    private static (DateTime Start, DateTime End)? Intersect(DateTime start, DateTime end, DateTime otherStart, DateTime otherEnd)
    {
        var resultStart = start > otherStart ? start : otherStart;
        var resultEnd = end < otherEnd ? end : otherEnd;
        return resultEnd > resultStart ? (resultStart, resultEnd) : null;
    }

    private static TimeSpan SumRanges(IEnumerable<(DateTime Start, DateTime End)> ranges, DateTime from, DateTime to) =>
        Sum(ranges.Select(r => Overlap(r.Start, r.End, from, to)));

    private static TimeSpan Sum(IEnumerable<TimeSpan> values) =>
        TimeSpan.FromTicks(values.Sum(v => v.Ticks));

    private static (DateTime From, DateTime To) NormalizeRange(DateTime from, DateTime to)
    {
        if (from == default) from = DateTime.UtcNow.Date;
        if (to == default) to = from.Date.AddDays(1);
        from = ToUtc(from);
        to = ToUtc(to);
        if (to < from) (from, to) = (to, from);
        if (to == from) to = from.AddDays(1);
        return (from, to);
    }

    private static DateTime UtcDate(DateTime value) => DateTime.SpecifyKind(ToUtc(value).Date, DateTimeKind.Utc);

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value
    };

    private static TimeSpan Overlap(DateTime start, DateTime end, DateTime from, DateTime to)
    {
        var clippedStart = start < from ? from : start;
        var clippedEnd = end > to ? to : end;
        return clippedEnd > clippedStart ? clippedEnd - clippedStart : TimeSpan.Zero;
    }

    private static int NormalizePage(int page, int pageSize, int total) =>
        Math.Min(Math.Max(1, page), Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record AccessibleStudentRow(int Id, string StudentNumber, string Name);
    private sealed record SessionSlice(int StudentId, string Station, string PcName, DateTime Start, DateTime End);
    private sealed record StationCalculation(StationUtilizationItem Item,
        IReadOnlyList<(DateTime Start, DateTime End)> OccupiedRanges,
        IReadOnlyList<(DateTime Start, DateTime End)> IdleRanges);
}
