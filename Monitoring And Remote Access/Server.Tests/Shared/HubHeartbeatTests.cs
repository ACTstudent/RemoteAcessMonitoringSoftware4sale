using Shared.Contracts;

namespace Server.Tests.SharedTests;

public class HubHeartbeatTests
{
    // The bug these guard against: the server timeout was tightened to 15s while
    // the desktop client still pinged every 15s, so a station in perfect health
    // was dropped whenever a ping ran late and the student watched it reconnect.
    [Fact]
    public void ClientTimeout_LeavesRoomForAMissedPing()
    {
        Assert.True(
            HubHeartbeat.ClientTimeout >= HubHeartbeat.KeepAlive * 2,
            $"A {HubHeartbeat.ClientTimeout.TotalSeconds}s timeout against a " +
            $"{HubHeartbeat.KeepAlive.TotalSeconds}s ping drops healthy clients: " +
            "the timeout must be at least double the keep-alive.");
    }

    [Fact]
    public void KeepAlive_IsFastEnoughToReportADropPromptly()
    {
        // A teacher acting on a stale monitoring view is the reason the interval
        // was shortened in the first place; keep that property asserted.
        Assert.True(HubHeartbeat.ClientTimeout <= TimeSpan.FromSeconds(30));
        Assert.True(HubHeartbeat.KeepAlive > TimeSpan.Zero);
    }
}
