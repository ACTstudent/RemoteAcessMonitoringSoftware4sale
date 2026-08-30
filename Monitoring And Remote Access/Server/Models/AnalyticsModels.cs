namespace Server.Models;

public sealed record DurationSummary(
    TimeSpan Application,
    TimeSpan Website,
    TimeSpan Idle,
    TimeSpan Active)
{
    public double ApplicationMinutes => Application.TotalMinutes;
    public double WebsiteMinutes => Website.TotalMinutes;
    public double IdleMinutes => Idle.TotalMinutes;
    public double ActiveMinutes => Active.TotalMinutes;
}

public sealed record ActivityTimelineItem(
    DateTime Timestamp,
    string EventType,
    string? ApplicationName,
    string? Details,
    string PcName);

public sealed record RemoteHistoryItem(DateTime Timestamp, string StudentId, string PcName, string Command, string? Details, int? SessionId);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed record AlertHistoryItem(
    int AlertId,
    string Action,
    int? UserId,
    DateTime Timestamp,
    string Details);

public sealed record MonitoringAlertFilter(
    DateTime? From = null,
    DateTime? To = null,
    string? Severity = null,
    string? StudentId = null,
    string? Station = null,
    MonitoringAlertStatus? Status = MonitoringAlertStatus.Open,
    int Page = 1,
    int PageSize = 100);

public sealed record AlertBulkActionResult(
    int RequestedCount,
    int MatchedGroupCount,
    int ChangedGroupCount);

public sealed record AnalyticsClassOption(int ClassId, string Name);

public sealed record AnalyticsStudentOption(int StudentId, string StudentNumber, string Name);

public sealed record StationUtilizationItem(
    string Station,
    int ComputerCount,
    int SessionCount,
    int UniqueStudents,
    TimeSpan Capacity,
    TimeSpan Occupied,
    TimeSpan Idle)
{
    public TimeSpan Active => Occupied > Idle ? Occupied - Idle : TimeSpan.Zero;
    public double UtilizationPercent => Percentage(Occupied, Capacity);
    public double IdlePercent => Percentage(Idle, Occupied);

    private static double Percentage(TimeSpan value, TimeSpan total) =>
        total <= TimeSpan.Zero ? 0 : Math.Clamp(value.TotalSeconds / total.TotalSeconds * 100, 0, 100);
}

public sealed record DailyUtilizationItem(
    DateTime Date,
    TimeSpan Capacity,
    TimeSpan Occupied,
    TimeSpan Idle)
{
    public TimeSpan Active => Occupied > Idle ? Occupied - Idle : TimeSpan.Zero;
    public double UtilizationPercent => Capacity <= TimeSpan.Zero
        ? 0
        : Math.Clamp(Occupied.TotalSeconds / Capacity.TotalSeconds * 100, 0, 100);
}

public sealed record LabUtilizationReport(
    DateTime From,
    DateTime To,
    int? ClassId,
    string? ClassName,
    string? Station,
    int RegisteredComputers,
    int UsedStations,
    int TotalSessions,
    int UniqueStudents,
    TimeSpan Capacity,
    TimeSpan Occupied,
    TimeSpan Idle,
    IReadOnlyList<StationUtilizationItem> Stations,
    IReadOnlyList<DailyUtilizationItem> Daily,
    IReadOnlyList<AnalyticsClassOption> AvailableClasses,
    IReadOnlyList<string> AvailableStations)
{
    public TimeSpan Active => Occupied > Idle ? Occupied - Idle : TimeSpan.Zero;
    public double UtilizationPercent => Capacity <= TimeSpan.Zero
        ? 0
        : Math.Clamp(Occupied.TotalSeconds / Capacity.TotalSeconds * 100, 0, 100);
    public double IdlePercent => Occupied <= TimeSpan.Zero
        ? 0
        : Math.Clamp(Idle.TotalSeconds / Occupied.TotalSeconds * 100, 0, 100);
}

public sealed record UnifiedTimelineFilter(
    DateTime From,
    DateTime To,
    int? StudentId = null,
    int? ClassId = null,
    string? Station = null,
    string? Source = null,
    string? EventType = null,
    int Page = 1,
    int PageSize = 100);

public sealed record UnifiedTimelineItem(
    DateTime Timestamp,
    string Source,
    string EventType,
    int StudentId,
    string StudentNumber,
    string StudentName,
    string Station,
    string Title,
    string? Details,
    string? Severity,
    string? Status,
    string ReferenceId);

public sealed record UnifiedTimelineReport(
    UnifiedTimelineFilter Filter,
    PagedResult<UnifiedTimelineItem> Timeline,
    IReadOnlyList<AnalyticsStudentOption> AvailableStudents,
    IReadOnlyList<AnalyticsClassOption> AvailableClasses,
    IReadOnlyList<string> AvailableStations);

public sealed record ReportSummary(
    int TotalSessions,
    TimeSpan TotalDuration,
    IReadOnlyDictionary<string, int> SessionsByClass,
    IReadOnlyDictionary<string, int> SessionsByTeacher,
    IReadOnlyDictionary<string, int> SessionsByStation);

public sealed record StudentAnalyticsReport(
    Student Student,
    DateTime From,
    DateTime To,
    DurationSummary Durations,
    IReadOnlyList<ActivityTimelineItem> Timeline,
    IReadOnlyList<MonitoringAlert> Alerts);

public sealed record ClassAnalyticsReport(
    Class Class,
    DateTime From,
    DateTime To,
    int TotalSessions,
    double TotalMinutes,
    IReadOnlyDictionary<string, int> SessionsByStudent,
    IReadOnlyList<MonitoringAlert> Alerts);
