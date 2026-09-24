using Microsoft.AspNetCore.SignalR;
using Moq;
using Server.Services;

namespace Server.Tests.Services;

public class SessionManagerServiceTests
{
    private SessionManagerService CreateService()
    {
        var proxy = Mock.Of<IClientProxy>();
        var groupManager = Mock.Of<IGroupManager>();

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(proxy);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy);

        var hubContext = new Mock<IHubContext<Server.Hubs.RemoteMonitoringHub>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        return new SessionManagerService(hubContext.Object);
    }

    [Fact]
    public void Snapshot_InitialState_ReturnsNone()
    {
        var service = CreateService();
        Assert.Equal("None", service.Snapshot().Status);
    }

    [Fact]
    public void StartSession_SetsRunning()
    {
        var service = CreateService();
        service.StartSession();
        Assert.Equal("Running", service.Snapshot().Status);
    }

    [Fact]
    public void StartSession_StartedAtIsSet()
    {
        var service = CreateService();
        service.StartSession();
        Assert.NotNull(service.Snapshot().StartedAt);
    }

    [Fact]
    public void PauseSession_FromRunning_Works()
    {
        var service = CreateService();
        service.StartSession();
        Thread.Sleep(200);
        service.PauseSession();

        Assert.Equal("Paused", service.Snapshot().Status);
        Assert.True(service.Snapshot().ElapsedSeconds >= 0);
    }

    [Fact]
    public void PauseSession_WhenNotRunning_NoOp()
    {
        var service = CreateService();
        service.PauseSession();
        Assert.Equal("None", service.Snapshot().Status);
    }

    [Fact]
    public void EndSession_MarksEnded()
    {
        var service = CreateService();
        service.StartSession();
        service.EndSession();
        Assert.Equal("Ended", service.Snapshot().Status);
    }

    [Fact]
    public void EndSession_WhenAlreadyEnded_NoOp()
    {
        var service = CreateService();
        service.StartSession();
        service.EndSession();
        service.EndSession();
        Assert.Equal("Ended", service.Snapshot().Status);
    }

    [Fact]
    public void ElapsedSeconds_Paused_Stable()
    {
        var service = CreateService();
        service.StartSession();
        Thread.Sleep(100);
        service.PauseSession();

        var e1 = service.Snapshot().ElapsedSeconds;
        Thread.Sleep(100);
        var e2 = service.Snapshot().ElapsedSeconds;
        Assert.Equal(e1, e2);
    }

    // Student agents drive their own timer and pause screen from this same
    // event, so the lab-wide state must only ever reach teacher screens.
    [Fact]
    public void LabState_IsSentToTeachersOnly()
    {
        var teachers = new Mock<IClientProxy>();
        var others = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(others.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(others.Object);
        clients.Setup(c => c.Group(Shared.Contracts.HubEventNames.TeachersGroup)).Returns(teachers.Object);
        var hub = new Mock<IHubContext<Server.Hubs.RemoteMonitoringHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        var service = new SessionManagerService(hub.Object);

        service.StartLab(7);
        service.PauseSession();
        service.ResumeLab();
        service.EndSession();

        teachers.Verify(p => p.SendCoreAsync(Shared.Contracts.HubEventNames.GlobalSessionState,
            It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
        others.Verify(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void StartLab_RestartsFromZeroUnderTheRule_AndEndClearsTheRule()
    {
        var service = CreateService();
        service.StartLab(3);
        service.PauseSession();
        service.StartLab(5);

        Assert.Equal("Running", service.Snapshot().Status);
        Assert.True(service.Snapshot().ElapsedSeconds < 5);
        Assert.Equal(5, service.LabRuleId);
        Assert.True(service.IsLabOpen);

        service.EndSession();
        Assert.Null(service.LabRuleId);
        Assert.False(service.IsLabOpen);
    }

    // Sign-in depends on the lab being open, so a server restart mid-class must
    // not forget it - that would lock the room out and reset every timer.
    [Fact]
    public void LabState_SurvivesAServerRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cams-lab-{Guid.NewGuid():N}.json");
        try
        {
            var hub = Mock.Of<IHubContext<Server.Hubs.RemoteMonitoringHub>>(h =>
                h.Clients == Mock.Of<IHubClients>(c => c.Group(It.IsAny<string>()) == Mock.Of<IClientProxy>()));
            var before = new SessionManagerService(hub, path);
            before.StartLab(9);
            before.PauseSession();

            var after = new SessionManagerService(hub, path);
            Assert.Equal("Paused", after.Snapshot().Status);
            Assert.Equal(9, after.LabRuleId);
            Assert.True(after.IsLabOpen);

            after.EndSession();
            Assert.False(new SessionManagerService(hub, path).IsLabOpen);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StartEndStart_ResetsElapsed()
    {
        var service = CreateService();
        service.StartSession();
        Thread.Sleep(100);
        service.EndSession();
        service.StartSession();

        Assert.True(service.Snapshot().ElapsedSeconds < 5);
    }
}