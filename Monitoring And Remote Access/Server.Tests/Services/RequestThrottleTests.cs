using Server.Services;

namespace Server.Tests.Services;

/// <summary>
/// The per-address request ceiling.
///
/// The window logic used to sit inline in the request pipeline, where the only
/// way to check it was to run a server and count responses. That is how the
/// login defect went unnoticed: the ceiling was applied to every request on
/// /Account/Login, so loading the form spent the same budget as submitting it.
/// The pipeline now meters only the POST; these tests pin the window itself.
/// </summary>
public class RequestThrottleTests
{
    private const string Key = "10.0.0.5:/Account/Login";

    /// <summary>A clock the test moves by hand, so a window boundary costs no wall time.</summary>
    private sealed class TestClock
    {
        public DateTime Now { get; set; } = new(2026, 9, 6, 8, 0, 0, DateTimeKind.Utc);
        public Func<DateTime> Read => () => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    [Fact]
    public void RequestsUpToTheLimitAreAllowed()
    {
        var throttle = new RequestThrottle(new TestClock().Read);

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            Assert.False(throttle.ShouldRefuse(Key, 10), $"attempt {attempt} should have been allowed");
        }
    }

    [Fact]
    public void TheRequestPastTheLimitIsRefused()
    {
        var throttle = new RequestThrottle(new TestClock().Read);
        for (var attempt = 1; attempt <= 10; attempt++) throttle.ShouldRefuse(Key, 10);

        Assert.True(throttle.ShouldRefuse(Key, 10));
    }

    [Fact]
    public void RefusalContinuesForTheRestOfTheWindow()
    {
        var clock = new TestClock();
        var throttle = new RequestThrottle(clock.Read);
        for (var attempt = 1; attempt <= 11; attempt++) throttle.ShouldRefuse(Key, 10);

        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.True(throttle.ShouldRefuse(Key, 10));
    }

    [Fact]
    public void TheWindowReopensAfterAMinute()
    {
        var clock = new TestClock();
        var throttle = new RequestThrottle(clock.Read);
        for (var attempt = 1; attempt <= 11; attempt++) throttle.ShouldRefuse(Key, 10);

        clock.Advance(RequestThrottle.WindowLength);

        Assert.False(throttle.ShouldRefuse(Key, 10));
    }

    [Fact]
    public void OneAddressDoesNotSpendAnothersBudget()
    {
        var throttle = new RequestThrottle(new TestClock().Read);
        for (var attempt = 1; attempt <= 11; attempt++) throttle.ShouldRefuse("10.0.0.5:/Account/Login", 10);

        Assert.False(throttle.ShouldRefuse("10.0.0.6:/Account/Login", 10));
    }

    [Fact]
    public void OnePathDoesNotSpendAnothersBudget()
    {
        var throttle = new RequestThrottle(new TestClock().Read);
        for (var attempt = 1; attempt <= 11; attempt++) throttle.ShouldRefuse("10.0.0.5:/Account/Login", 10);

        Assert.False(throttle.ShouldRefuse("10.0.0.5:/api/client/login", 120));
    }

    [Fact]
    public void EachKeyKeepsItsOwnLimit()
    {
        var throttle = new RequestThrottle(new TestClock().Read);

        // The API ceiling is far higher than the login one; exceeding the login
        // count must not refuse an API caller.
        for (var attempt = 1; attempt <= 40; attempt++)
        {
            Assert.False(throttle.ShouldRefuse("10.0.0.5:/api/telemetry", 120));
        }
    }

    [Fact]
    public void ExpiredWindowsAreSweptSoTheMapCannotGrowWithoutBound()
    {
        var clock = new TestClock();
        var throttle = new RequestThrottle(clock.Read);

        for (var address = 0; address < 50; address++) throttle.ShouldRefuse($"10.0.1.{address}:/Account/Login", 10);
        Assert.Equal(50, throttle.TrackedKeys);

        // Past the sweep interval, and past the age at which a window is dropped.
        clock.Advance(RequestThrottle.SweepInterval + TimeSpan.FromSeconds(1));
        throttle.ShouldRefuse("10.0.2.1:/Account/Login", 10);

        // The 50 stale keys are gone; only the key that triggered the sweep remains.
        Assert.Equal(1, throttle.TrackedKeys);
    }

    [Fact]
    public void AnUnseenKeyIsRefusedOnceTheMapIsFull()
    {
        var throttle = new RequestThrottle(new TestClock().Read, maxTrackedKeys: 3);
        for (var address = 0; address < 3; address++)
        {
            Assert.False(throttle.ShouldRefuse($"10.0.3.{address}:/Account/Login", 10));
        }

        Assert.True(throttle.ShouldRefuse("10.0.3.99:/Account/Login", 10));
    }

    [Fact]
    public void AKnownKeyStillCountsOnceTheMapIsFull()
    {
        var throttle = new RequestThrottle(new TestClock().Read, maxTrackedKeys: 3);
        for (var address = 0; address < 3; address++) throttle.ShouldRefuse($"10.0.4.{address}:/Account/Login", 10);

        // A caller already being tracked must keep being counted rather than
        // slipping past the ceiling because the map happens to be full.
        for (var attempt = 2; attempt <= 10; attempt++)
        {
            Assert.False(throttle.ShouldRefuse("10.0.4.0:/Account/Login", 10));
        }
        Assert.True(throttle.ShouldRefuse("10.0.4.0:/Account/Login", 10));
    }

    [Fact]
    public void ConcurrentRequestsOnOneKeyAreAllCounted()
    {
        var throttle = new RequestThrottle(new TestClock().Read);
        var refusals = 0;

        Parallel.For(0, 100, _ =>
        {
            if (throttle.ShouldRefuse(Key, 10)) Interlocked.Increment(ref refusals);
        });

        // Exactly ten get through however the calls interleave.
        Assert.Equal(90, refusals);
    }
}
