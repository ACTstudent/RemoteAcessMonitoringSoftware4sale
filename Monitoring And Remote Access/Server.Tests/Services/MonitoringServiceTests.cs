using Server.Services;
using Shared.Contracts;

namespace Server.Tests.Services;

public class MonitoringServiceTests
{
    [Fact]
    public void RegisterStudent_AddsToActiveStudents()
    {
        var service = new MonitoringService();
        var msg = service.RegisterStudent("conn1", "student1", "PC01");

        Assert.Equal("conn1", msg.ConnectionId);
        Assert.Equal("student1", msg.StudentId);
        Assert.Equal("PC01", msg.PcName);
        Assert.Single(service.ActiveStudents);
    }

    [Fact]
    public void RegisterStudent_MultipleConnections_TracksAll()
    {
        var service = new MonitoringService();
        service.RegisterStudent("connA", "s1", "PC1");
        service.RegisterStudent("connB", "s2", "PC2");
        service.RegisterStudent("connC", "s3", "PC3");

        Assert.Equal(3, service.ActiveStudents.Count);
    }

    [Fact]
    public void RegisterStudent_OverwritesExistingConnectionId()
    {
        var service = new MonitoringService();
        service.RegisterStudent("conn1", "old", "PCOld");
        service.RegisterStudent("conn1", "new", "PCNew");

        var student = service.ActiveStudents.First(s => s.ConnectionId == "conn1");
        Assert.Equal("new", student.StudentId);
    }

    [Fact]
    public void UnregisterStudent_RemovesFromAllDictionaries()
    {
        var service = new MonitoringService();
        service.RegisterStudent("conn1", "s1", "PC1");
        service.ReportIdleStatus(new IdleStatusMessage("conn1", "s1", "PC1", true, DateTime.Now));
        service.ReportActiveApp(new ActiveAppMessage("conn1", "s1", "PC1", "chrome.exe", DateTime.Now));

        var removed = service.UnregisterStudent("conn1");
        Assert.NotNull(removed);
        Assert.Empty(service.ActiveStudents);
        Assert.Empty(service.IdleStatus);
        Assert.Empty(service.ActiveApps);
    }

    [Fact]
    public void UnregisterStudent_UnknownConnection_ReturnsNull()
    {
        var service = new MonitoringService();
        var result = service.UnregisterStudent("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public void ReportIdleStatus_UpdatesStatus()
    {
        var service = new MonitoringService();
        var msg = new IdleStatusMessage("connA", "s1", "PC1", true, DateTime.Now);
        service.ReportIdleStatus(msg);

        var status = service.IdleStatus.First();
        Assert.Equal("connA", status.ConnectionId);
        Assert.True(status.IsIdle);
    }

    [Fact]
    public void ReportIdleStatus_OverwritesPrevious()
    {
        var service = new MonitoringService();
        service.ReportIdleStatus(new IdleStatusMessage("conn1", "s1", "PC1", true, DateTime.Now));
        service.ReportIdleStatus(new IdleStatusMessage("conn1", "s1", "PC1", false, DateTime.Now));

        Assert.Single(service.IdleStatus);
        Assert.False(service.IdleStatus.First().IsIdle);
    }

    [Fact]
    public void ReportActiveApp_UpdatesApp()
    {
        var service = new MonitoringService();
        var msg = new ActiveAppMessage("conn1", "s1", "PC1", "notepad.exe", DateTime.Now);
        service.ReportActiveApp(msg);

        var app = service.ActiveApps.First();
        Assert.Equal("conn1", app.ConnectionId);
        Assert.Equal("notepad.exe", app.ApplicationName);
    }

    [Fact]
    public void ReportActiveApp_OverwritesPrevious()
    {
        var service = new MonitoringService();
        service.ReportActiveApp(new ActiveAppMessage("conn1", "s1", "PC1", "notepad.exe", DateTime.Now));
        service.ReportActiveApp(new ActiveAppMessage("conn1", "s1", "PC1", "excel.exe", DateTime.Now));

        Assert.Single(service.ActiveApps);
        Assert.Equal("excel.exe", service.ActiveApps.First().ApplicationName);
    }
}