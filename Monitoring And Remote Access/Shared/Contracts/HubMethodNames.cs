namespace Shared.Contracts;

public static class HubMethodNames
{
    public const string SendScreenFrame = "SendScreenFrame";
    public const string SendRemoteInput = "SendRemoteInput";
    public const string LockStudent = "LockStudent";
    public const string UnlockStudent = "UnlockStudent";
    public const string ForceLogout = "ForceLogout";
    public const string ShutdownStudent = "ShutdownStudent";
    public const string RestartStudent = "RestartStudent";
    public const string SendWarningPopup = "SendWarningPopup";
    public const string BroadcastScreen = "BroadcastScreen";
    public const string StopBroadcast = "StopBroadcast";
    public const string SendNotification = "SendNotification";
    public const string GlobalStartSession = "GlobalStartSession";
    public const string GlobalPauseSession = "GlobalPauseSession";
    public const string GlobalEndSession = "GlobalEndSession";
    public const string FetchRestrictions = "FetchRestrictions";
    public const string ReportInfraction = "ReportInfraction";
    public const string ReportIdleStatus = "ReportIdleStatus";
    public const string ReportActiveApp = "ReportActiveApp";
    public const string ReportWebsiteActivity = "ReportWebsiteActivity";
    public const string ReportBrowserMonitoringStatus = "ReportBrowserMonitoringStatus";
    public const string ReportTelemetryBatch = "ReportTelemetryBatch";
}
