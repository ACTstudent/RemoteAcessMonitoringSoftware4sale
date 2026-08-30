using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Tests.Services;

public sealed class TelemetryRetentionCleanerTests
{
    [Fact]
    public async Task Cleanup_DeletesExpiredRowsInBatchesAndKeepsActiveRemoteSession()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var now = DateTime.UtcNow;
        var old = now.AddDays(-31);
        var recent = now.AddDays(-1);

        await using (var seed = new ApplicationDbContext(dbOptions))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.ActivityEvents.AddRange(
                new ActivityEvent { ConnectionId = "old", StudentId = "1", PcName = "PC", EventType = "Connected", Timestamp = old },
                new ActivityEvent { ConnectionId = "new", StudentId = "1", PcName = "PC", EventType = "Connected", Timestamp = recent });
            seed.UsageLogs.AddRange(
                new UsageLog { PcName = "PC", AppName = "old", Timestamp = old },
                new UsageLog { PcName = "PC", AppName = "new", Timestamp = recent });
            seed.WebsiteUsageLogs.AddRange(
                new WebsiteUsageLog { Domain = "old.example", Browser = "browser", Timestamp = old },
                new WebsiteUsageLog { Domain = "new.example", Browser = "browser", Timestamp = recent });
            seed.IdleIntervals.AddRange(
                new IdleInterval { ConnectionId = "old", StudentId = "1", PcName = "PC", StartedAt = old },
                new IdleInterval { ConnectionId = "new", StudentId = "1", PcName = "PC", StartedAt = recent });
            seed.MonitoringAlerts.AddRange(
                Alert("old", old), Alert("new", recent));
            seed.RemoteCommandLogs.AddRange(
                Command("old", old), Command("new", recent));
            seed.RemoteControlSessions.AddRange(
                Session("old", old, isActive: false),
                Session("active", old, isActive: true),
                Session("new", recent, isActive: false));
            await seed.SaveChangesAsync();
        }

        await using (var db = new ApplicationDbContext(dbOptions))
        {
            var deleted = await TelemetryRetentionCleaner.CleanupAsync(db, new TelemetryRetentionOptions
            {
                BatchSize = 1,
                ActivityEventDays = 30,
                ConnectionLogDays = 30,
                UsageLogDays = 30,
                WebsiteUsageLogDays = 30,
                IdleIntervalDays = 30,
                MonitoringAlertDays = 30,
                RemoteLogDays = 30
            }, now);

            Assert.Equal(7, deleted);
            Assert.Single(await db.ActivityEvents.ToListAsync());
            Assert.Single(await db.UsageLogs.ToListAsync());
            Assert.Single(await db.WebsiteUsageLogs.ToListAsync());
            Assert.Single(await db.IdleIntervals.ToListAsync());
            Assert.Single(await db.MonitoringAlerts.ToListAsync());
            Assert.Single(await db.RemoteCommandLogs.ToListAsync());
            Assert.Equal(2, await db.RemoteControlSessions.CountAsync());
            Assert.Contains(await db.RemoteControlSessions.ToListAsync(), session => session.IsActive);
        }
    }

    private static MonitoringAlert Alert(string studentId, DateTime timestamp) => new()
    {
        StudentId = studentId,
        PcName = "PC",
        Severity = "Warning",
        Title = "Alert",
        Message = "Alert",
        CreatedAt = timestamp,
        FirstSeenAt = timestamp,
        LastSeenAt = timestamp
    };

    private static RemoteCommandLog Command(string studentId, DateTime timestamp) => new()
    {
        TeacherId = 1,
        StudentId = studentId,
        PcName = "PC",
        Command = "Test",
        Timestamp = timestamp
    };

    private static RemoteControlSession Session(string studentId, DateTime timestamp, bool isActive) => new()
    {
        TeacherId = 1,
        StudentId = studentId,
        PcName = "PC",
        ConnectionId = studentId,
        StartedAt = timestamp,
        EndedAt = isActive ? null : timestamp,
        IsActive = isActive
    };
}
