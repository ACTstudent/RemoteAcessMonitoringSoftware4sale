using Client.Services;
using Shared.Contracts;
using Xunit;

namespace Client.Tests;

/// <summary>
/// CODE-03. The session clock was two fields on MainForm touched from four
/// places, one of them a WinForms Tick handler. Out here the rules can be
/// stated once and checked without a machine to run the agent on.
/// </summary>
public class SessionClockTests
{
    [Fact]
    public void StartsWithNoSession()
    {
        var clock = new SessionClock();

        Assert.Equal(LabSessionStatus.None, clock.Status);
        Assert.Equal(0, clock.ElapsedSeconds);
        Assert.False(clock.IsRunning);
    }

    [Fact]
    public void TicksOnlyWhileRunning()
    {
        var clock = new SessionClock();

        Assert.False(clock.Tick());
        Assert.Equal(0, clock.ElapsedSeconds);

        clock.Apply(LabSessionStatus.Running, 0);
        Assert.True(clock.Tick());
        Assert.Equal(1, clock.ElapsedSeconds);
    }

    [Fact]
    public void APausedSessionDoesNotAdvance()
    {
        var clock = new SessionClock();
        clock.Apply(LabSessionStatus.Running, 100);
        clock.Apply(LabSessionStatus.Paused, 100);

        Assert.False(clock.Tick());
        Assert.Equal(100, clock.ElapsedSeconds);
    }

    [Fact]
    public void TickReportsWhetherAnythingChanged_SoAPausedTimerIsNotRepainted()
    {
        var clock = new SessionClock();
        clock.Apply(LabSessionStatus.Paused, 10);

        // Sixty pointless repaints a minute is the thing this prevents.
        Assert.False(clock.Tick());
    }

    [Fact]
    public void TheServersElapsedTimeWins()
    {
        var clock = new SessionClock();
        clock.Apply(LabSessionStatus.Running, 0);
        clock.Tick();
        clock.Tick();

        // A client that was asleep or throttled has drifted. The student is
        // shown the session's real age, not the agent's own count.
        clock.Apply(LabSessionStatus.Running, 3600);

        Assert.Equal(3600, clock.ElapsedSeconds);
    }

    [Fact]
    public void ANegativeElapsedTimeIsClampedRatherThanShown()
    {
        var clock = new SessionClock();

        clock.Apply(LabSessionStatus.Running, -5);

        Assert.Equal(0, clock.ElapsedSeconds);
        Assert.Equal("00:00", clock.Display());
    }

    [Fact]
    public void EndingStopsAndResets()
    {
        var clock = new SessionClock();
        clock.Apply(LabSessionStatus.Running, 90);

        clock.End();

        Assert.Equal(LabSessionStatus.Ended, clock.Status);
        Assert.Equal(0, clock.ElapsedSeconds);
        Assert.False(clock.IsRunning);
        Assert.False(clock.Tick());
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(9, "00:09")]
    [InlineData(60, "01:00")]
    [InlineData(599, "09:59")]
    [InlineData(3600, "60:00")]
    [InlineData(7265, "121:05")]
    public void DisplayIsMinutesAndSeconds_AndDoesNotWrapAtAnHour(int seconds, string expected)
    {
        var clock = new SessionClock();
        clock.Apply(LabSessionStatus.Running, seconds);

        // A two-hour session reads 121:05, not 01:05. Wrapping would tell a
        // student their session had just begun.
        Assert.Equal(expected, clock.Display());
    }
}
