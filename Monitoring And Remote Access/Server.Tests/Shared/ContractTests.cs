using Shared.Contracts;

namespace Server.Tests.SharedTests;

public class ContractTests
{
    [Fact]
    public void StudentConnectionMessage_Equality()
    {
        var a = new StudentConnectionMessage("c1", "s1", "PC1", new DateTime(2026, 1, 1));
        var b = new StudentConnectionMessage("c1", "s1", "PC1", new DateTime(2026, 1, 1));
        Assert.Equal(a, b);
        Assert.NotEqual(a, new StudentConnectionMessage("c2", "s1", "PC1", new DateTime(2026, 1, 1)));
    }

    [Fact]
    public void GlobalSessionMessage_Equatable()
    {
        var a = new GlobalSessionMessage("Running", 100, DateTime.Now);
        var b = a;
        Assert.Equal(a, b);
    }

    [Fact]
    public void NotificationMessage_Equatable()
    {
        var dt = DateTime.Now;
        var a = new NotificationMessage("Warning", "Title", "Body", dt);
        var b = new NotificationMessage("Warning", "Title", "Body", dt);
        Assert.Equal(a, b);
    }

    [Fact]
    public void IdleStatusMessage_Equatable()
    {
        var dt = DateTime.Now;
        var a = new IdleStatusMessage("c1", "s1", "PC1", true, dt);
        var b = new IdleStatusMessage("c1", "s1", "PC1", true, dt);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ActiveAppMessage_Equatable()
    {
        var dt = DateTime.Now;
        var a = new ActiveAppMessage("c1", "s1", "PC1", "chrome.exe", dt);
        var b = new ActiveAppMessage("c1", "s1", "PC1", "chrome.exe", dt);
        Assert.Equal(a, b);
    }

    [Fact]
    public void BroadcastMessage_Equatable()
    {
        var dt = DateTime.Now;
        var a = new BroadcastMessage("base64data", dt);
        var b = new BroadcastMessage("base64data", dt);
        Assert.Equal(a, b);
    }

    [Fact]
    public void InfractionMessage_Equatable()
    {
        var dt = DateTime.Now;
        var a = new InfractionMessage("c1", "s1", "PC01", "game.exe", "Application", dt);
        var b = new InfractionMessage("c1", "s1", "PC01", "game.exe", "Application", dt);
        Assert.Equal(a, b);
    }

    [Fact]
    public void RestrictionRuleMessage_Equatable()
    {
        var a = new RestrictionRuleMessage(5, "Application", "chrome.exe", "Block");
        var b = new RestrictionRuleMessage(5, "Application", "chrome.exe", "Block");
        Assert.Equal(a, b);
    }

    [Fact]
    public void HubEventNames_AllDefined()
    {
        Assert.False(string.IsNullOrEmpty(HubEventNames.TeachersGroup));
        Assert.False(string.IsNullOrEmpty(HubEventNames.StudentsGroup));
        Assert.False(string.IsNullOrEmpty(HubEventNames.ReceiveScreenFrame));
        Assert.False(string.IsNullOrEmpty(HubEventNames.StudentConnected));
        Assert.False(string.IsNullOrEmpty(HubEventNames.StudentDisconnected));
        Assert.False(string.IsNullOrEmpty(HubEventNames.LockStudent));
        Assert.False(string.IsNullOrEmpty(HubEventNames.UnlockStudent));
        Assert.False(string.IsNullOrEmpty(HubEventNames.ForceLogout));
        Assert.False(string.IsNullOrEmpty(HubEventNames.GlobalSessionState));
        Assert.False(string.IsNullOrEmpty(HubEventNames.SessionEnded));
        Assert.False(string.IsNullOrEmpty(HubEventNames.RestrictionsReceived));
        Assert.False(string.IsNullOrEmpty(HubEventNames.InfractionDetected));
    }
}