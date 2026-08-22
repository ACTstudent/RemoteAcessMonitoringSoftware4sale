using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Shared.Contracts;

namespace Server.Tests;

public class SystemAndModelTests
{
    private ApplicationDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public void Student_FullName_SplitAndConcat_Works()
    {
        var s1 = new Student { FirstName = "Juan", LastName = "Luna" };
        Assert.Equal("Juan Luna", s1.FullName);

        var s2 = new Student { FullName = "Antonio Luna" };
        s2.FirstName = "Antonio";
        s2.LastName = "Luna";
        Assert.Equal("Antonio Luna", s2.FullName);
    }

    [Fact]
    public void Teacher_DefaultStatus_IsActive()
    {
        var t = new Teacher { Username = "teacher1" };
        Assert.Equal("Active", t.Status);
    }

    [Fact]
    public void Class_DefaultStatus_IsActive()
    {
        var c = new Class { ClassName = "Grade 10" };
        Assert.Equal("Active", c.Status);
        Assert.False(c.IsArchived);
    }

    [Fact]
    public void Computer_DefaultStatus_IsAvailable()
    {
        var comp = new Computer { LaboratoryStation = "PC-01" };
        Assert.Equal("Available", comp.Status);
    }

    [Fact]
    public void LanConfiguration_DefaultIsActive_IsTrue()
    {
        var lan = new LanConfiguration();
        Assert.True(lan.IsActive);
        Assert.Equal(5000, lan.ServerPort);
    }

    [Fact]
    public void LabSession_DurationMinutes_CalculatesCorrectly()
    {
        var now = DateTime.Now;
        var s = new LabSession
        {
            StartTime = now,
            EndTime = now.AddMinutes(45),
            Status = "Ended"
        };
        Assert.NotNull(s.EndTime);
        Assert.Equal(45, (s.EndTime.Value - s.StartTime).TotalMinutes);
    }

    [Fact]
    public void ActiveAppMessage_Constructor_SetsProperties()
    {
        var now = DateTime.Now;
        var msg = new ActiveAppMessage("c1", "s1", "PC-01", "notepad.exe", now);
        Assert.Equal("c1", msg.ConnectionId);
        Assert.Equal("s1", msg.StudentId);
        Assert.Equal("PC-01", msg.PcName);
        Assert.Equal("notepad.exe", msg.ApplicationName);
    }

    [Fact]
    public void IdleStatusMessage_Constructor_SetsProperties()
    {
        var now = DateTime.Now;
        var msg = new IdleStatusMessage("c1", "s1", "PC-02", true, now);
        Assert.Equal("c1", msg.ConnectionId);
        Assert.Equal("s1", msg.StudentId);
        Assert.Equal("PC-02", msg.PcName);
        Assert.True(msg.IsIdle);
    }

    [Fact]
    public void StudentConnectionMessage_Constructor_SetsProperties()
    {
        var now = DateTime.Now;
        var msg = new StudentConnectionMessage("conn-123", "s10", "PC-05", now);
        Assert.Equal("conn-123", msg.ConnectionId);
        Assert.Equal("s10", msg.StudentId);
        Assert.Equal("PC-05", msg.PcName);
    }

    [Fact]
    public void GlobalSessionMessage_Constructor_SetsProperties()
    {
        var now = DateTime.Now;
        var msg = new GlobalSessionMessage("Running", 300, now);
        Assert.Equal("Running", msg.Status);
        Assert.Equal(300, msg.ElapsedSeconds);
    }

    [Fact]
    public void InfractionMessage_Constructor_SetsProperties()
    {
        var now = DateTime.Now;
        var msg = new InfractionMessage("c1", "s5", "PC-03", "facebook.com", "Website", now);
        Assert.Equal("c1", msg.ConnectionId);
        Assert.Equal("s5", msg.StudentId);
        Assert.Equal("PC-03", msg.PcName);
        Assert.Equal("facebook.com", msg.Target);
        Assert.Equal("Website", msg.TargetType);
    }

    [Fact]
    public void NotificationMessage_Constructor_SetsProperties()
    {
        var now = DateTime.Now;
        var msg = new NotificationMessage("Warning", "Session Ending", "5 minutes remaining.", now);
        Assert.Equal("Warning", msg.Type);
        Assert.Equal("Session Ending", msg.Title);
        Assert.Equal("5 minutes remaining.", msg.Message);
    }

