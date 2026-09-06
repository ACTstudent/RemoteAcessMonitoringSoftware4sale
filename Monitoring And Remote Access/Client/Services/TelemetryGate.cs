namespace Client.Services;

/// <summary>
/// Decides what the agent is allowed to send, and when.
///
/// This was five fields and their comparisons scattered through MainForm's
/// status loop: whether idle changed, whether five seconds had passed, whether
/// the website differed from the last one, whether a browser's state differed
/// from the last one, and whether an infraction for the same app was still
/// inside its cooldown. All of it is a function of a value and a clock, and
/// none of it needs a window - so it can be tested, which matters more than
/// usual here because the agent cannot be run without a second machine.
///
/// Not thread-safe by design: the status loop is a single task, and adding a
/// lock would suggest otherwise.
/// </summary>
public sealed class TelemetryGate
{
    /// <summary>How often the foreground application may be reported.</summary>
    public static readonly TimeSpan ActiveAppInterval = TimeSpan.FromSeconds(5);

    /// <summary>How long the same infraction stays quiet after being reported.</summary>
    public static readonly TimeSpan InfractionCooldown = TimeSpan.FromSeconds(30);

    // Starts false, not null, on purpose. MainForm's field did, which means a
    // student who is active when the session begins produces no idle report at
    // all - the server is left to assume "not idle", which is what it already
    // assumed. Reporting the starting state would arguably be better, but this
    // is an extraction and changing what goes over the wire is not part of it.
    private bool _lastIdle;
    private DateTime _lastActiveApp = DateTime.MinValue;
    private string _lastWebsite = string.Empty;
    private readonly Dictionary<string, string> _lastBrowserStatus = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastInfraction = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Idle is reported on change only.</summary>
    public bool ShouldReportIdle(bool isIdle)
    {
        if (_lastIdle == isIdle) return false;
        _lastIdle = isIdle;
        return true;
    }

    /// <summary>
    /// The foreground application is reported on an interval rather than on
    /// change: it is a sample of what the student is doing, not an event.
    /// </summary>
    public bool ShouldReportActiveApp(DateTime utcNow)
    {
        if (utcNow - _lastActiveApp <= ActiveAppInterval) return false;
        _lastActiveApp = utcNow;
        return true;
    }

    /// <summary>
    /// A website is reported when the browser or the domain changes. Passing a
    /// null domain clears the memory, so returning to the same site after
    /// leaving it reports again.
    /// </summary>
    public bool ShouldReportWebsite(string? browser, string? domain)
    {
        if (string.IsNullOrEmpty(domain))
        {
            _lastWebsite = string.Empty;
            return false;
        }

        var signature = $"{browser}:{domain}";
        if (_lastWebsite == signature) return false;
        _lastWebsite = signature;
        return true;
    }

    /// <summary>
    /// A browser's collection state is reported when it changes. The signature
    /// combines the mode and its detail, because the same mode for a different
    /// reason is worth telling the teacher about.
    /// </summary>
    public bool ShouldReportBrowserStatus(string identity, string signature)
    {
        if (_lastBrowserStatus.TryGetValue(identity, out var previous) && previous == signature) return false;
        _lastBrowserStatus[identity] = signature;
        return true;
    }

    /// <summary>
    /// One infraction per target per cooldown. Without this a student sitting
    /// on a blocked page produces an alert on every pass of the loop, and the
    /// teacher's alert list becomes unreadable at exactly the moment it matters.
    /// </summary>
    public bool ShouldReportInfraction(string targetType, string target, DateTime utcNow)
    {
        var key = targetType + ":" + target;
        if (_lastInfraction.TryGetValue(key, out var last) && utcNow - last < InfractionCooldown) return false;
        _lastInfraction[key] = utcNow;
        return true;
    }

    /// <summary>
    /// Forgets everything. Used when a session ends, so the next student does
    /// not inherit the previous one's suppressed reports.
    /// </summary>
    public void Reset()
    {
        _lastIdle = false;
        _lastActiveApp = DateTime.MinValue;
        _lastWebsite = string.Empty;
        _lastBrowserStatus.Clear();
        _lastInfraction.Clear();
    }
}
