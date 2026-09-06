using Shared.Contracts;

namespace Server.Extensions;

/// <summary>
/// Turns a browser monitoring mode into something a teacher can act on.
///
/// The enum names are accurate and mean nothing to the person reading them:
/// a teacher shown "WindowTitleFallback" cannot tell whether monitoring is
/// working, degraded, or off. Worse, the three states differ in a way that
/// matters — one records exact addresses, one records only window titles, and
/// one records nothing — and presenting the middle one as if it were the last
/// would tell a teacher a student is idle when they are not.
///
/// One definition, used by the pages the server renders and handed to the live
/// monitoring script as data, so the two cannot drift apart.
/// </summary>
public static class BrowserMonitoringDisplay
{
    public static string Label(BrowserMonitoringMode mode) => mode switch
    {
        BrowserMonitoringMode.ManagedProtocol => "Full addresses",
        BrowserMonitoringMode.WindowTitleFallback => "Page titles only",
        BrowserMonitoringMode.Unavailable => "Not being recorded",
        _ => "Unknown"
    };

    /// <summary>What the label means, and what to do about it. Shown on demand.</summary>
    public static string Explanation(BrowserMonitoringMode mode) => mode switch
    {
        BrowserMonitoringMode.ManagedProtocol =>
            "CAMS is managing this browser, so the exact address of each page is recorded.",
        BrowserMonitoringMode.WindowTitleFallback =>
            "CAMS can read this browser's window title but not the page address. " +
            "Browsing is still being recorded, just less precisely — this is not idle.",
        BrowserMonitoringMode.Unavailable =>
            "CAMS cannot read this browser at all, so nothing from it is recorded. " +
            "Check that the CAMS Student Client is running on the workstation and " +
            "that the managed browser is installed.",
        _ => "This browser reported a state CAMS does not recognise."
    };

    public static string BadgeClass(BrowserMonitoringMode mode) => mode switch
    {
        BrowserMonitoringMode.ManagedProtocol => "text-bg-success",
        BrowserMonitoringMode.WindowTitleFallback => "text-bg-warning",
        BrowserMonitoringMode.Unavailable => "text-bg-secondary",
        _ => "text-bg-secondary"
    };

    /// <summary>
    /// The label and explanation for every mode, keyed by the name the hub
    /// sends. Rendered into the monitoring page so the live tiles read from the
    /// same source as the server-rendered ones.
    /// </summary>
    public static IReadOnlyDictionary<string, object> ForScript() =>
        Enum.GetValues<BrowserMonitoringMode>().ToDictionary(
            mode => mode.ToString(),
            mode => (object)new { label = Label(mode), explanation = Explanation(mode) });
}
