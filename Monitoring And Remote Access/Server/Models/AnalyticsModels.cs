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
