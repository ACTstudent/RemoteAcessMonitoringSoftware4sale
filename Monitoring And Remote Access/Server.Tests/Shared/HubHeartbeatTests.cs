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

    // The browser pages cannot read HubHeartbeat, so they carry the values as
    // literals - and they were missed when the desktop client was fixed, still
    // pinging at SignalR's 15s default against the server's 15s timeout.
    [Theory]
    [InlineData("teacher-alert-badge.js")]
    [InlineData("student-session.js")]
    public void BrowserHubConnections_UseTheSharedHeartbeat(string script)
    {
        var source = File.ReadAllText(Path.Combine(FindServerProject(), "wwwroot", "js", script));

        Assert.Contains($"keepAliveIntervalInMilliseconds = {(int)HubHeartbeat.KeepAlive.TotalMilliseconds};", source);
        Assert.Contains($"serverTimeoutInMilliseconds = {(int)HubHeartbeat.ClientTimeout.TotalMilliseconds};", source);
    }

    private static string FindServerProject()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var server = Path.Combine(directory.FullName, "Server");
            if (Directory.Exists(Path.Combine(server, "wwwroot", "js"))) return server;
        }
        throw new DirectoryNotFoundException("The Server project was not found above the test output directory.");
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
