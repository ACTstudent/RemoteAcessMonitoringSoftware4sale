namespace Shared.Contracts;

public static class HubEventNames
{
    public const string TeachersGroup = "Teachers";
    public const string StudentsGroup = "Students";

    public const string ReceiveScreenFrame = "ReceiveScreenFrame";
    public const string StudentConnected = "StudentConnected";
    public const string StudentDisconnected = "StudentDisconnected";
    public const string ExecuteRemoteInput = "ExecuteRemoteInput";

    // Teacher control commands
    public const string LockStudent = "LockStudent";
    public const string UnlockStudent = "UnlockStudent";
    public const string ForceLogout = "ForceLogout";
    public const string BroadcastScreen = "BroadcastScreen";
    public const string SendNotification = "SendNotification";

    // Student status reporting
    public const string ReportIdleStatus = "ReportIdleStatus";
    public const string IdleStatusReceived = "IdleStatusReceived";
    public const string ReportActiveApp = "ReportActiveApp";
    public const string ActiveAppReceived = "ActiveAppReceived";

    // Global session management
    public const string GlobalStartSession = "GlobalStartSession";
    public const string GlobalPauseSession = "GlobalPauseSession";
    public const string GlobalEndSession = "GlobalEndSession";
    public const string GlobalSessionState = "GlobalSessionState";
    public const string SessionEnded = "SessionEnded";

    // Restriction enforcement
    public const string FetchRestrictions = "FetchRestrictions";
    public const string RestrictionsReceived = "RestrictionsReceived";
    public const string ReportInfraction = "ReportInfraction";
    public const string InfractionDetected = "InfractionDetected";

    // Remote workstation commands
    public const string ShutdownStudent = "ShutdownStudent";
    public const string SendWarningPopup = "SendWarningPopup";
}
