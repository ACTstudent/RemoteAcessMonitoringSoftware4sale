using Shared.Contracts;

namespace Client.Services;

/// <summary>
/// The student's view of the laboratory session: what state it is in and how
/// long it has been running.
///
/// Two fields in MainForm, read and written from four places - the one-second
/// timer, the timer's own rendering, the hub's session-state message, and the
/// end-of-session handler. Pulling them out means the rule that the clock only
/// advances while the session is running can be stated once and tested, rather
/// than living inside a Tick handler on a form that needs a second machine to
/// run.
/// </summary>
public sealed class SessionClock
{
    public string Status { get; private set; } = LabSessionStatus.None;

    public int ElapsedSeconds { get; private set; }

    /// <summary>Whether a second has any business being counted.</summary>
    public bool IsRunning => Status == LabSessionStatus.Running;

    /// <summary>
    /// Advances one second, but only while running. Returns whether anything
    /// changed, so a paused session does not repaint the timer sixty times a
    /// minute for no reason.
    /// </summary>
    public bool Tick()
    {
        if (!IsRunning) return false;
        ElapsedSeconds++;
        return true;
    }

    /// <summary>
    /// Takes the server's word for both the state and the elapsed time. The
    /// server is authoritative: a client that has been asleep, throttled or
    /// disconnected will have drifted, and the student is shown the session's
    /// real age rather than its own count.
    /// </summary>
    public void Apply(string status, int elapsedSeconds)
    {
        Status = status;
        ElapsedSeconds = Math.Max(0, elapsedSeconds);
    }

    /// <summary>The session is over; the clock stops and resets.</summary>
    public void End()
    {
        Status = LabSessionStatus.Ended;
        ElapsedSeconds = 0;
    }

    /// <summary>mm:ss, as shown on the toolbar.</summary>
    public string Display()
    {
        var seconds = Math.Max(0, ElapsedSeconds);
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }
}
