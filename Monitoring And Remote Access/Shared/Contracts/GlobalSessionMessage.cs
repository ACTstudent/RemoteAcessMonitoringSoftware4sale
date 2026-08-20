namespace Shared.Contracts;

public sealed record GlobalSessionMessage(
    string Status,          // None | Running | Paused | Ended
    int ElapsedSeconds,
    DateTime? StartedAt);
