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
}
