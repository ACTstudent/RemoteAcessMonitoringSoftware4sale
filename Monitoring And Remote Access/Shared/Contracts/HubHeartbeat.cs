namespace Shared.Contracts;

/// <summary>
/// The heartbeat both ends of the monitoring hub agree on.
/// </summary>
/// <remarks>
/// These two values are one setting, not two. The server drops a client it has
/// not heard from within <see cref="ClientTimeout"/>, so a client that pings
/// less often than that is dropped while it is still healthy - and the student
/// sees "Reconnecting" for no reason a teacher can act on.
///
/// They lived apart before: the server tightened its timeout to 15s while the
/// desktop client kept the SignalR default 15s ping, leaving no margin at all
/// for a GC pause, a screen-capture burst or one late Wi-Fi frame. The guidance
/// is that the timeout should be at least double the ping; this is triple, and
/// matches what the browser clients already set for themselves.
///
/// Change these together or not at all.
/// </remarks>
public static class HubHeartbeat
{
    /// <summary>How often each side pings when it has nothing else to say.</summary>
    public static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(5);

    /// <summary>How long silence lasts before the peer is treated as gone.</summary>
    public static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(15);
}
