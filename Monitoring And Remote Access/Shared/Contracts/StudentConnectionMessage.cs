namespace Shared.Contracts;

public sealed record StudentConnectionMessage(
    string ConnectionId,
    string StudentId,
    string PcName,
    DateTime ConnectedAt,
    // The student's full name, for the monitoring grid. StudentId stays the
    // student number, which everything else keys on.
    string? DisplayName = null);
