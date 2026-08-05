namespace Shared.Contracts;

public static class HubEventNames
{
    public const string TeachersGroup = "Teachers";
    public const string StudentsGroup = "Students";

    public const string ReceiveScreenFrame = "ReceiveScreenFrame";
    public const string StudentConnected = "StudentConnected";
    public const string StudentDisconnected = "StudentDisconnected";
    public const string ExecuteRemoteInput = "ExecuteRemoteInput";
}
