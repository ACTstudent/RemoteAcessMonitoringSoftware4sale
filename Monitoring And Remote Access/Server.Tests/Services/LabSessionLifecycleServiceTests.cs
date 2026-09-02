using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Server.Data;
using Server.Hubs;
using Server.Models;
using Server.Services;
using Shared.Contracts;

namespace Server.Tests.Services;

public class LabSessionLifecycleServiceTests
{
    [Fact]
    public void GetElapsedSeconds_SubtractsCompletedAndCurrentPauseTime()
    {
        var started = DateTime.UtcNow.AddMinutes(-10);
        var session = new LabSession
        {
            StartTime = started,
            Status = "Paused",
            PauseTime = started.AddMinutes(8),
            AccumulatedPauseSeconds = 120
        };

        Assert.InRange(LabSessionLifecycleService.GetElapsedSeconds(session, DateTime.UtcNow), 359, 361);
        Assert.Equal(started, session.StartTime);
    }

    [Fact]
    public async Task EndExpiredSessions_EndsSessionAndReleasesComputer()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var computer = new Computer { LaboratoryStation = "PC-1", Status = "In Use", AssignedTo = "1" };
        db.Computers.Add(computer);
        db.LabSessions.Add(new LabSession { StudentId = 1, Computer = computer, StartTime = DateTime.UtcNow.AddMinutes(-10), MaxDurationMinutes = 1, Status = "Running" });
        await db.SaveChangesAsync();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(c => c.Users(It.IsAny<IReadOnlyList<string>>())).Returns(Mock.Of<IClientProxy>());
        var hub = new Mock<IHubContext<RemoteMonitoringHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);

        var service = new LabSessionLifecycleService(db, hub.Object);
        Assert.True(LabSessionLifecycleService.GetElapsedSeconds(await db.LabSessions.SingleAsync(), DateTime.UtcNow) > 500);
        Assert.Equal(1, await service.EndExpiredSessionsAsync());
        Assert.Equal("Ended", (await db.LabSessions.SingleAsync()).Status);
        Assert.False((await db.LabSessions.SingleAsync()).IsActive);
        Assert.Equal("Assigned", (await db.Computers.SingleAsync()).Status);
        Assert.Equal("1", (await db.Computers.SingleAsync()).AssignedTo);
    }

    [Fact]
    public async Task EndExpiredSessions_IsIdempotent()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        db.LabSessions.Add(new LabSession { StudentId = 1, StartTime = DateTime.UtcNow.AddMinutes(-10), MaxDurationMinutes = 1 });
        await db.SaveChangesAsync();
        var hub = new Mock<IHubContext<RemoteMonitoringHub>>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(c => c.Users(It.IsAny<IReadOnlyList<string>>())).Returns(Mock.Of<IClientProxy>());
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        var service = new LabSessionLifecycleService(db, hub.Object);
        Assert.Equal(1, await service.EndExpiredSessionsAsync());
        Assert.Equal(0, await service.EndExpiredSessionsAsync());
    }

    [Fact]
    public async Task EnsureStudentSession_CreatesProfileAndConnectsClassTeacher()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var teacher = new Teacher { TeacherId = 8, Username = "teacher-8", PasswordHash = "hash", Status = "Active" };
        var classroom = new Class { ClassName = "Grade 6 Test", AcademicYear = "2026-2027", Teacher = teacher };
        var student = new Student { StudentNumber = "STU-1", Username = "student-1", PasswordHash = "hash", Status = "Active", Class = classroom, Adviser = teacher };
        db.AddRange(teacher, classroom, student);
        await db.SaveChangesAsync();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.User(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hub = new Mock<IHubContext<RemoteMonitoringHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);

        var session = await new LabSessionLifecycleService(db, hub.Object)
            .EnsureStudentSessionAsync(student.Id, "LAB2-PC26", "127.0.0.1");

        Assert.Equal(teacher.TeacherId, session.TeacherId);
        Assert.Equal("Running", session.Status);
        Assert.True(session.IsActive);
        Assert.Equal("LAB2-PC26", session.Computer!.LaboratoryStation);
        Assert.Equal(student.Id.ToString(), session.Computer.AssignedTo);
    }

    [Fact]
    public async Task CloseRemoteSessionsForRule_EndsAndNotifiesMatchingSupportSessions()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var student = new Student { StudentNumber = "STU-REMOTE", Username = "student-remote", PasswordHash = "hash", Status = "Active" };
        var rule = new SessionRule { Name = "Remote rule", MaxDurationMinutes = 60, AllowRemoteControl = true, IsActive = true };
        db.AddRange(student, rule);
        await db.SaveChangesAsync();
        db.LabSessions.Add(new LabSession { StudentId = student.Id, SessionRuleId = rule.SessionRuleId, StartTime = DateTime.UtcNow, Status = "Running", IsActive = true, PCName = "PC-REMOTE" });
        db.RemoteControlSessions.Add(new RemoteControlSession { TeacherId = 1, StudentId = student.StudentNumber, PcName = "PC-REMOTE", ConnectionId = "remote-connection", IsActive = true });
        await db.SaveChangesAsync();
        var client = new Mock<ISingleClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(value => value.Client("remote-connection")).Returns(client.Object);
        var hub = new Mock<IHubContext<RemoteMonitoringHub>>();
        hub.SetupGet(value => value.Clients).Returns(clients.Object);

        var closed = await new LabSessionLifecycleService(db, hub.Object)
            .CloseRemoteSessionsForRuleAsync(rule.SessionRuleId);

        Assert.Equal(1, closed);
        Assert.False((await db.RemoteControlSessions.SingleAsync()).IsActive);
        client.Verify(proxy => proxy.SendCoreAsync(HubEventNames.RemoteControlState,
            It.Is<object?[]>(arguments => HasInactiveRemoteControlState(arguments)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static bool HasInactiveRemoteControlState(object?[] arguments) =>
        arguments.Length == 1 && arguments[0] is RemoteControlStateMessage { IsActive: false };
}
