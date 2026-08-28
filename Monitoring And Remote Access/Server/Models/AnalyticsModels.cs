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

public sealed record StudentAnalyticsReport(
    Student Student,
    DateTime From,
    DateTime To,
    DurationSummary Durations,
    IReadOnlyList<ActivityTimelineItem> Timeline,
    IReadOnlyList<MonitoringAlert> Alerts);
