using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Tests.Services;

public class AnalyticsServiceTests
{
    [Fact]
    public async Task StudentReport_AggregatesActiveAndIdleDurations()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var teacher = new Teacher { TeacherId = 7, FirstName = "T", LastName = "T", Username = "teacher", PasswordHash = "hash" };
        var student = new Student { Id = 4, StudentNumber = "S-4", FullName = "Student", Username = "student", PasswordHash = "hash", AdviserId = 7 };
        var from = DateTime.UtcNow.AddMinutes(-30);
        db.Teachers.Add(teacher);
        db.Students.Add(student);
        db.LabSessions.Add(new LabSession { StudentId = 4, TeacherId = 7, StartTime = from, EndTime = from.AddMinutes(20), Status = "Ended", PCName = "PC-1" });
        db.IdleIntervals.Add(new IdleInterval { StudentId = "S-4", ConnectionId = "c", PcName = "PC-1", StartedAt = from.AddMinutes(5), EndedAt = from.AddMinutes(10) });
        db.ActivityEvents.Add(new ActivityEvent { StudentId = "S-4", ConnectionId = "c", PcName = "PC-1", EventType = "ApplicationUsed", ApplicationName = "editor", Timestamp = from.AddMinutes(1) });
        await db.SaveChangesAsync();

        var report = await new AnalyticsService(db).GetStudentReportAsync(4, 7, from, from.AddMinutes(20));

