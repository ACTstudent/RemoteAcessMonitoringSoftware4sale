namespace Shared.Contracts;

public sealed record NotificationMessage(
    string Type,
    string Title,
    string Message,
    DateTime Timestamp);

public sealed record IdleStatusMessage(
    string ConnectionId,
    string StudentId,
    string PcName,
    bool IsIdle,
    DateTime Timestamp);

public sealed record ActiveAppMessage(
    string ConnectionId,
    string StudentId,
    string PcName,
    string ApplicationName,
    DateTime Timestamp);

public sealed record BroadcastMessage(
    string FrameBase64,
    DateTime Timestamp);