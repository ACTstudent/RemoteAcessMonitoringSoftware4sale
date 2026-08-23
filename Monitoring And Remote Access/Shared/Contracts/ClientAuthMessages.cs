namespace Shared.Contracts;

public sealed record StudentClientLoginRequest(
    string Username,
    string Password,
    string PcName);

public sealed record StudentClientLoginResponse(
    string StudentId,
    string DisplayName);
