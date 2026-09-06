namespace Shared.Contracts;

/// <summary>
/// The role names carried in the authentication cookie and read by
/// <c>[Authorize(Roles = ...)]</c>. These are const so they can be used in
/// attributes, and they cross the wire, so the value is the contract: changing
/// one invalidates every cookie already issued.
/// </summary>
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Teacher = "Teacher";
    public const string Student = "Student";

    /// <summary>
    /// For the actions an administrator and a teacher both reach.
    /// <c>[Authorize(Roles = ...)]</c> takes a comma-separated list, so this
    /// cannot be composed from the two above at compile time.
    /// </summary>
    public const string AdminOrTeacher = Admin + "," + Teacher;
}

/// <summary>
/// The lifecycle of a laboratory session, as stored in <c>LabSession.Status</c>
/// and sent to the student client. Both sides read these, which is why they are
/// here rather than in the server: a session the server calls "Paused" and the
/// agent calls something else is a student left staring at a stopped screen.
/// </summary>
public static class LabSessionStatus
{
    public const string Running = "Running";
    public const string Paused = "Paused";
    public const string Ended = "Ended";

    /// <summary>The state before any session has started. Not stored.</summary>
    public const string None = "None";
}
