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
}
