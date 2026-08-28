using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Services;

namespace Server.Tests.Services;

public class TelemetryServiceTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task RecordApplicationUsage_PersistsUsageAndActivity()
    {
        await using var db = CreateContext();
        var service = new TelemetryService(db);
        var timestamp = DateTime.UtcNow.AddMinutes(-1);

        await service.RecordApplicationUsageAsync("connection-1", "42", "PC-01", "code.exe", timestamp);

        var usage = await db.UsageLogs.SingleAsync();
        var activity = await db.ActivityEvents.SingleAsync();
        Assert.Equal(42, usage.StudentId);
        Assert.Equal("code.exe", usage.AppName);
        Assert.Equal("ApplicationUsed", activity.EventType);
        Assert.Equal("connection-1", activity.ConnectionId);
    }

    [Fact]
    public async Task RecordIdleStatus_OpensThenClosesOneInterval()
    {
        await using var db = CreateContext();
        var service = new TelemetryService(db);
        var started = DateTime.UtcNow.AddMinutes(-2);
        var ended = DateTime.UtcNow.AddMinutes(-1);

        await service.RecordIdleStatusAsync("connection-1", "student-1", "PC-01", true, started);
        await service.RecordIdleStatusAsync("connection-1", "student-1", "PC-01", false, ended);

        var interval = await db.IdleIntervals.SingleAsync();
        Assert.Equal(started, interval.StartedAt);
        Assert.Equal(ended, interval.EndedAt);
        Assert.Equal(2, await db.ActivityEvents.CountAsync());
    }

    [Fact]
    public async Task RecordIdleStatus_DuplicateIdleReportDoesNotOpenAnotherInterval()
    {
        await using var db = CreateContext();
        var service = new TelemetryService(db);
        var timestamp = DateTime.UtcNow.AddMinutes(-1);

        await service.RecordIdleStatusAsync("connection-1", "student-1", "PC-01", true, timestamp);
        await service.RecordIdleStatusAsync("connection-1", "student-1", "PC-01", true, timestamp.AddSeconds(10));

        Assert.Single(await db.IdleIntervals.ToListAsync());
    }

    [Fact]
    public async Task RecordActivityEvent_RejectsOversizedPayload()
    {
        await using var db = CreateContext();
        var service = new TelemetryService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.RecordActivityEventAsync(
            "connection-1", "student-1", "PC-01", "Other", details: new string('x', 1001)));
        Assert.Empty(await db.ActivityEvents.ToListAsync());
    }

    [Fact]
    public async Task RecordTelemetry_RejectsTimestampsOutsideAllowedWindow()
    {
        await using var db = CreateContext();
        var service = new TelemetryService(db);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.RecordApplicationUsageAsync(
            "connection-1", "student-1", "PC-01", "app.exe", DateTime.UtcNow.AddMinutes(6)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.RecordWebsiteUsageAsync(
            "connection-1", "student-1", "PC-01", "example.com", "browser", DateTime.UtcNow.AddDays(-8)));
        Assert.Empty(await db.ActivityEvents.ToListAsync());
    }

    [Fact]
    public async Task RecordWebsiteUsage_RejectsOversizedBrowserAndDomain()
    {
        await using var db = CreateContext();
        var service = new TelemetryService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.RecordWebsiteUsageAsync(
            "connection-1", "student-1", "PC-01", new string('d', 301), "browser", DateTime.UtcNow));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RecordWebsiteUsageAsync(
            "connection-1", "student-1", "PC-01", "example.com", new string('b', 51), DateTime.UtcNow));
        Assert.Empty(await db.WebsiteUsageLogs.ToListAsync());
    }
}