    [Fact]
    public void RestrictionRuleMessage_Constructor_SetsProperties()
    {
        var msg = new RestrictionRuleMessage(1, "BlockWebsite", "youtube.com", "Block");
        Assert.Equal(1, msg.Id);
        Assert.Equal("BlockWebsite", msg.RuleType);
        Assert.Equal("youtube.com", msg.Target);
        Assert.Equal("Block", msg.Mode);
    }

    [Fact]
    public async Task AuditLog_DbPersistence_SavesDetails()
    {
        using var db = GetDbContext();
        var log = new AuditLog
        {
            UserType = "Admin",
            UserId = 1,
            Action = "TestAudit",
            Details = "Testing Audit persistence",
            IpAddress = "127.0.0.1",
            Timestamp = DateTime.Now
        };

        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();

        var saved = await db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "TestAudit");
        Assert.NotNull(saved);
        Assert.Equal("127.0.0.1", saved.IpAddress);
    }

    [Fact]
    public async Task UsageLog_DbPersistence_SavesDetails()
    {
        using var db = GetDbContext();
        var uLog = new UsageLog
        {
            StudentId = 1,
            PcName = "PC-01",
            AppName = "code.exe",
            Timestamp = DateTime.Now
        };

        db.UsageLogs.Add(uLog);
        await db.SaveChangesAsync();

        var saved = await db.UsageLogs.FirstOrDefaultAsync(u => u.AppName == "code.exe");
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task ClassStudent_JoinEntity_LinksClassAndStudent()
    {
        using var db = GetDbContext();
        var cls = new Class { ClassName = "Grade 12 - TVL" };
        var student = new Student { FullName = "Graciano Lopez Jaena", Username = "gjaena", PasswordHash = "hash" };

        db.Classes.Add(cls);
        db.Students.Add(student);
        await db.SaveChangesAsync();

        var cs = new ClassStudent { ClassId = cls.ClassId, StudentId = student.Id };
        db.ClassStudents.Add(cs);
        await db.SaveChangesAsync();

        var join = await db.ClassStudents.FirstOrDefaultAsync(x => x.ClassId == cls.ClassId && x.StudentId == student.Id);
        Assert.NotNull(join);
    }

    [Fact]
    public async Task BlacklistItem_DbPersistence_SavesItem()
    {
        using var db = GetDbContext();
        var item = new BlacklistItem { TargetType = "Domain", Value = "malicious.com", IsActive = true, CreatedAt = DateTime.Now };
        db.BlacklistItems.Add(item);
        await db.SaveChangesAsync();

        var saved = await db.BlacklistItems.FirstOrDefaultAsync(b => b.Value == "malicious.com");
        Assert.NotNull(saved);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task Notification_DbPersistence_SavesNotification()
    {
        using var db = GetDbContext();
        var notif = new Notification { Title = "System Alert", Message = "Maintenance at 5 PM", CreatedAt = DateTime.Now };
        db.Notifications.Add(notif);
        await db.SaveChangesAsync();

        var saved = await db.Notifications.FirstOrDefaultAsync(n => n.Title == "System Alert");
        Assert.NotNull(saved);
        Assert.False(saved.IsRead);
    }

    [Fact]
    public async Task RestrictionRule_DbPersistence_SavesRule()
    {
        using var db = GetDbContext();
        var rule = new RestrictionRule { RuleType = "BlockProcess", Target = "torrent.exe", IsGlobal = true, IsActive = true, CreatedAt = DateTime.Now };
        db.RestrictionRules.Add(rule);
        await db.SaveChangesAsync();

        var saved = await db.RestrictionRules.FirstOrDefaultAsync(r => r.Target == "torrent.exe");
        Assert.NotNull(saved);
        Assert.True(saved.IsGlobal);
    }

    [Fact]
    public async Task SessionRule_DbPersistence_SavesRule()
    {
        using var db = GetDbContext();
        var rule = new SessionRule { Name = "Quiz Mode 30m", MaxDurationMinutes = 30, IsDefault = false, IsActive = true, CreatedAt = DateTime.Now };
        db.SessionRules.Add(rule);
        await db.SaveChangesAsync();

        var saved = await db.SessionRules.FirstOrDefaultAsync(s => s.Name == "Quiz Mode 30m");
        Assert.NotNull(saved);
        Assert.Equal(30, saved.MaxDurationMinutes);
    }

    [Fact]
    public void HubEventNames_Constants_AreDefined()
    {
        Assert.Equal("Teachers", HubEventNames.TeachersGroup);
        Assert.Equal("Students", HubEventNames.StudentsGroup);
        Assert.Equal("ReceiveScreenFrame", HubEventNames.ReceiveScreenFrame);
        Assert.Equal("StudentConnected", HubEventNames.StudentConnected);
        Assert.Equal("StudentDisconnected", HubEventNames.StudentDisconnected);
    }

    [Fact]
    public void Student_Validation_RequiredFields()
    {
        var s = new Student { StudentNumber = "STU-001", FullName = "Juan Dela Cruz", Username = "stu1", PasswordHash = "hash" };
        var validationResults = new List<ValidationResult>();
        var ctx = new ValidationContext(s);
        bool isValid = Validator.TryValidateObject(s, ctx, validationResults, true);
        Assert.True(isValid);
    }

    [Fact]
    public void Teacher_Validation_RequiredFields()
    {
        var t = new Teacher { FirstName = "Maria", LastName = "Santos", Username = "teacher1", PasswordHash = "hash" };
        var validationResults = new List<ValidationResult>();
        var ctx = new ValidationContext(t);
        bool isValid = Validator.TryValidateObject(t, ctx, validationResults, true);
        Assert.True(isValid);
    }

    [Fact]
    public void Computer_Validation_RequiredFields()
    {
        var c = new Computer { LaboratoryStation = "PC-01" };
        var validationResults = new List<ValidationResult>();
        var ctx = new ValidationContext(c);
        bool isValid = Validator.TryValidateObject(c, ctx, validationResults, true);
        Assert.True(isValid);
    }

    [Fact]
    public void Class_Validation_RequiredFields()
    {
        var c = new Class { ClassName = "Grade 7 - Rose" };
        var validationResults = new List<ValidationResult>();
        var ctx = new ValidationContext(c);
        bool isValid = Validator.TryValidateObject(c, ctx, validationResults, true);
        Assert.True(isValid);
    }

    [Fact]
    public void Student_EmptyConstructor_DefaultValues()
    {
        var s = new Student();
        Assert.Equal("Active", s.Status);
        Assert.Empty(s.StudentNumber);
        Assert.Empty(s.Username);
    }

    [Fact]
    public void Teacher_EmptyConstructor_DefaultValues()
    {
        var t = new Teacher();
        Assert.Equal("Active", t.Status);
        Assert.Empty(t.Username);
        Assert.Empty(t.Email);
    }

    [Fact]
    public void Class_EmptyConstructor_DefaultValues()
    {
        var c = new Class();
        Assert.Equal("Active", c.Status);
        Assert.False(c.IsArchived);
        Assert.Empty(c.ClassName);
    }

    [Fact]
    public void Computer_EmptyConstructor_DefaultValues()
    {
        var comp = new Computer();
        Assert.Equal("Available", comp.Status);
        Assert.Empty(comp.LaboratoryStation);
    }

    [Fact]
    public void Admin_ModelProperties_Work()
    {
        var admin = new Admin { Username = "admin1", PasswordHash = "hash" };
        Assert.Equal("admin1", admin.Username);
    }

    [Fact]
    public void Role_ModelProperties_Work()
    {
        var role = new Role { Name = "Instructor", Description = "Teaching Staff" };
        Assert.Equal("Instructor", role.Name);
        Assert.Equal("Teaching Staff", role.Description);
    }

    [Fact]
    public void Permission_ModelProperties_Work()
    {
        var perm = new Permission { Name = "EditUsers", Description = "Allows editing users" };
        Assert.Equal("EditUsers", perm.Name);
        Assert.Equal("Allows editing users", perm.Description);
    }

    [Fact]
    public void LabSession_DefaultIsActive_IsTrue()
    {
        var s = new LabSession();
        Assert.True(s.IsActive);
        Assert.Equal("Running", s.Status);
    }

    [Fact]
    public void LanConfiguration_Properties_CanSetAndGet()
    {
        var lan = new LanConfiguration
        {
            ServerAddress = "10.0.0.1",
            ServerPort = 8080,
            Gateway = "10.0.0.254",
            DnsServer = "1.1.1.1",
            DhcpRangeStart = "10.0.0.100",
            DhcpRangeEnd = "10.0.0.200"
        };
        Assert.Equal("10.0.0.1", lan.ServerAddress);
        Assert.Equal(8080, lan.ServerPort);
        Assert.Equal("10.0.0.254", lan.Gateway);
    }

    [Fact]
    public void RestrictionRule_GlobalSetting_Works()
    {
        var r = new RestrictionRule { IsGlobal = true, RuleType = "BlockApp", Target = "game.exe" };
        Assert.True(r.IsGlobal);
        Assert.Equal("BlockApp", r.RuleType);
    }

    [Fact]
    public void BlacklistItem_ActiveSetting_Works()
    {
        var item = new BlacklistItem { IsActive = true, TargetType = "IP", Value = "192.168.1.50" };
        Assert.True(item.IsActive);
        Assert.Equal("IP", item.TargetType);
    }

    [Fact]
    public void SessionRule_Duration_Works()
    {
        var rule = new SessionRule { MaxDurationMinutes = 120 };
        Assert.Equal(120, rule.MaxDurationMinutes);
    }

    [Fact]
    public void Notification_ReadState_CanBeUpdated()
    {
        var notif = new Notification { IsRead = false };
        notif.IsRead = true;
        Assert.True(notif.IsRead);
    }

    [Fact]
    public void AuditLog_IpAddress_SupportsIPv6()
    {
        var log = new AuditLog { IpAddress = "::1" };
        Assert.Equal("::1", log.IpAddress);
    }

    [Fact]
    public void UsageLog_PcName_SupportsSpecialChars()
    {
        var u = new UsageLog { PcName = "LAB-01_STATION-A" };
        Assert.Equal("LAB-01_STATION-A", u.PcName);
    }

    [Fact]
    public void ActiveAppMessage_EqualsContract_Works()
    {
        var now = DateTime.Today;
        var a1 = new ActiveAppMessage("c1", "s1", "PC-1", "chrome.exe", now);
        var a2 = new ActiveAppMessage("c1", "s1", "PC-1", "chrome.exe", now);
        Assert.Equal(a1, a2);
    }

    [Fact]
    public void IdleStatusMessage_EqualsContract_Works()
    {
        var now = DateTime.Today;
        var i1 = new IdleStatusMessage("c1", "s1", "PC-1", true, now);
        var i2 = new IdleStatusMessage("c1", "s1", "PC-1", true, now);
        Assert.Equal(i1, i2);
    }

    [Fact]
    public void StudentConnectionMessage_EqualsContract_Works()
    {
        var now = DateTime.Today;
        var c1 = new StudentConnectionMessage("conn-1", "1", "PC-1", now);
        var c2 = new StudentConnectionMessage("conn-1", "1", "PC-1", now);
        Assert.Equal(c1, c2);
    }

    [Fact]
    public void NotificationMessage_EqualsContract_Works()
    {
        var now = DateTime.Today;
        var n1 = new NotificationMessage("Info", "Hello", "World", now);
        var n2 = new NotificationMessage("Info", "Hello", "World", now);
        Assert.Equal(n1, n2);
    }

    [Fact]
    public void InfractionMessage_EqualsContract_Works()
    {
        var now = DateTime.Today;
        var f1 = new InfractionMessage("c1", "s1", "PC-1", "target", "Block", now);
        var f2 = new InfractionMessage("c1", "s1", "PC-1", "target", "Block", now);
        Assert.Equal(f1, f2);
    }

    [Fact]
    public void RestrictionRuleMessage_EqualsContract_Works()
    {
        var r1 = new RestrictionRuleMessage(1, "BlockWeb", "domain.com", "Block");
        var r2 = new RestrictionRuleMessage(1, "BlockWeb", "domain.com", "Block");
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void GlobalSessionMessage_EqualsContract_Works()
    {
        var now = DateTime.Today;
        var g1 = new GlobalSessionMessage("Running", 100, now);
        var g2 = new GlobalSessionMessage("Running", 100, now);
        Assert.Equal(g1, g2);
    }
}
