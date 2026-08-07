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