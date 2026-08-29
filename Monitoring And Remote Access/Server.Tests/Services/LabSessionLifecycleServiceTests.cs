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
        var hub = new Mock<IHubContext<RemoteMonitoringHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);

        var service = new LabSessionLifecycleService(db, hub.Object);
        Assert.Equal(1, await service.EndExpiredSessionsAsync());
        Assert.Equal("Ended", (await db.LabSessions.SingleAsync()).Status);
        Assert.False((await db.LabSessions.SingleAsync()).IsActive);
        Assert.Equal("Available", (await db.Computers.SingleAsync()).Status);
        Assert.Null((await db.Computers.SingleAsync()).AssignedTo);
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
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        var service = new LabSessionLifecycleService(db, hub.Object);
        Assert.Equal(1, await service.EndExpiredSessionsAsync());
        Assert.Equal(0, await service.EndExpiredSessionsAsync());
    }
}
