using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Server.Controllers;
using Server.Data;
using Server.Hubs;
using Server.Models;
using Server.Services;

namespace Server.Tests.Controllers;

public sealed class AdminHistoricalArchiveSqliteTests
{
    [Fact]
    public async Task ArchiveActions_PreserveHistoricallyReferencedRowsUnderSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var teacher = new Teacher { FirstName = "T", LastName = "One", Username = "teacher", PasswordHash = "hash" };
        var student = new Student { StudentNumber = "S-1", FullName = "Student", Username = "student", PasswordHash = "hash" };
        var computer = new Computer { LaboratoryStation = "PC-01" };
        var rule = new SessionRule { Name = "Historical", IsActive = true, IsDefault = true };
        db.AddRange(teacher, student, computer, rule);
        await db.SaveChangesAsync();
        var cls = new Class { ClassName = "Archived class", TeacherId = teacher.TeacherId, IsArchived = true };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        student.AdviserId = teacher.TeacherId;
        db.ClassStudents.Add(new ClassStudent { ClassId = cls.ClassId, StudentId = student.Id });
        db.LabSessions.Add(new LabSession
        {
            StudentId = student.Id,
            TeacherId = teacher.TeacherId,
            ComputerId = computer.ComputerId,
            SessionRuleId = rule.SessionRuleId,
            PCName = computer.LaboratoryStation,
            Status = "Ended",
            IsActive = false,
            EndTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        await controller.DeleteTeacher(teacher.TeacherId);
        await controller.DeleteStudent(student.Id);
        await controller.DeleteComputer(computer.ComputerId);
        await controller.DeleteSessionRule(rule.SessionRuleId);

        db.ChangeTracker.Clear();
        Assert.Equal("Inactive", (await db.Teachers.SingleAsync()).Status);
        Assert.Equal("Inactive", (await db.Students.SingleAsync()).Status);
        Assert.Equal("Archived", (await db.Computers.SingleAsync()).Status);
        Assert.False((await db.SessionRules.SingleAsync(item => item.SessionRuleId == rule.SessionRuleId)).IsActive);
        var history = await db.LabSessions.SingleAsync();
        Assert.NotNull(history.TeacherId);
        Assert.NotNull(history.ComputerId);
        Assert.NotNull(history.SessionRuleId);
        Assert.Single(await db.ClassStudents.ToListAsync());
    }

    [Fact]
    public async Task ArchiveComputer_WithActiveSession_ReturnsClearErrorAndPreservesComputer()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var student = new Student { StudentNumber = "S-2", FullName = "Student", Username = "student2", PasswordHash = "hash" };
        var computer = new Computer { LaboratoryStation = "PC-02" };
        db.AddRange(student, computer);
        await db.SaveChangesAsync();
        db.LabSessions.Add(new LabSession { StudentId = student.Id, ComputerId = computer.ComputerId, PCName = "PC-02", IsActive = true, Status = "Running" });
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        await controller.DeleteComputer(computer.ComputerId);

        Assert.NotEqual("Archived", (await db.Computers.FindAsync(computer.ComputerId))?.Status);
        Assert.Contains("active lab session", controller.TempData["ErrorMessage"]?.ToString());
    }

    private static AdminController CreateController(ApplicationDbContext db)
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(value => value.User(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(value => value.Users(It.IsAny<IReadOnlyList<string>>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(value => value.Client(It.IsAny<string>())).Returns(Mock.Of<ISingleClientProxy>());
        var hub = new Mock<IHubContext<RemoteMonitoringHub>>();
        hub.SetupGet(value => value.Clients).Returns(clients.Object);
        var controller = new AdminController(db, new LabSessionLifecycleService(db, hub.Object));
        var context = new DefaultHttpContext { Session = new FakeSession() };
        context.Session.SetString("Role", "Admin");
        context.Session.SetInt32("AdminId", 1);
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        controller.TempData = new TempDataDictionary(context, Mock.Of<ITempDataProvider>());
        return controller;
    }
}
