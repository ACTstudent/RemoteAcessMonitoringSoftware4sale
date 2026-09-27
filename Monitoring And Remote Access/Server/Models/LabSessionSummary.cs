namespace Server.Models;

/// <summary>
/// The lab-wide session as the Dashboard and Sessions pages show it. A teacher
/// opens the lab before anyone signs in; the students' own sessions inside it
/// are counted separately, as Running and Paused.
/// </summary>
public sealed record LabSessionSummary(
    string Status,               // None | Running | Paused | Ended
    int ElapsedSeconds,
    DateTime? StartedAtUtc,
    SessionRule? Rule,
    string? StartedBy,
    int RunningStudents,
    int PausedStudents)
{
    public bool IsOpen => Status is "Running" or "Paused";
    public bool IsPaused => Status == "Paused";
    public int SignedInStudents => RunningStudents + PausedStudents;
}
