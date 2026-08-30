using Microsoft.EntityFrameworkCore;
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
        db.IdleIntervals.Add(new IdleInterval { StudentId = "4", ConnectionId = "c", PcName = "PC-1", StartedAt = from.AddMinutes(5), EndedAt = from.AddMinutes(10) });
        db.ActivityEvents.Add(new ActivityEvent { StudentId = "4", ConnectionId = "c", PcName = "PC-1", EventType = "ApplicationUsed", ApplicationName = "editor", Timestamp = from.AddMinutes(1) });
        await db.SaveChangesAsync();

        var report = await new AnalyticsService(db).GetStudentReportAsync(4, 7, from, from.AddMinutes(20));

        Assert.NotNull(report);
        Assert.Equal(15, report!.Durations.ActiveMinutes, 1);
        Assert.Equal(5, report.Durations.IdleMinutes, 1);
        Assert.Equal(19, report.Durations.ApplicationMinutes, 1);
    }

    [Fact]
    public async Task AlertAcknowledgement_IsLimitedToTeacherRoster()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        db.Students.Add(new Student { Id = 1, StudentNumber = "S-1", FullName = "S", Username = "s", PasswordHash = "h", AdviserId = 1 });
        db.Students.Add(new Student { Id = 2, StudentNumber = "S-2", FullName = "S", Username = "s2", PasswordHash = "h", AdviserId = 2 });
        db.MonitoringAlerts.AddRange(new MonitoringAlert { MonitoringAlertId = 10, StudentId = "1", PcName = "PC", Title = "A", Message = "M" }, new MonitoringAlert { MonitoringAlertId = 11, StudentId = "2", PcName = "PC", Title = "B", Message = "M" });
        await db.SaveChangesAsync();
        var service = new AnalyticsService(db);

        Assert.True(await service.SetAlertAcknowledgedAsync(10, 1, true));
        Assert.False(await service.SetAlertAcknowledgedAsync(11, 1, true));
        Assert.True((await db.MonitoringAlerts.FindAsync(10))!.IsAcknowledged);
    }

    [Fact]
    public async Task AlertExport_OnlyReturnsAuthorizedStudents()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        db.Students.AddRange(
            new Student { Id = 1, StudentNumber = "S-1", FullName = "S", Username = "s", PasswordHash = "h", AdviserId = 1 },
            new Student { Id = 2, StudentNumber = "S-2", FullName = "S2", Username = "s2", PasswordHash = "h", AdviserId = 2 });
        db.MonitoringAlerts.AddRange(new MonitoringAlert { StudentId = "1", PcName = "PC", Title = "A", Message = "M" }, new MonitoringAlert { StudentId = "2", PcName = "PC", Title = "B", Message = "M" });
        await db.SaveChangesAsync();
        var alerts = await new AnalyticsService(db).GetAlertExportAsync(1);
        Assert.Single(alerts);
        Assert.Equal("1", alerts[0].StudentId);
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
            new ActivityEvent { StudentId = "9", PcName = "PC-9", ConnectionId = "c", EventType = "Connected", Timestamp = now.AddMinutes(-2) },
            new ActivityEvent { StudentId = "9", PcName = "PC-9", ConnectionId = "c", EventType = "Disconnected", Timestamp = now.AddMinutes(-1) });
        await db.SaveChangesAsync();
        var result = await new AnalyticsService(db).GetActivityTimelineAsync(9, 3, now.AddHours(-1), now.AddMinutes(1), eventType: "Disconnected");
        Assert.Single(result.Items);
        Assert.Equal("Disconnected", result.Items[0].EventType);
    }
}
