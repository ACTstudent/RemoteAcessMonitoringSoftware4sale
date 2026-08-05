namespace Shared.Contracts;

public sealed record RemoteInputMessage(
    string EventType,
    int X,
    int Y,
    int KeyCode,
    bool IsShift);
