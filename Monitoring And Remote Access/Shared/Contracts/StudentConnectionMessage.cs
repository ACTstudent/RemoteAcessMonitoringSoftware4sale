namespace Shared.Contracts;

public sealed record StudentConnectionMessage(
    string ConnectionId,
    string StudentId,
    string PcName,
    DateTime ConnectedAt);
