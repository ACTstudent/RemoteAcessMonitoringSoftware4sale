using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Services;
using Shared.Contracts;

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
    public async Task RecordWebsiteUsage_ResolvesAlphanumericStudentNumber()
    {
        await using var db = CreateContext();
        var student = new Server.Models.Student
        {
            StudentNumber = "STU-A10",
            Username = "student-a10",
            FullName = "Student A10"
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var service = new TelemetryService(db);

        await service.RecordWebsiteUsageAsync("connection-1", "STU-A10", "PC-01", "example.com", "chrome", DateTime.UtcNow);

        Assert.Equal(student.Id, (await db.WebsiteUsageLogs.SingleAsync()).StudentId);
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
    public async Task RecordDisconnected_ClosesOpenIdleInterval()
    {
        await using var db = CreateContext();
        var service = new TelemetryService(db);
        var started = DateTime.UtcNow.AddMinutes(-2);
        var disconnected = DateTime.UtcNow.AddMinutes(-1);
        await service.RecordIdleStatusAsync("connection-1", "student-1", "PC-01", true, started);

        await service.RecordDisconnectedAsync("connection-1", "student-1", "PC-01", timestamp: disconnected);

        Assert.Equal(disconnected, (await db.IdleIntervals.SingleAsync()).EndedAt);
        Assert.Contains(await db.ActivityEvents.ToListAsync(), activity => activity.EventType == "Disconnected");
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
        Assert.Single(await db.ActivityEvents.ToListAsync());
    }

    [Fact]
    public async Task RecordIdleStatus_OutOfOrderCloseDoesNotCreateInvalidInterval()
    {
        await using var db = CreateContext();
        var service = new TelemetryService(db);
        var started = DateTime.UtcNow.AddMinutes(-1);

        await service.RecordIdleStatusAsync("connection-1", "student-1", "PC-01", true, started);
        await service.RecordIdleStatusAsync("connection-1", "student-1", "PC-01", false, started.AddSeconds(-1));

        var interval = await db.IdleIntervals.SingleAsync();
        Assert.Null(interval.EndedAt);
        Assert.Single(await db.ActivityEvents.ToListAsync());
    }

    [Fact]
    public async Task RecordActivityEvent_NormalizesUnspecifiedTimestampToUtc()
    {
        await using var db = CreateContext();
        var service = new TelemetryService(db);
        var timestamp = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-1), DateTimeKind.Unspecified);

        await service.RecordActivityEventAsync("connection-1", "student-1", "PC-01", "Other", timestamp: timestamp);

        Assert.Equal(DateTimeKind.Utc, (await db.ActivityEvents.SingleAsync()).Timestamp.Kind);
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

    [Fact]
    public async Task RecordBatch_PersistsOnceInOrderAndNormalizesPrivateValues()
    {
        await using var db = CreateContext();
        var service = new TelemetryService(db);
        var started = DateTime.UtcNow.AddMinutes(-2);
        var ended = started.AddSeconds(10);
        var items = new List<TelemetryBatchItem>
        {
            TelemetryBatchItem.From(new IdleStatusMessage("connection-1", "42", "PC-01", true, started)),
            TelemetryBatchItem.From(new IdleStatusMessage("connection-1", "42", "PC-01", false, ended)),
            TelemetryBatchItem.From(new ActiveAppMessage("connection-1", "42", "PC-01",
                "chrome - Private page title", ended.AddSeconds(1))),
            TelemetryBatchItem.From(new WebsiteActivityMessage("connection-1", "42", "PC-01",
                "https://user:secret@example.com/private", "Chrome", ended.AddSeconds(2))),
            TelemetryBatchItem.From(new BrowserMonitoringStatusMessage("connection-1", "42", "PC-01",
                "chrome", BrowserMonitoringMode.ManagedProtocol, ended.AddSeconds(3)))
        };

        await service.RecordBatchAsync(items);

        var interval = await db.IdleIntervals.SingleAsync();
        Assert.Equal(ended, interval.EndedAt);
        Assert.Equal("chrome", (await db.UsageLogs.SingleAsync()).AppName);
        Assert.Equal("example.com", (await db.WebsiteUsageLogs.SingleAsync()).Domain);
        var browserStatus = await db.BrowserMonitoringRecords.SingleAsync();
        Assert.Equal(BrowserMonitoringMode.ManagedProtocol, browserStatus.Mode);
        Assert.Equal("chrome", browserStatus.Browser);
        Assert.Equal(4, await db.ActivityEvents.CountAsync());
    }

    [Fact]
    public async Task RecordBatch_ClosesExistingIdleIntervalThenOpensNextInterval()
    {
        await using var db = CreateContext();
        var service = new TelemetryService(db);
        var started = DateTime.UtcNow.AddMinutes(-3);
        var resumed = started.AddMinutes(1);
        var idleAgain = resumed.AddMinutes(1);
        await service.RecordIdleStatusAsync("connection-1", "student-1", "PC-01", true, started);

        await service.RecordBatchAsync(new[]
        {
            TelemetryBatchItem.From(new IdleStatusMessage(
                "connection-1", "student-1", "PC-01", false, resumed)),
            TelemetryBatchItem.From(new IdleStatusMessage(
                "connection-1", "student-1", "PC-01", true, idleAgain))
        });

        var intervals = await db.IdleIntervals.OrderBy(interval => interval.StartedAt).ToListAsync();
        Assert.Equal(2, intervals.Count);
        Assert.Equal(resumed, intervals[0].EndedAt);
        Assert.Null(intervals[1].EndedAt);
        Assert.Equal(idleAgain, intervals[1].StartedAt);
    }
}