        Assert.NotNull(report);
        Assert.Equal(15, report!.Durations.ActiveMinutes, 1);
        Assert.Equal(5, report.Durations.IdleMinutes, 1);
        Assert.Equal(19, report.Durations.ApplicationMinutes, 1);
    }

    [Fact]
    public async Task AlertAcknowledgement_CoversEveryStudentGlobally()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        db.Students.Add(new Student { Id = 1, StudentNumber = "S-1", FullName = "S", Username = "s", PasswordHash = "h", AdviserId = 1 });
        db.Students.Add(new Student { Id = 2, StudentNumber = "S-2", FullName = "S", Username = "s2", PasswordHash = "h", AdviserId = 2 });
        db.MonitoringAlerts.AddRange(new MonitoringAlert { MonitoringAlertId = 10, StudentId = "S-1", PcName = "PC", Title = "A", Message = "M" }, new MonitoringAlert { MonitoringAlertId = 11, StudentId = "S-2", PcName = "PC", Title = "B", Message = "M" });
        await db.SaveChangesAsync();
        var service = new AnalyticsService(db);

        // Global access: a teacher can acknowledge alerts for any student, not just their roster.
        Assert.True(await service.SetAlertAcknowledgedAsync(10, 1, true));
        Assert.True(await service.SetAlertAcknowledgedAsync(11, 1, true));
        Assert.True((await db.MonitoringAlerts.FindAsync(10))!.IsAcknowledged);
        Assert.True((await db.MonitoringAlerts.FindAsync(11))!.IsAcknowledged);
    }

    [Fact]
    public async Task AlertExport_ReturnsEveryStudentGlobally()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        db.Students.AddRange(
            new Student { Id = 1, StudentNumber = "S-1", FullName = "S", Username = "s", PasswordHash = "h", AdviserId = 1 },
            new Student { Id = 2, StudentNumber = "S-2", FullName = "S2", Username = "s2", PasswordHash = "h", AdviserId = 2 });
        db.MonitoringAlerts.AddRange(new MonitoringAlert { StudentId = "S-1", PcName = "PC", Title = "A", Message = "M" }, new MonitoringAlert { StudentId = "S-2", PcName = "PC", Title = "B", Message = "M" });
        await db.SaveChangesAsync();
        var alerts = await new AnalyticsService(db).GetAlertExportAsync(1);
        // Global access: every student's alerts are exported for any teacher.
        Assert.Equal(2, alerts.Count);
        Assert.Contains(alerts, a => a.StudentId == "S-1");
        Assert.Contains(alerts, a => a.StudentId == "S-2");
    }

    [Fact]
    public async Task ClassReport_RestrictsToTeacherClassAndDateRange()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        db.Teachers.Add(new Teacher { TeacherId = 1, Username = "t", PasswordHash = "h" });
        db.Classes.Add(new Class { ClassId = 2, ClassName = "Lab", TeacherId = 1 });
        db.Students.Add(new Student { Id = 3, FullName = "S", Username = "s", PasswordHash = "h", ClassId = 2 });
        db.LabSessions.Add(new LabSession { StudentId = 3, TeacherId = 1, PCName = "PC", StartTime = DateTime.UtcNow.AddMinutes(-10), EndTime = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var report = await new AnalyticsService(db).GetClassReportAsync(2, 1, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        Assert.NotNull(report);
        Assert.Equal(1, report!.TotalSessions);
    }

    [Fact]
    public async Task ClassReport_IncludesEnrolledStudentsWithoutSessions()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        db.Teachers.Add(new Teacher { TeacherId = 1, Username = "t", PasswordHash = "h" });
        db.Classes.Add(new Class { ClassId = 2, ClassName = "Lab", TeacherId = 1 });
        db.Students.AddRange(
            new Student { Id = 3, FullName = "Present", Username = "p", PasswordHash = "h", ClassId = 2 },
            new Student { Id = 4, FullName = "Absent", Username = "a", PasswordHash = "h", ClassId = 2 });
        await db.SaveChangesAsync();
        var report = await new AnalyticsService(db).GetClassReportAsync(2, 1, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        Assert.NotNull(report);
        Assert.Equal(0, report!.SessionsByStudent["Absent"]);
    }

    [Fact]
    public async Task RemoteHistoryFiltersByStructuredStudentId()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        db.RemoteCommandLogs.AddRange(
            new RemoteCommandLog { TeacherId = 1, StudentId = "S-1", PcName = "PC-1", Command = "LockStudent", Details = "S-10 in text" },
            new RemoteCommandLog { TeacherId = 1, StudentId = "S-2", PcName = "PC-2", Command = "LockStudent" });
        await db.SaveChangesAsync();
        var result = await new AnalyticsService(db).GetRemoteHistoryAsync(1, studentId: "S-1");
        Assert.Single(result.Items);
        Assert.Equal("S-1", result.Items[0].StudentId);
    }

    [Fact]
    public async Task ActivityTimeline_FiltersLifecycleEventsAndPaginates()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        db.Students.Add(new Student { Id = 9, StudentNumber = "S-9", FullName = "S", Username = "s", PasswordHash = "h", AdviserId = 3 });
        var now = DateTime.UtcNow;
        db.ActivityEvents.AddRange(
            new ActivityEvent { StudentId = "S-9", PcName = "PC-9", ConnectionId = "c", EventType = "Connected", Timestamp = now.AddMinutes(-2) },
            new ActivityEvent { StudentId = "S-9", PcName = "PC-9", ConnectionId = "c", EventType = "Disconnected", Timestamp = now.AddMinutes(-1) });
        await db.SaveChangesAsync();
        var result = await new AnalyticsService(db).GetActivityTimelineAsync(9, 3, now.AddHours(-1), now.AddMinutes(1), eventType: "Disconnected");
        Assert.Single(result.Items);
        Assert.Equal("Disconnected", result.Items[0].EventType);
    }

    [Fact]
    public async Task LabUtilization_ClipsIdleToAuthorizedFilteredSessions()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var from = new DateTime(2026, 8, 30, 8, 0, 0, DateTimeKind.Utc);
        var teacher = new Teacher { TeacherId = 1, Username = "teacher-1", PasswordHash = "h" };
        var otherTeacher = new Teacher { TeacherId = 2, Username = "teacher-2", PasswordHash = "h" };
        var teacherClass = new Class { ClassId = 11, ClassName = "Teacher class", TeacherId = 1 };
        var otherClass = new Class { ClassId = 22, ClassName = "Other class", TeacherId = 2 };
        var pc1 = new Computer { ComputerId = 1, LaboratoryStation = "PC-01" };
        var pc2 = new Computer { ComputerId = 2, LaboratoryStation = "PC-02" };
        db.AddRange(teacher, otherTeacher, teacherClass, otherClass, pc1, pc2);
        db.Students.AddRange(
            new Student { Id = 10, StudentNumber = "S-10", FullName = "Authorized", Username = "s10", PasswordHash = "h", ClassId = 11 },
            new Student { Id = 20, StudentNumber = "S-20", FullName = "Unauthorized", Username = "s20", PasswordHash = "h", ClassId = 22 });
        db.LabSessions.AddRange(
            new LabSession { StudentId = 10, TeacherId = 1, Computer = pc1, PCName = "PC-01", StartTime = from, EndTime = from.AddMinutes(30), Status = "Ended" },
            new LabSession { StudentId = 20, TeacherId = 2, Computer = pc2, PCName = "PC-02", StartTime = from, EndTime = from.AddMinutes(60), Status = "Ended" });
        db.IdleIntervals.AddRange(
            new IdleInterval { StudentId = "S-10", ConnectionId = "inside", PcName = "PC-01", StartedAt = from.AddMinutes(10), EndedAt = from.AddMinutes(20) },
            new IdleInterval { StudentId = "S-10", ConnectionId = "outside", PcName = "PC-01", StartedAt = from.AddMinutes(40), EndedAt = from.AddMinutes(55) });
        await db.SaveChangesAsync();

        var service = new AnalyticsService(db);
        var report = await service.GetLabUtilizationAsync(1, from, from.AddHours(1), "PC-01", 11);

        Assert.NotNull(report);
        Assert.Equal(1, report!.RegisteredComputers);
        Assert.Equal(1, report.TotalSessions);
        Assert.Equal(60, report.Capacity.TotalMinutes, 1);
        Assert.Equal(30, report.Occupied.TotalMinutes, 1);
        Assert.Equal(10, report.Idle.TotalMinutes, 1);
        Assert.Equal(20, report.Active.TotalMinutes, 1);
        Assert.Equal(50, report.UtilizationPercent, 1);
        Assert.Null(await service.GetLabUtilizationAsync(1, from, from.AddHours(1), classId: 22));
    }

    [Fact]
    public async Task UnifiedTimeline_CombinesSourcesGloballyAcrossEveryStudent()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var from = new DateTime(2026, 8, 30, 9, 0, 0, DateTimeKind.Utc);
        db.Students.AddRange(
            new Student { Id = 1, StudentNumber = "S-1", FullName = "Allowed", Username = "allowed", PasswordHash = "h", AdviserId = 7 },
            new Student { Id = 2, StudentNumber = "S-2", FullName = "Denied", Username = "denied", PasswordHash = "h", AdviserId = 8 });
        db.LabSessions.Add(new LabSession { Id = 30, StudentId = 1, TeacherId = 7, PCName = "PC-01", StartTime = from.AddMinutes(5), EndTime = from.AddMinutes(15), Status = "Ended" });
        db.ActivityEvents.AddRange(
            new ActivityEvent { StudentId = "S-1", ConnectionId = "a", PcName = "PC-01", EventType = "Connected", Timestamp = from.AddMinutes(6) },
            new ActivityEvent { StudentId = "S-2", ConnectionId = "b", PcName = "PC-02", EventType = "Connected", Timestamp = from.AddMinutes(7) });
        db.MonitoringAlerts.AddRange(
            new MonitoringAlert { MonitoringAlertId = 40, StudentId = "S-1", PcName = "PC-01", Title = "Allowed alert", Message = "M", DedupeKey = "allowed", CreatedAt = from.AddMinutes(8) },
            new MonitoringAlert { MonitoringAlertId = 41, StudentId = "S-2", PcName = "PC-02", Title = "Denied alert", Message = "M", DedupeKey = "denied", CreatedAt = from.AddMinutes(9) });
        db.RemoteCommandLogs.AddRange(
            new RemoteCommandLog { TeacherId = 7, StudentId = "S-1", PcName = "PC-01", Command = "LockStudent", Timestamp = from.AddMinutes(10) },
            new RemoteCommandLog { TeacherId = 8, StudentId = "S-1", PcName = "PC-01", Command = "UnlockStudent", Timestamp = from.AddMinutes(11) },
            new RemoteCommandLog { TeacherId = 7, StudentId = "S-2", PcName = "PC-02", Command = "LockStudent", Timestamp = from.AddMinutes(12) });
        await db.SaveChangesAsync();
        var service = new AnalyticsService(db);

        var report = await service.GetUnifiedTimelineAsync(7, new UnifiedTimelineFilter(from, from.AddHours(1), PageSize: 2));

        // Global access: the timeline now spans every student across every source.
        // 1 session (S-1) + 2 activity (S-1, S-2) + 2 alerts (S-1, S-2) + 3 remote logs (S-1 x2, S-2 x1) = 8.
        Assert.NotNull(report);
        Assert.Equal(8, report!.Timeline.TotalCount);
        Assert.Equal(2, report.Timeline.Items.Count);
        var activityOnly = await service.GetUnifiedTimelineAsync(7,
            new UnifiedTimelineFilter(from, from.AddHours(1), Source: "Activity", EventType: "Connected"));
        Assert.Equal(2, activityOnly!.Timeline.Items.Count);

        // A previously "denied" student is now fully visible.
        var otherStudent = await service.GetUnifiedTimelineAsync(7,
            new UnifiedTimelineFilter(from, from.AddHours(1), StudentId: 2));
        Assert.NotNull(otherStudent);
        Assert.All(otherStudent!.Timeline.Items, item => Assert.Equal(2, item.StudentId));
    }

    [Fact]
    public async Task AlertLifecycle_GroupsOccurrencesAndAppliesBulkTransitionsToWholeGroup()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var first = new DateTime(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc);
        db.Students.AddRange(
            new Student { Id = 1, StudentNumber = "S-1", FullName = "Allowed", Username = "allowed", PasswordHash = "h", AdviserId = 7 },
            new Student { Id = 2, StudentNumber = "S-2", FullName = "Denied", Username = "denied", PasswordHash = "h", AdviserId = 8 });
        db.MonitoringAlerts.AddRange(
            new MonitoringAlert { MonitoringAlertId = 51, StudentId = "S-1", PcName = "PC-01", Title = "Restricted", Message = "First", DedupeKey = "website:blocked", OccurrenceCount = 2, CreatedAt = first },
            new MonitoringAlert { MonitoringAlertId = 52, StudentId = "S-1", PcName = "PC-01", Title = "Restricted", Message = "Latest", DedupeKey = "website:blocked", CreatedAt = first.AddMinutes(10) },
            new MonitoringAlert { MonitoringAlertId = 53, StudentId = "S-2", PcName = "PC-01", Title = "Restricted", Message = "Denied", DedupeKey = "website:blocked", CreatedAt = first.AddMinutes(20) });
        await db.SaveChangesAsync();
        var service = new AnalyticsService(db);

        // Global access: alerts for every student are visible, so S-1 and S-2 form two groups.
        var open = await service.GetAlertsAsync(7);
        Assert.Equal(2, open.Items.Count);
        var group = open.Items.Single(g => g.StudentId == "S-1");
        Assert.Equal(3, group.OccurrenceCount);
        Assert.Equal(first, group.FirstSeenAt);
        Assert.Equal(first.AddMinutes(10), group.LastSeenAt);
        Assert.Equal(52, group.MonitoringAlertId);

        // Acknowledging across both students now matches and changes both groups.
        var acknowledged = await service.AcknowledgeAlertsAsync(new[] { group.MonitoringAlertId, 53 }, 7);
        Assert.Equal(2, acknowledged.RequestedCount);
        Assert.Equal(2, acknowledged.MatchedGroupCount);
        Assert.Equal(2, acknowledged.ChangedGroupCount);
        Assert.All(await db.MonitoringAlerts.Where(a => a.StudentId == "S-1").ToListAsync(), alert =>
        {
            Assert.Equal(MonitoringAlertStatus.Acknowledged, alert.Status);
            Assert.Equal(7, alert.AcknowledgedByTeacherId);
            Assert.NotNull(alert.AcknowledgedAt);
        });
        Assert.Equal(MonitoringAlertStatus.Acknowledged, (await db.MonitoringAlerts.FindAsync(53))!.Status);

        var dismissed = await service.DismissAlertsAsync(new[] { 51 }, 7, "Handled offline");
        Assert.Equal(1, dismissed.ChangedGroupCount);
        Assert.All(await db.MonitoringAlerts.Where(a => a.StudentId == "S-1").ToListAsync(), alert =>
        {
            Assert.Equal(MonitoringAlertStatus.Dismissed, alert.Status);
            Assert.Equal("Handled offline", alert.DismissalReason);
            Assert.Null(alert.AcknowledgedAt);
        });

        var reopened = await service.ReopenAlertsAsync(new[] { 52 }, 7);
        Assert.Equal(1, reopened.ChangedGroupCount);
        Assert.All(await db.MonitoringAlerts.Where(a => a.StudentId == "S-1").ToListAsync(), alert =>
        {
            Assert.Equal(MonitoringAlertStatus.Open, alert.Status);
            Assert.Null(alert.DismissedAt);
            Assert.Null(alert.DismissalReason);
        });
    }

    [Fact]
    public async Task AlertLifecycleMigration_BackfillsExistingRowsOnSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260830060854_CompleteRemainingScope");
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO MonitoringAlerts
                (StudentId, PcName, Severity, Title, Message, IsAcknowledged, DedupeKey, CreatedAt)
            VALUES
                ('1', 'PC-01', 'Warning', 'Restricted', 'Blocked', 0, 'Website:Blocked', '2026-08-30 10:00:00');
            """);

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var alert = await db.MonitoringAlerts.SingleAsync();
        Assert.Equal("1|pc-01|website:blocked", alert.GroupKey);
        Assert.Equal(1, alert.OccurrenceCount);
        Assert.Equal(alert.CreatedAt, alert.FirstSeenAt);
        Assert.Equal(alert.CreatedAt, alert.LastSeenAt);
    }
}
