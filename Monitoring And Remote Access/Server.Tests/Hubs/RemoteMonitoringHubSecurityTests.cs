using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Server.Data;
using Server.Hubs;
using Server.Services;
using Shared.Contracts;

namespace Server.Tests.Hubs;

public sealed class RemoteMonitoringHubSecurityTests
{
    [Fact]
    public async Task LockStudent_AllowsActiveTeacherWhoDoesNotOwnTarget()
    {
        await using var provider = CreateProvider();
        await SeedStudentAsync(provider, "student-1", classTeacherId: 2);
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        var clients = new Mock<IHubCallerClients>();
        var target = new Mock<ISingleClientProxy>();
        clients.Setup(c => c.Client("student-connection")).Returns(target.Object);
        var hub = CreateHub(provider, monitoring, "teacher-connection", "Teacher", "1", clients);

        await hub.LockStudent("student-connection");

        target.Verify(p => p.SendCoreAsync(HubEventNames.LockStudent, It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LockStudent_AllowsOwningTeacherAndSendsCommand()
    {
        await using var provider = CreateProvider();
        await SeedStudentAsync(provider, "student-1", classTeacherId: 1);
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        var clients = new Mock<IHubCallerClients>();
        var target = new Mock<ISingleClientProxy>();
        clients.Setup(c => c.Client("student-connection")).Returns(target.Object);
        var hub = CreateHub(provider, monitoring, "teacher-connection", "Teacher", "1", clients);

        await hub.LockStudent("student-connection");

        target.Verify(p => p.SendCoreAsync(HubEventNames.LockStudent, It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LockStudent_AllowsTeacherThroughClassStudentsMembership()
    {
        await using var provider = CreateProvider();
        var student = await SeedStudentAsync(provider, "student-join", classTeacherId: 2);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var teacherClass = new Server.Models.Class { ClassName = "Joined class", TeacherId = 1 };
            db.Classes.Add(teacherClass);
            await db.SaveChangesAsync();
            db.ClassStudents.Add(new Server.Models.ClassStudent { ClassId = teacherClass.ClassId, StudentId = student.Id });
            await db.SaveChangesAsync();
        }
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-join", "PC-01");
        var clients = new Mock<IHubCallerClients>();
        var target = new Mock<ISingleClientProxy>();
        clients.Setup(value => value.Client("student-connection")).Returns(target.Object);
        var hub = CreateHub(provider, monitoring, "teacher-connection", "Teacher", "1", clients);

        await hub.LockStudent("student-connection");

        target.Verify(proxy => proxy.SendCoreAsync(HubEventNames.LockStudent, It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing-connection")]
    public async Task LockStudent_RejectsInvalidTarget(string targetConnectionId)
    {
        await using var provider = CreateProvider();
        var hub = CreateHub(provider, new MonitoringService(), "teacher-connection", "Teacher", "1");

        await Assert.ThrowsAsync<HubException>(() => hub.LockStudent(targetConnectionId));
    }

    [Fact]
    public async Task SendScreenFrame_RejectsFrameAboveProtocolLimit()
    {
        await using var provider = CreateProvider();
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        var hub = CreateHub(provider, monitoring, "student-connection", "Student", "1", clientAgent: true);

        var error = await Assert.ThrowsAsync<HubException>(() => hub.SendScreenFrame(
            new ScreenFrameMessage("spoofed", "spoofed-pc", new string('x', 6 * 1024 * 1024 + 1), DateTime.UtcNow)));

        Assert.Contains("exceeds the maximum size", error.Message);
    }

    [Fact]
    public async Task ReportTelemetryBatch_CanonicalizesIdentityAndPrivateValues()
    {
        await using var provider = CreateProvider();
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        IReadOnlyList<TelemetryBatchItem>? recorded = null;
        var telemetry = new Mock<ITelemetryService>();
        telemetry.Setup(service => service.RecordBatchAsync(
                It.IsAny<IReadOnlyList<TelemetryBatchItem>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<TelemetryBatchItem>, CancellationToken>((items, _) => recorded = items)
            .Returns(Task.CompletedTask);
        var hub = CreateHub(provider, monitoring, "student-connection", "Student", "1",
            clientAgent: true, telemetryService: telemetry.Object);

        var timestamp = DateTime.UtcNow.AddMinutes(-3);
        var result = await hub.ReportTelemetryBatch(new TelemetryBatchMessage(new[]
        {
            TelemetryBatchItem.From(new ActiveAppMessage("spoofed", "spoofed", "spoofed",
                "chrome - Private title", timestamp)),
            TelemetryBatchItem.From(new WebsiteActivityMessage("spoofed", "spoofed", "spoofed",
                "https://user:secret@example.com/private", "Chrome", timestamp))
        }));

        Assert.Equal(2, result.ProcessedCount);
        Assert.NotNull(recorded);
        Assert.Equal("student-connection", recorded![0].ActiveApp!.ConnectionId);
        Assert.Equal("student-1", recorded[0].ActiveApp!.StudentId);
        Assert.Equal("PC-01", recorded[0].ActiveApp!.PcName);
        Assert.Equal("chrome", recorded[0].ActiveApp!.ApplicationName);
        Assert.Equal(timestamp, recorded[0].ActiveApp!.Timestamp);
        Assert.Equal("example.com", recorded[1].WebsiteActivity!.Domain);
        Assert.Equal("chrome", recorded[1].WebsiteActivity!.Browser);
        Assert.Single(monitoring.ActiveApps);
    }

    [Fact]
    public async Task ReportTelemetryBatch_StripsNonAllowlistedBrowserDetail()
    {
        await using var provider = CreateProvider();
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        IReadOnlyList<TelemetryBatchItem>? recorded = null;
        var telemetry = new Mock<ITelemetryService>();
        telemetry.Setup(service => service.RecordBatchAsync(
                It.IsAny<IReadOnlyList<TelemetryBatchItem>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<TelemetryBatchItem>, CancellationToken>((items, _) => recorded = items)
            .Returns(Task.CompletedTask);
        var hub = CreateHub(provider, monitoring, "student-connection", "Student", "1",
            clientAgent: true, telemetryService: telemetry.Object);

        var result = await hub.ReportTelemetryBatch(new TelemetryBatchMessage(new[]
        {
            TelemetryBatchItem.From(new BrowserMonitoringStatusMessage("spoofed", "spoofed", "spoofed",
                "chrome", BrowserMonitoringMode.ManagedProtocol, DateTime.UtcNow, "https://private.example.com/secret"))
        }));

        Assert.Equal(1, result.ProcessedCount);
        Assert.NotNull(recorded);
        Assert.Null(recorded![0].BrowserMonitoringStatus!.Detail);
    }

    [Fact]
    public async Task FetchRestrictions_ReturnsOnlyGlobalAndOwningTeacherRules()
    {
        await using var provider = CreateProvider();
        var student = await SeedStudentAsync(provider, "student-1", classTeacherId: 1);
        await SeedLabSessionAsync(provider, student.Id, allowRemoteControl: false);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.RestrictionRules.AddRange(
                new Server.Models.RestrictionRule { RuleType = "Website", Target = "global.test", Mode = "Block", IsGlobal = true, IsActive = true },
                new Server.Models.RestrictionRule { RuleType = "Website", Target = "teacher-one.test", Mode = "Block", TeacherId = 1, IsGlobal = false, IsActive = true },
                new Server.Models.RestrictionRule { RuleType = "Website", Target = "teacher-two.test", Mode = "Block", TeacherId = 2, IsGlobal = false, IsActive = true });
            await db.SaveChangesAsync();
        }

        IReadOnlyList<RestrictionRuleMessage>? delivered = null;
        var clients = new Mock<IHubCallerClients>();
        var target = new Mock<ISingleClientProxy>();
        target.Setup(proxy => proxy.SendCoreAsync(HubEventNames.RestrictionsReceived, It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((_, args, _) =>
                delivered = Assert.IsAssignableFrom<IReadOnlyList<RestrictionRuleMessage>>(args[0]))
            .Returns(Task.CompletedTask);
        clients.Setup(value => value.Client("student-connection")).Returns(target.Object);
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        var hub = CreateHub(provider, monitoring, "student-connection", "Student", "1", clients, clientAgent: true);

        await hub.FetchRestrictions();

        Assert.NotNull(delivered);
        Assert.Contains(delivered!, rule => rule.Target == "global.test");
        Assert.Contains(delivered!, rule => rule.Target == "teacher-one.test");
        Assert.DoesNotContain(delivered!, rule => rule.Target == "teacher-two.test");
    }

    [Fact]
    public async Task ForceLogout_EndsLabSessionBeforeDisconnectingStudent()
    {
        await using var provider = CreateProvider();
        var student = await SeedStudentAsync(provider, "student-1", classTeacherId: 1);
        await SeedLabSessionAsync(provider, student.Id, allowRemoteControl: false);
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        var clients = new Mock<IHubCallerClients>();
        clients.Setup(value => value.Client("student-connection")).Returns(Mock.Of<ISingleClientProxy>());
        clients.Setup(value => value.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hub = CreateHub(provider, monitoring, "teacher-connection", "Teacher", "1", clients);

        await hub.ForceLogout("student-connection");

        await using var scope = provider.CreateAsyncScope();
        var session = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LabSessions.SingleAsync();
        Assert.False(session.IsActive);
        Assert.Equal("Ended", session.Status);
    }

    [Fact]
    public async Task GlobalSessionControl_AllowsActiveTeacher()
    {
        await using var provider = CreateProvider();
        var student = await SeedStudentAsync(provider, "student-global", classTeacherId: 2);
        await SeedLabSessionAsync(provider, student.Id, allowRemoteControl: false);
        var hub = CreateHub(provider, new MonitoringService(), "teacher-connection", "Teacher", "1");

        await hub.GlobalEndSession();

        await using var scope = provider.CreateAsyncScope();
        Assert.False((await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LabSessions.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task GlobalEndSession_EndsPersistedLabAndRemoteSessions()
    {
        await using var provider = CreateProvider();
        var student = await SeedStudentAsync(provider, "student-1", classTeacherId: 1);
        await SeedLabSessionAsync(provider, student.Id, allowRemoteControl: true);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.RemoteControlSessions.Add(new Server.Models.RemoteControlSession
            {
                TeacherId = 1,
                StudentId = "student-1",
                PcName = "PC-01",
                ConnectionId = "student-connection"
            });
            await db.SaveChangesAsync();
        }
        var hub = CreateHub(provider, new MonitoringService(), "admin-connection", "Admin", "1");

        await hub.GlobalEndSession();

        await using var verificationScope = provider.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False((await verification.LabSessions.SingleAsync()).IsActive);
        Assert.False((await verification.RemoteControlSessions.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task ReportTelemetryBatch_PersistsQueuedInfractionAlert()
    {
        await using var provider = CreateProvider();
        var student = await SeedStudentAsync(provider, "student-1", classTeacherId: 1);
        await SeedLabSessionAsync(provider, student.Id, allowRemoteControl: false);
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        var telemetry = new Mock<ITelemetryService>();
        telemetry.Setup(service => service.RecordBatchAsync(It.IsAny<IReadOnlyList<TelemetryBatchItem>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var hub = CreateHub(provider, monitoring, "student-connection", "Student", student.Id.ToString(),
            clientAgent: true, telemetryService: telemetry.Object);

        var result = await hub.ReportTelemetryBatch(new TelemetryBatchMessage(new[]
        {
            TelemetryBatchItem.From(new InfractionMessage("spoofed", "spoofed", "spoofed", "game.exe", "Application", DateTime.UtcNow))
        }));

        Assert.Equal(1, result.ProcessedCount);
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal("Application: game.exe", (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .MonitoringAlerts.SingleAsync()).Message);
    }

    [Fact]
    public async Task StudentDisconnect_PreservesLabSessionForAutomaticReconnect()
    {
        await using var provider = CreateProvider();
        var student = await SeedStudentAsync(provider, "student-1", classTeacherId: 1);
        await SeedLabSessionAsync(provider, student.Id, allowRemoteControl: false);
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        var hub = CreateHub(provider, monitoring, "student-connection", "Student", student.Id.ToString(), clientAgent: true);

        await hub.OnDisconnectedAsync(new IOException("temporary network interruption"));

        await using var scope = provider.CreateAsyncScope();
        var session = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LabSessions.SingleAsync();
        Assert.True(session.IsActive);
        Assert.Equal("Running", session.Status);
    }

    [Fact]
    public async Task Disconnect_CleansMonitoringStateAndRemoteSession()
    {
        await using var provider = CreateProvider();
        var student = await SeedStudentAsync(provider, "student-1", classTeacherId: 1);
        await SeedLabSessionAsync(provider, student.Id, allowRemoteControl: true);
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        monitoring.ReportIdleStatus(new IdleStatusMessage("student-connection", "student-1", "PC-01", true, DateTime.UtcNow));
        monitoring.ReportActiveApp(new ActiveAppMessage("student-connection", "student-1", "PC-01", "app.exe", DateTime.UtcNow));
        var hub = CreateHub(provider, monitoring, "teacher-connection", "Teacher", "1");
        await hub.StartRemoteControl("student-connection");

        await hub.OnDisconnectedAsync(null);

        Assert.Single(monitoring.ActiveStudents);
        Assert.Single(monitoring.IdleStatus);
        Assert.Single(monitoring.ActiveApps);
        await using var scope = provider.CreateAsyncScope();
        var session = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().RemoteControlSessions.SingleAsync();
        Assert.False(session.IsActive);
        Assert.NotNull(session.EndedAt);
    }

    [Fact]
    public async Task TeacherDisconnect_NotifiesStudentThatRemoteControlStopped()
    {
        await using var provider = CreateProvider();
        var student = await SeedStudentAsync(provider, "student-1", classTeacherId: 1);
        await SeedLabSessionAsync(provider, student.Id, allowRemoteControl: true);
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        var clients = new Mock<IHubCallerClients>();
        var studentClient = new Mock<ISingleClientProxy>();
        clients.Setup(value => value.Client("student-connection")).Returns(studentClient.Object);
        clients.Setup(value => value.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(value => value.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(Mock.Of<IClientProxy>());
        var hub = CreateHub(provider, monitoring, "teacher-disconnect", "Teacher", "1", clients);
        await hub.StartRemoteControl("student-connection");
        studentClient.Invocations.Clear();

        await hub.OnDisconnectedAsync(null);

        studentClient.Verify(proxy => proxy.SendCoreAsync(HubEventNames.RemoteControlState,
            It.Is<object?[]>(args => HasInactiveRemoteControlState(args)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static bool HasInactiveRemoteControlState(object?[] args) =>
        args.Length == 1 && args[0] is RemoteControlStateMessage { IsActive: false };

    [Fact]
    public async Task StartRemoteControl_RejectsRuleThatDisallowsRemoteControl()
    {
        await using var provider = CreateProvider();
        var student = await SeedStudentAsync(provider, "student-1", classTeacherId: 1);
        await SeedLabSessionAsync(provider, student.Id, allowRemoteControl: false);
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        var hub = CreateHub(provider, monitoring, "teacher-connection", "Teacher", "1");

        var error = await Assert.ThrowsAsync<HubException>(() => hub.StartRemoteControl("student-connection"));

        Assert.Equal("Remote control is disabled by the active session rule.", error.Message);
    }

    [Fact]
    public async Task StartRemoteControl_AllowsSessionOwnedByAnotherTeacher()
    {
        await using var provider = CreateProvider();
        var student = await SeedStudentAsync(provider, "student-1", classTeacherId: 1);
        await SeedLabSessionAsync(provider, student.Id, allowRemoteControl: true);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.LabSessions.SingleAsync()).TeacherId = 2;
            await db.SaveChangesAsync();
        }
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        var hub = CreateHub(provider, monitoring, "teacher-connection", "Teacher", "1");

        var result = await hub.StartRemoteControl("student-connection");

        Assert.True(result.Succeeded);
        await using var verificationScope = provider.CreateAsyncScope();
        Assert.Equal(1, (await verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .RemoteControlSessions.SingleAsync()).TeacherId);
    }

    [Fact]
    public async Task LockStudent_RejectsTeacherDeactivatedAfterConnectionWasEstablished()
    {
        await using var provider = CreateProvider();
        await SeedStudentAsync(provider, "student-inactive", classTeacherId: 2);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Teachers.SingleAsync(teacher => teacher.TeacherId == 1)).Status = "Inactive";
            await db.SaveChangesAsync();
        }
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-inactive-connection", "student-inactive", "PC-09");
        var hub = CreateHub(provider, monitoring, "existing-teacher-connection", "Teacher", "1");

        var error = await Assert.ThrowsAsync<HubException>(() => hub.LockStudent("student-inactive-connection"));

        Assert.Equal("The teacher account is inactive.", error.Message);
    }

    [Fact]
    public async Task SendRemoteInput_RequiresRemoteSupportStartedByThisConnection()
    {
        await using var provider = CreateProvider();
        var student = await SeedStudentAsync(provider, "student-bound", classTeacherId: 2);
        await SeedLabSessionAsync(provider, student.Id, allowRemoteControl: true);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.RemoteControlSessions.Add(new Server.Models.RemoteControlSession
            {
                TeacherId = 1,
                StudentId = "student-bound",
                PcName = "PC-01",
                ConnectionId = "student-bound-connection"
            });
            await db.SaveChangesAsync();
        }
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-bound-connection", "student-bound", "PC-01");
        var hub = CreateHub(provider, monitoring, $"unbound-{Guid.NewGuid()}", "Teacher", "1");

        var error = await Assert.ThrowsAsync<HubException>(() => hub.SendRemoteInput("student-bound-connection",
            new RemoteInputMessage("mousedown", 1, 1, 0, false)));

        Assert.Equal("Start an authorized remote-support session first.", error.Message);
    }

    [Fact]
    public async Task SendScreenFrame_PublishesToGlobalTeacherViewerGroup()
    {
        await using var provider = CreateProvider();
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-frame-connection", "student-frame", "PC-03");
        var clients = new Mock<IHubCallerClients>();
        var teachers = new Mock<IClientProxy>();
        clients.Setup(c => c.Group(HubEventNames.TeachersGroup)).Returns(teachers.Object);
        var hub = CreateHub(provider, monitoring, "student-frame-connection", "Student", "10", clients, clientAgent: true);

        await hub.SendScreenFrame(new ScreenFrameMessage("spoofed", "spoofed", "frame", DateTime.UtcNow));

        teachers.Verify(proxy => proxy.SendCoreAsync(HubEventNames.ReceiveScreenFrame,
            It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendRemoteInput_RejectsWithoutActiveLabSession()
    {
        await using var provider = CreateProvider();
        await SeedStudentAsync(provider, "student-1", classTeacherId: 1);
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        var hub = CreateHub(provider, monitoring, "teacher-connection", "Teacher", "1");

        var error = await Assert.ThrowsAsync<HubException>(() => hub.SendRemoteInput("student-connection",
            new RemoteInputMessage("mousedown", 1, 1, 0, false)));

        Assert.Equal("The workstation has no active lab session.", error.Message);
    }

    [Fact]
    public async Task StartRemoteControl_ExpiresLabSession()
    {
        await using var provider = CreateProvider();
        var student = await SeedStudentAsync(provider, "student-1", classTeacherId: 1);
        await SeedLabSessionAsync(provider, student.Id, allowRemoteControl: true,
            startedAt: DateTime.UtcNow.AddMinutes(-10), duration: 1);
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        var hub = CreateHub(provider, monitoring, "teacher-connection", "Teacher", "1");

        var error = await Assert.ThrowsAsync<HubException>(() => hub.StartRemoteControl("student-connection"));

        Assert.Equal("The lab session has expired.", error.Message);
        await using var scope = provider.CreateAsyncScope();
        var session = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LabSessions.SingleAsync();
        Assert.False(session.IsActive);
        Assert.Equal("Ended", session.Status);
    }

    private static RemoteMonitoringHub CreateHub(IServiceProvider provider, IMonitoringService monitoring,
        string connectionId, string role, string userId, Mock<IHubCallerClients>? clients = null,
        bool clientAgent = false, ITelemetryService? telemetryService = null)
    {
        var context = new Mock<HubCallerContext>();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role)
        };
        if (clientAgent)
            claims.Add(new Claim(AuthPrincipalFactory.ClientAgentClaim, bool.TrueString));
        context.SetupGet(c => c.ConnectionId).Returns(connectionId);
        context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));

        var ownsClients = clients is null;
        clients ??= new Mock<IHubCallerClients>();
        var proxy = new Mock<ISingleClientProxy>();
        var groupProxy = new Mock<IClientProxy>();
        if (ownsClients)
        {
            clients.Setup(c => c.Client(It.IsAny<string>())).Returns(proxy.Object);
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(groupProxy.Object);
            clients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(groupProxy.Object);
        }
        var hub = new RemoteMonitoringHub(monitoring, telemetryService ?? Mock.Of<ITelemetryService>(),
            new SessionManagerService(Mock.Of<IHubContext<RemoteMonitoringHub>>()),
            provider.GetRequiredService<IServiceScopeFactory>())
        {
            Context = context.Object,
            Clients = clients.Object
        };
        return hub;
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));
        var clients = new Mock<IHubClients>();
        clients.Setup(value => value.User(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(value => value.Users(It.IsAny<IReadOnlyList<string>>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(value => value.Client(It.IsAny<string>())).Returns(Mock.Of<ISingleClientProxy>());
        var hubContext = new Mock<IHubContext<RemoteMonitoringHub>>();
        hubContext.SetupGet(value => value.Clients).Returns(clients.Object);
        services.AddSingleton(hubContext.Object);
        services.AddScoped<LabSessionLifecycleService>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Teachers.Add(new Server.Models.Teacher
        {
            TeacherId = 1,
            FirstName = "Active",
            LastName = "Teacher",
            Username = "teacher-1",
            PasswordHash = "hash",
            Status = "Active"
        });
        db.SaveChanges();
        return provider;
    }

    private static async Task<Server.Models.Student> SeedStudentAsync(IServiceProvider provider, string number, int classTeacherId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cls = new Server.Models.Class { ClassName = "Test class", TeacherId = classTeacherId };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        var student = new Server.Models.Student { StudentNumber = number, FullName = "Test Student", Username = number, ClassId = cls.ClassId, AdviserId = classTeacherId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return student;
    }

    private static async Task SeedLabSessionAsync(IServiceProvider provider, int studentId, bool allowRemoteControl,
        DateTime? startedAt = null, int duration = 60)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rule = new Server.Models.SessionRule { Name = "Test rule", MaxDurationMinutes = duration,
            AllowRemoteControl = allowRemoteControl, IsActive = true };
        db.SessionRules.Add(rule);
        db.LabSessions.Add(new Server.Models.LabSession { StudentId = studentId, TeacherId = 1,
            SessionRule = rule, StartTime = startedAt ?? DateTime.UtcNow, Status = "Running",
            IsActive = true, MaxDurationMinutes = duration, PCName = "PC-01" });
        await db.SaveChangesAsync();
    }
}
