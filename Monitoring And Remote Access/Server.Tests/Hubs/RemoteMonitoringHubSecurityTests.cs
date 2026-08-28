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
    public async Task LockStudent_RejectsTeacherWhoDoesNotOwnTarget()
    {
        await using var provider = CreateProvider();
        await SeedStudentAsync(provider, "student-1", classTeacherId: 2);
        var monitoring = new MonitoringService();
        monitoring.RegisterStudent("student-connection", "student-1", "PC-01");
        var hub = CreateHub(provider, monitoring, "teacher-connection", "Teacher", "1");

        var error = await Assert.ThrowsAsync<HubException>(() => hub.LockStudent("student-connection"));

        Assert.Equal("You are not authorized to control this workstation.", error.Message);
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
    public async Task Disconnect_CleansMonitoringStateAndRemoteSession()
    {
        await using var provider = CreateProvider();
        await SeedStudentAsync(provider, "student-1", classTeacherId: 1);
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

    private static RemoteMonitoringHub CreateHub(IServiceProvider provider, IMonitoringService monitoring,
        string connectionId, string role, string userId, Mock<IHubCallerClients>? clients = null, bool clientAgent = false)
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
            clients.Setup(c => c.Client(It.IsAny<string>())).Returns(proxy.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(groupProxy.Object);
        var hub = new RemoteMonitoringHub(monitoring, Mock.Of<ITelemetryService>(),
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
        return services.BuildServiceProvider();
    }

    private static async Task SeedStudentAsync(IServiceProvider provider, string number, int classTeacherId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cls = new Server.Models.Class { ClassName = "Test class", TeacherId = classTeacherId };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        db.Students.Add(new Server.Models.Student { StudentNumber = number, FullName = "Test Student", Username = number, ClassId = cls.ClassId, AdviserId = classTeacherId });
        await db.SaveChangesAsync();
    }
}
