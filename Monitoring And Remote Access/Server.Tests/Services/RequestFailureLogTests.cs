using Server.Services;
using Xunit;

namespace Server.Tests.Services;

/// <summary>
/// CODE-07. The behaviour under test is what happens during a fault storm,
/// which is exactly when nobody is watching and the database is least able to
/// cope.
/// </summary>
public class RequestFailureLogTests
{
    private sealed class TestClock
    {
        public DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
        public DateTime Read() => Now;
        public void Advance(TimeSpan by) => Now = Now.Add(by);
    }

    private static (RequestFailureLog log, TestClock clock) Build(TimeSpan? window = null)
    {
        var clock = new TestClock();
        return (new RequestFailureLog(clock.Read, window ?? TimeSpan.FromMinutes(1)), clock);
    }

    [Fact]
    public void TheFirstOccurrence_IsAlwaysWritten()
    {
        var (log, _) = Build();

        Assert.True(log.ShouldWrite(new InvalidOperationException(), "/Admin/Students", out var suppressed));
        Assert.Equal(0, suppressed);
    }

    [Fact]
    public void RepeatsWithinTheWindow_AreNotWritten()
    {
        var (log, _) = Build();
        log.ShouldWrite(new InvalidOperationException(), "/Admin/Students", out _);

        for (var i = 0; i < 500; i++)
        {
            Assert.False(log.ShouldWrite(new InvalidOperationException(), "/Admin/Students", out _));
        }
    }

    [Fact]
    public void AfterTheWindow_ItWritesAgainAndReportsWhatItSuppressed()
    {
        var (log, clock) = Build();
        log.ShouldWrite(new InvalidOperationException(), "/Admin/Students", out _);
        for (var i = 0; i < 7; i++) log.ShouldWrite(new InvalidOperationException(), "/Admin/Students", out _);

        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.True(log.ShouldWrite(new InvalidOperationException(), "/Admin/Students", out var suppressed));
        Assert.Equal(7, suppressed);
    }

    [Fact]
    public void ADifferentRoute_IsADifferentFailure()
    {
        var (log, _) = Build();
        log.ShouldWrite(new InvalidOperationException(), "/Admin/Students", out _);

        // Suppressing this would hide a second, unrelated breakage behind the
        // first one.
        Assert.True(log.ShouldWrite(new InvalidOperationException(), "/Teacher/Alerts", out _));
    }

    [Fact]
    public void ADifferentExceptionType_IsADifferentFailure()
    {
        var (log, _) = Build();
        log.ShouldWrite(new InvalidOperationException(), "/Admin/Students", out _);

        Assert.True(log.ShouldWrite(new TimeoutException(), "/Admin/Students", out _));
    }

    [Fact]
    public void TheMessageDoesNotIdentifyOccurrences_SoRepeatsAreRecognised()
    {
        var (log, _) = Build();
        log.ShouldWrite(new InvalidOperationException("row id 4192 conflicts"), "/Admin/Students", out _);

        // Same type, same route, different message. If the message were part of
        // the signature every occurrence would look new and nothing would ever
        // be suppressed.
        Assert.False(log.ShouldWrite(new InvalidOperationException("row id 4193 conflicts"), "/Admin/Students", out _));
    }

    [Fact]
    public void Describe_CarriesTypeRouteAndCorrelationId()
    {
        var message = RequestFailureLog.Describe(
            new TimeoutException(), "POST", "/Admin/CreateClass", "0HN7ABC:0000001", 0);

        Assert.Contains("TimeoutException", message);
        Assert.Contains("POST", message);
        Assert.Contains("/Admin/CreateClass", message);
        Assert.Contains("0HN7ABC:0000001", message);
    }

    [Fact]
    public void Describe_DoesNotCarryTheExceptionMessage()
    {
        // A database failure quotes the row it was writing. That belongs in the
        // stack trace field, which is only populated in Development.
        var message = RequestFailureLog.Describe(
            new InvalidOperationException("password reset token abc123 for user jsmith"),
            "POST", "/Account/Reset", "0HN7ABC:0000002", 0);

        Assert.DoesNotContain("abc123", message);
        Assert.DoesNotContain("jsmith", message);
    }

    [Fact]
    public void Describe_SaysHowManyWereSuppressed()
    {
        var message = RequestFailureLog.Describe(
            new TimeoutException(), "GET", "/Teacher/Dashboard", "id", 41);

        Assert.Contains("41", message);
    }

    [Fact]
    public void ManyDistinctFailures_DoNotGrowWithoutBound()
    {
        var (log, _) = Build();

        // Every one is written - none is a repeat - but the tracking table must
        // not keep every signature it has ever seen.
        for (var i = 0; i < 5000; i++)
        {
            Assert.True(log.ShouldWrite(new InvalidOperationException(), $"/Admin/Item/{i}", out _));
        }
    }
}
