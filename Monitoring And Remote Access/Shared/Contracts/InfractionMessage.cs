namespace Shared.Contracts;

public sealed record InfractionMessage(
    string ConnectionId,
    string StudentId,
    string PcName,
    string Target,        // blocked process name / website matched
    string TargetType,    // Application | Website
    DateTime Timestamp);
