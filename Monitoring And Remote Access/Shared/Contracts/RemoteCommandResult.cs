namespace Shared.Contracts;

public sealed record RemoteCommandResult(bool Succeeded, string Message, int? RemoteControlSessionId);
