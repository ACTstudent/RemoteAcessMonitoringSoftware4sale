namespace Shared.Contracts;

public sealed record RemoteControlStateMessage(
    string StudentId,
    bool IsActive,
    DateTime Timestamp);
