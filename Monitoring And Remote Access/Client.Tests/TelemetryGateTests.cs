using Client.Services;
using Xunit;

namespace Client.Tests;

/// <summary>
/// CODE-03. These decisions used to live as five fields inside MainForm's status
/// loop, where they could only be checked by running the agent on a second
/// machine. There is no second machine, so the compensating measure is that the
/// logic is now reachable without a window.
///
/// The behaviour is asserted as it was, not as it might better be. Where the
/// original was arguably wrong - a student active at the start of a session
/// produces no idle report at all - that is recorded as a test rather than
/// quietly fixed, because an extraction that also changes what goes over the
/// wire cannot be reviewed as an extraction.
/// </summary>
public class TelemetryGateTests
{
    private static readonly DateTime T0 = new(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc);

    // ---- idle ----

    [Fact]
    public void Idle_IsReportedWhenItChanges()
    {
        var gate = new TelemetryGate();

        Assert.True(gate.ShouldReportIdle(true));
        Assert.True(gate.ShouldReportIdle(false));
        Assert.True(gate.ShouldReportIdle(true));
    }

    [Fact]
    public void Idle_IsNotReportedWhenItStaysTheSame()
    {
        var gate = new TelemetryGate();
        gate.ShouldReportIdle(true);

        Assert.False(gate.ShouldReportIdle(true));
        Assert.False(gate.ShouldReportIdle(true));
    }

    [Fact]
    public void AStudentActiveFromTheStart_ProducesNoIdleReport()
    {
        var gate = new TelemetryGate();

        // Preserved from MainForm, where the field started false. Worth knowing
        // about: the server never hears "not idle" until the student has been
        // idle once.
        Assert.False(gate.ShouldReportIdle(false));
    }

    // ---- active application ----

    [Fact]
    public void ActiveApp_IsReportedOnceTheIntervalHasPassed()
    {
        var gate = new TelemetryGate();

        Assert.True(gate.ShouldReportActiveApp(T0));
        Assert.False(gate.ShouldReportActiveApp(T0.AddSeconds(4)));
        Assert.True(gate.ShouldReportActiveApp(T0.AddSeconds(6)));
    }

    [Fact]
    public void ActiveApp_TreatsExactlyTheIntervalAsTooSoon()
    {
        var gate = new TelemetryGate();
        gate.ShouldReportActiveApp(T0);

        // The original compared with > rather than >=. Recorded so a later
        // tidy-up does not flip it and double the reporting rate.
        Assert.False(gate.ShouldReportActiveApp(T0.Add(TelemetryGate.ActiveAppInterval)));
    }

    // ---- website ----

    [Fact]
    public void Website_IsReportedWhenTheDomainChanges()
    {
        var gate = new TelemetryGate();

        Assert.True(gate.ShouldReportWebsite("chrome", "example.org"));
        Assert.False(gate.ShouldReportWebsite("chrome", "example.org"));
        Assert.True(gate.ShouldReportWebsite("chrome", "other.org"));
    }

    [Fact]
    public void Website_IsReportedWhenTheSameDomainMovesToAnotherBrowser()
    {
        var gate = new TelemetryGate();
        gate.ShouldReportWebsite("chrome", "example.org");

        Assert.True(gate.ShouldReportWebsite("brave", "example.org"));
    }

    [Fact]
    public void Website_ReportsAgainAfterLeavingAndReturning()
    {
        var gate = new TelemetryGate();
        gate.ShouldReportWebsite("chrome", "example.org");

        gate.ShouldReportWebsite(null, null);   // no site in the foreground

        Assert.True(gate.ShouldReportWebsite("chrome", "example.org"));
    }

    [Fact]
    public void Website_WithNoDomain_IsNeverReported()
    {
        var gate = new TelemetryGate();

        Assert.False(gate.ShouldReportWebsite("chrome", null));
        Assert.False(gate.ShouldReportWebsite("chrome", ""));
    }

    // ---- browser collection state ----

    [Fact]
    public void BrowserStatus_IsReportedPerBrowserAndOnlyOnChange()
    {
        var gate = new TelemetryGate();

        Assert.True(gate.ShouldReportBrowserStatus("chrome", "ManagedProtocol:ok"));
        Assert.False(gate.ShouldReportBrowserStatus("chrome", "ManagedProtocol:ok"));

        // A different browser is tracked separately.
        Assert.True(gate.ShouldReportBrowserStatus("brave", "ManagedProtocol:ok"));
    }

    [Fact]
    public void BrowserStatus_ReportsTheSameModeWithADifferentReason()
    {
        var gate = new TelemetryGate();
        gate.ShouldReportBrowserStatus("chrome", "WindowTitleFallback:Foreground URL captured");

        // Same mode, different detail. The teacher is told why it changed.
        Assert.True(gate.ShouldReportBrowserStatus(
            "chrome", "WindowTitleFallback:Foreground browser detected; URL unavailable"));
    }

    [Fact]
    public void BrowserStatus_IgnoresCaseInTheBrowserName()
    {
        var gate = new TelemetryGate();
        gate.ShouldReportBrowserStatus("Chrome", "x");

        Assert.False(gate.ShouldReportBrowserStatus("chrome", "x"));
    }

    // ---- infractions ----

    [Fact]
    public void Infraction_IsReportedOnceThenStaysQuietForTheCooldown()
    {
        var gate = new TelemetryGate();

        Assert.True(gate.ShouldReportInfraction("app", "game.exe", T0));
        Assert.False(gate.ShouldReportInfraction("app", "game.exe", T0.AddSeconds(29)));
        Assert.True(gate.ShouldReportInfraction("app", "game.exe", T0.AddSeconds(31)));
    }

    [Fact]
    public void Infraction_SeparatesTargetsAndTypes()
    {
        var gate = new TelemetryGate();
        gate.ShouldReportInfraction("app", "game.exe", T0);

        // A second blocked thing must not be hidden behind the first.
        Assert.True(gate.ShouldReportInfraction("app", "other.exe", T0));
        Assert.True(gate.ShouldReportInfraction("website", "game.exe", T0));
    }

    [Fact]
    public void Infraction_AStudentParkedOnABlockedPage_ProducesOneAlertPerCooldown()
    {
        var gate = new TelemetryGate();
        var reported = 0;

        // The loop runs every 5 seconds for five minutes.
        for (var second = 0; second < 300; second += 5)
        {
            if (gate.ShouldReportInfraction("website", "blocked.example", T0.AddSeconds(second))) reported++;
        }

        // Ten, not sixty. This is the difference between a usable alert list
        // and an unreadable one.
        Assert.Equal(10, reported);
    }

    // ---- reset ----

    [Fact]
    public void Reset_ForgetsEverything_SoTheNextStudentStartsClean()
    {
        var gate = new TelemetryGate();
        gate.ShouldReportIdle(true);
        gate.ShouldReportActiveApp(T0);
        gate.ShouldReportWebsite("chrome", "example.org");
        gate.ShouldReportBrowserStatus("chrome", "x");
        gate.ShouldReportInfraction("app", "game.exe", T0);

        gate.Reset();

        Assert.True(gate.ShouldReportIdle(true));
        Assert.True(gate.ShouldReportActiveApp(T0));
        Assert.True(gate.ShouldReportWebsite("chrome", "example.org"));
        Assert.True(gate.ShouldReportBrowserStatus("chrome", "x"));
        Assert.True(gate.ShouldReportInfraction("app", "game.exe", T0));
    }
}
