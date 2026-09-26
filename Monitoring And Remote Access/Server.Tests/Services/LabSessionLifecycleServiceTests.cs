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

    private static Mock<IHubContext<RemoteMonitoringHub>> QuietHub()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.User(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(c => c.Users(It.IsAny<IReadOnlyList<string>>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hub = new Mock<IHubContext<RemoteMonitoringHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        return hub;
    }

    // The lab runs from the moment it is started, before anyone signs in, and
    // follows the lab-wide Pause, Resume and End. Without this the page showed
    // no running session at all after "Start for everyone" in an empty room.
    [Fact]
    public async Task LabFollowsStartPauseResumeAndEnd_EvenWithNoStudentsSignedIn()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var rule = new SessionRule { Name = "Lab", MaxDurationMinutes = 60, IsActive = true };
        db.SessionRules.Add(rule);
        await db.SaveChangesAsync();
        var hub = QuietHub();
        var lab = new SessionManagerService(hub.Object);
        var service = new LabSessionLifecycleService(db, hub.Object, lab: lab);

        Assert.Equal(0, await service.StartAllSessionsAsync(rule));
        Assert.Equal("Running", lab.Snapshot().Status);
        Assert.Equal(rule.SessionRuleId, lab.LabRuleId);

        await service.PauseAllSessionsAsync();
        Assert.Equal("Paused", lab.Snapshot().Status);

        await service.ResumeAllSessionsAsync();
        Assert.Equal("Running", lab.Snapshot().Status);

        await service.EndAllSessionsAsync();
        Assert.Equal("Ended", lab.Snapshot().Status);
        Assert.False(lab.IsLabOpen);
    }

    // The teacher who starts the lab runs it: a session with no teacher of its
    // own is given theirs, a session that has one keeps it, and every connected
    // student is told to fetch its rules again, since that teacher's rules now
    // reach them.
    [Fact]
    public async Task StartAllSessions_HandsTeacherlessSessionsToTheTeacherRunningTheLab()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        db.LabSessions.AddRange(
            new LabSession { StudentId = 1, TeacherId = null, PCName = "PC-1", StartTime = DateTime.UtcNow, Status = "Running", IsActive = true },
            new LabSession { StudentId = 2, TeacherId = 9, PCName = "PC-2", StartTime = DateTime.UtcNow, Status = "Running", IsActive = true });
        await db.SaveChangesAsync();
        var students = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.User(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(c => c.Group(HubEventNames.StudentsGroup)).Returns(students.Object);
        var hub = new Mock<IHubContext<RemoteMonitoringHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        var lab = new SessionManagerService(hub.Object);

        await new LabSessionLifecycleService(db, hub.Object, lab: lab).StartAllSessionsAsync(null, teacherId: 4);

        Assert.Equal(4, (await db.LabSessions.SingleAsync(s => s.StudentId == 1)).TeacherId);
        Assert.Equal(9, (await db.LabSessions.SingleAsync(s => s.StudentId == 2)).TeacherId);
        Assert.Equal(4, lab.LabTeacherId);
        students.Verify(p => p.SendCoreAsync(HubEventNames.PolicyRefreshRequired, It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NewcomerSigningIntoARunningLab_BelongsToTheTeacherRunningIt()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var newcomer = new Student { StudentNumber = "STU-NEW", Username = "newcomer", PasswordHash = "hash", Status = "Active" };
        db.Add(newcomer);
        await db.SaveChangesAsync();
        var hub = QuietHub();
        var lab = new SessionManagerService(hub.Object);
        var service = new LabSessionLifecycleService(db, hub.Object, lab: lab);
        await service.StartAllSessionsAsync(null, teacherId: 4);

        var session = await service.EnsureStudentSessionAsync(newcomer.Id, "LAB5-PC33", "127.0.0.1");

        Assert.Equal(4, session.TeacherId);
    }

    [Fact]
    public async Task ResumeAll_DoesNotOpenALabThatWasNeverStarted()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var hub = QuietHub();
        var lab = new SessionManagerService(hub.Object);

        await new LabSessionLifecycleService(db, hub.Object, lab: lab).ResumeAllSessionsAsync();

        Assert.Equal("None", lab.Snapshot().Status);
    }

    // Starting the lab is for everyone at once: every signed-in student restarts
    // under the chosen rule, paused or not, and nobody is ended - ending would
    // close their client and sign the whole room out.
    [Fact]
    public async Task StartAllSessions_RestartsEverySignedInSessionUnderTheChosenRule()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var oldRule = new SessionRule { Name = "Old", MaxDurationMinutes = 45, IsDefault = true, IsActive = true };
        var labRule = new SessionRule { Name = "Lab", MaxDurationMinutes = 90, IsActive = true };
        db.SessionRules.AddRange(oldRule, labRule);
        await db.SaveChangesAsync();
        var anHourAgo = DateTime.UtcNow.AddHours(-1);
        db.LabSessions.AddRange(
            new LabSession { StudentId = 1, PCName = "PC-1", SessionRuleId = oldRule.SessionRuleId, MaxDurationMinutes = 45, StartTime = anHourAgo, Status = "Running", IsActive = true, AccumulatedPauseSeconds = 30 },
            new LabSession { StudentId = 2, PCName = "PC-2", SessionRuleId = oldRule.SessionRuleId, MaxDurationMinutes = 45, StartTime = anHourAgo, Status = "Paused", PauseTime = anHourAgo.AddMinutes(5), IsActive = true },
            new LabSession { StudentId = 3, PCName = "PC-3", StartTime = anHourAgo, EndTime = anHourAgo.AddMinutes(10), Status = "Ended", IsActive = false });
        await db.SaveChangesAsync();

        var started = await new LabSessionLifecycleService(db, QuietHub().Object).StartAllSessionsAsync(labRule);

        Assert.Equal(2, started);
        var live = await db.LabSessions.Where(s => s.IsActive).ToListAsync();
        Assert.Equal(2, live.Count);
        Assert.All(live, session =>
        {
            Assert.Equal("Running", session.Status);
            Assert.Equal(labRule.SessionRuleId, session.SessionRuleId);
            Assert.Equal(90, session.MaxDurationMinutes);
            Assert.Null(session.PauseTime);
            Assert.Equal(0, session.AccumulatedPauseSeconds);
            Assert.InRange(LabSessionLifecycleService.GetElapsedSeconds(session, DateTime.UtcNow), 0, 5);
        });
        var ended = await db.LabSessions.SingleAsync(s => s.StudentId == 3);
        Assert.Equal("Ended", ended.Status);
        Assert.Null(ended.SessionRuleId);
    }

    // "All students" includes the ones not signed in yet: whoever signs in after
    // the start, on any computer, joins under the teacher's rule. Once the lab is
    // ended nobody can start a new session until it is started again.
    [Fact]
    public async Task StartAllSessions_LaterSignInsJoinUnderTheLabRuleUntilTheLabEnds()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var defaultRule = new SessionRule { Name = "Default", MaxDurationMinutes = 60, IsDefault = true, IsActive = true };
        var labRule = new SessionRule { Name = "Lab", MaxDurationMinutes = 90, IsActive = true };
        var early = new Student { StudentNumber = "STU-EARLY", Username = "early", PasswordHash = "hash", Status = "Active" };
        var late = new Student { StudentNumber = "STU-LATE", Username = "late", PasswordHash = "hash", Status = "Active" };
        db.AddRange(defaultRule, labRule, early, late);
        await db.SaveChangesAsync();
        var hub = QuietHub();
        var lab = new SessionManagerService(hub.Object);
        var service = new LabSessionLifecycleService(db, hub.Object, lab: lab);

        await service.StartAllSessionsAsync(labRule);
        var joined = await service.EnsureStudentSessionAsync(early.Id, "LAB-PC-07", "127.0.0.1");
        Assert.Equal(labRule.SessionRuleId, joined.SessionRuleId);
        Assert.Equal(90, joined.MaxDurationMinutes);

        await service.EndAllSessionsAsync();
        Assert.Null(lab.LabRuleId);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnsureStudentSessionAsync(late.Id, "LAB-PC-08", "127.0.0.1"));
        Assert.Equal(WorkstationRegistrationService.NoLabMessage, refused.Message);

        // Started again, under the default rule this time.
        await service.StartAllSessionsAsync(null);
        var afterwards = await service.EnsureStudentSessionAsync(late.Id, "LAB-PC-08", "127.0.0.1");
        Assert.Equal(defaultRule.SessionRuleId, afterwards.SessionRuleId);
    }

    // A student cannot sign in before the teacher has started the lab, and a
    // refused sign-in leaves the workstation record untouched.
    [Fact]
    public async Task SignIn_IsRefusedWhileNoLabIsRunning()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var student = new Student { StudentNumber = "STU-WAIT", Username = "waiting", PasswordHash = "hash", Status = "Active" };
        db.Add(student);
        await db.SaveChangesAsync();
        var hub = QuietHub();
        var lab = new SessionManagerService(hub.Object);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LabSessionLifecycleService(db, hub.Object, lab: lab).EnsureStudentSessionAsync(student.Id, "LAB-PC-01", "127.0.0.1"));

        Assert.Equal(WorkstationRegistrationService.NoLabMessage, refused.Message);
        Assert.Empty(await db.LabSessions.ToListAsync());
        Assert.Empty(await db.Computers.ToListAsync());
    }

    // A student already in a session is not thrown out by the check: their
    // client reconnecting after a network blip goes back into the same session.
    [Fact]
    public async Task SignIn_ReconnectsToAnExistingSessionEvenWithNoLabRunning()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var student = new Student { StudentNumber = "STU-BACK", Username = "back", PasswordHash = "hash", Status = "Active" };
        db.Add(student);
        await db.SaveChangesAsync();
        db.LabSessions.Add(new LabSession { StudentId = student.Id, PCName = "LAB-PC-02", StartTime = DateTime.UtcNow, Status = "Running", IsActive = true });
        await db.SaveChangesAsync();
        var hub = QuietHub();

        var session = await new LabSessionLifecycleService(db, hub.Object, lab: new SessionManagerService(hub.Object))
            .EnsureStudentSessionAsync(student.Id, "LAB-PC-02", "127.0.0.1");

        Assert.True(session.IsActive);
        Assert.Single(await db.LabSessions.ToListAsync());
    }

    private static bool HasInactiveRemoteControlState(object?[] arguments) =>
        arguments.Length == 1 && arguments[0] is RemoteControlStateMessage { IsActive: false };

    // SQLite hands back DateTimes with Kind=Unspecified. Treating those as local
    // time shifted a session's start by the machine's UTC offset, so a session
    // seconds old measured as hours old and expired immediately. In-memory tests
    // never caught it because they preserve Kind=Utc.
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void GetElapsedSeconds_TreatsStoredTimestampsAsUtcRegardlessOfKind(DateTimeKind kind)
    {
        var startedUtc = DateTime.UtcNow.AddMinutes(-5);
        var start = kind switch
        {
            DateTimeKind.Local => startedUtc.ToLocalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(startedUtc, DateTimeKind.Unspecified),
            _ => startedUtc
        };

        var session = new LabSession { StartTime = start, Status = "Running", PCName = "PC-1" };
        var elapsed = LabSessionLifecycleService.GetElapsedSeconds(session, DateTime.UtcNow);

        // Five minutes, whatever the machine's offset happens to be.
        Assert.InRange(elapsed, 295, 305);
    }

    [Fact]
    public void GetElapsedSeconds_FreshSessionIsNotTreatedAsExpired()
    {
        var session = new LabSession
        {
            // As read back from SQLite: correct UTC value, but Kind is lost.
            StartTime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            Status = "Running",
            PCName = "PC-1",
            MaxDurationMinutes = 60
        };

        var elapsed = LabSessionLifecycleService.GetElapsedSeconds(session, DateTime.UtcNow);

        Assert.InRange(elapsed, 0, 5);
        Assert.False(elapsed >= session.MaxDurationMinutes!.Value * 60);
    }
}
