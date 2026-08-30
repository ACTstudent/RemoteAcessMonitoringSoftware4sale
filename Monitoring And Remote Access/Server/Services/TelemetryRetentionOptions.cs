namespace Server.Services;

public sealed class TelemetryRetentionOptions
{
    public const string SectionName = "TelemetryRetention";

    public bool Enabled { get; set; } = true;
    public int CleanupIntervalMinutes { get; set; } = 60;
    public int BatchSize { get; set; } = 250;
    public int ActivityEventDays { get; set; } = 30;
    public int ConnectionLogDays { get; set; } = 30;
    public int UsageLogDays { get; set; } = 30;
    public int WebsiteUsageLogDays { get; set; } = 30;
    public int BrowserMonitoringDays { get; set; } = 30;
    public int IdleIntervalDays { get; set; } = 30;
    public int MonitoringAlertDays { get; set; } = 90;
    public int RemoteLogDays { get; set; } = 90;
}
