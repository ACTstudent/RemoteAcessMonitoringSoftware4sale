namespace Shared.Contracts;

public sealed record StudentClientLoginRequest(
    string Username,
    string Password,
    string PcName);

public sealed record StudentClientLoginResponse(
    string StudentId,
    string DisplayName);

/// <summary>Sent by a signed-in student agent to replace its own password.</summary>
public sealed record StudentClientPasswordChangeRequest(
    string CurrentPassword,
    string NewPassword);

/// <summary>
/// The password rules the student agent checks before it asks, and the server
/// checks again when it is asked - one copy, so the two cannot drift apart.
/// </summary>
public static class StudentPasswordRules
{
    public const int MinimumLength = 8;

    /// <summary>The same ceiling the sign-in endpoint puts on a password.</summary>
    public const int MaximumLength = 256;
}
