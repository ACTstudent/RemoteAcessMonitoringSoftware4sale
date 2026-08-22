using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Server.Controllers;
using Server.Data;
using Server.Hubs;
using Server.Models;
using Server.Services;

namespace Server.Tests.Controllers;

public class TeacherControllerTests
{
    private ApplicationDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private TeacherController CreateController(ApplicationDbContext context, bool isTeacher = true)
    {
        var hubMock = new Mock<IHubContext<RemoteMonitoringHub>>();
        var clientsMock = new Mock<IHubClients>();
        var proxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(proxyMock.Object);
        hubMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        var sessionManager = new SessionManagerService(hubMock.Object);
        var controller = new TeacherController(context, sessionManager);
        var httpContext = new DefaultHttpContext();
        httpContext.Session = new FakeSession();
        if (isTeacher)
        {
            httpContext.Session.SetString("Role", "Teacher");
            httpContext.Session.SetInt32("TeacherId", 1);
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    [Fact]
    public async Task Dashboard_UnauthorizedUser_RedirectsToLogin()
    {
        using var db = GetDbContext();
        var controller = CreateController(db, isTeacher: false);
        var result = await controller.Dashboard();
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Login", redirect.ActionName);
        Assert.Equal("Account", redirect.ControllerName);
    }

    [Fact]
    public async Task Dashboard_AuthorizedUser_ReturnsView()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var result = await controller.Dashboard();
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public void GlobalSessionState_ReturnsJson()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var result = controller.GlobalSessionState();
        Assert.IsType<JsonResult>(result);
    }

    [Fact]
    public async Task Sessions_ReturnsViewWithData()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var result = await controller.Sessions();
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Session_StartPauseEnd_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        var student = new Student { FullName = "Test Student", Username = "teststudent", PasswordHash = "hash" };
        var computer = new Computer { LaboratoryStation = "PC-01", Status = "Available" };
        var rule = new SessionRule { Name = "Standard 45", MaxDurationMinutes = 45, IsDefault = true, IsActive = true };

        db.Students.Add(student);
        db.Computers.Add(computer);
        db.SessionRules.Add(rule);
        await db.SaveChangesAsync();

        // 1. Start Session
        var startResult = await controller.StartSession(student.Id, computer.ComputerId, rule.SessionRuleId);
        Assert.IsType<RedirectToActionResult>(startResult);

        var session = await db.LabSessions.FirstOrDefaultAsync(s => s.StudentId == student.Id);
        Assert.NotNull(session);
        Assert.Equal("Running", session.Status);
        Assert.True(session.IsActive);

        var updatedComp = await db.Computers.FindAsync(computer.ComputerId);
        Assert.Equal("In Use", updatedComp?.Status);

        // 2. Toggle Pause (Running -> Paused)
        var pauseResult = await controller.TogglePause(session.Id);
        Assert.IsType<RedirectToActionResult>(pauseResult);
        Assert.Equal("Paused", (await db.LabSessions.FindAsync(session.Id))?.Status);

        // 3. Toggle Resume (Paused -> Running)
        var resumeResult = await controller.TogglePause(session.Id);
        Assert.IsType<RedirectToActionResult>(resumeResult);
        Assert.Equal("Running", (await db.LabSessions.FindAsync(session.Id))?.Status);

        // 4. End Session
        var endResult = await controller.EndSession(session.Id);
        Assert.IsType<RedirectToActionResult>(endResult);

        var endedSession = await db.LabSessions.FindAsync(session.Id);
        Assert.Equal("Ended", endedSession?.Status);
        Assert.False(endedSession?.IsActive);

        var freedComp = await db.Computers.FindAsync(computer.ComputerId);
        Assert.Equal("Available", freedComp?.Status);
    }

    [Fact]
    public async Task Teacher_StudentCRUD_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        // 1. Create Student
        var createResult = await controller.CreateStudent(new Student
        {
            StudentNumber = "STU-101",
            FullName = "Jose Rizal",
            Username = "jrizal",
            PasswordHash = "pass123"
        });
        Assert.IsType<RedirectToActionResult>(createResult);

        var student = await db.Students.FirstOrDefaultAsync(s => s.Username == "jrizal");
        Assert.NotNull(student);

        // 2. Update Student
        var updateResult = await controller.UpdateStudent(new Student
        {
            Id = student.Id,
            StudentNumber = "STU-101",
            FullName = "Dr. Jose Rizal",
            Username = "jrizal"
        }, newPassword: "newpassword456");
        Assert.IsType<RedirectToActionResult>(updateResult);

        var updatedStudent = await db.Students.FindAsync(student.Id);
        Assert.Equal("Dr. Jose Rizal", updatedStudent?.FullName);

        // 3. Delete Student
        var deleteResult = await controller.DeleteStudent(student.Id);
        Assert.IsType<RedirectToActionResult>(deleteResult);
        Assert.Null(await db.Students.FindAsync(student.Id));
    }

    [Fact]
    public async Task CreateStudent_EmptyUsername_ReturnsErrorMessage()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var result = await controller.CreateStudent(new Student { Username = "" });
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Students", redirect.ActionName);
    }

    [Fact]
    public async Task Teacher_ClassCRUD_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        // 1. Create Class
        var createResult = await controller.CreateClass(new Class
        {
            ClassName = "Grade 7 - Narra",
            Section = "Narra",
            Subject = "Computer Education 7",
            GradeLevel = "Grade 7"
        });
        Assert.IsType<RedirectToActionResult>(createResult);

        var cls = await db.Classes.FirstOrDefaultAsync(c => c.ClassName == "Grade 7 - Narra");
        Assert.NotNull(cls);
        Assert.Equal(1, cls.TeacherId);

        // 2. Bulk Add Students
        var bulkResult = await controller.BulkAddStudents(
            cls.ClassId,
            new List<string> { "Andres", "Emilio" },
            new List<string> { "Bonifacio", "Aguinaldo" },
            new List<string> { "abonifacio", "eaguinaldo" },
            new List<string> { "pass1", "pass2" }
        );
        Assert.IsType<RedirectToActionResult>(bulkResult);

        var enrolled = await db.ClassStudents.Where(cs => cs.ClassId == cls.ClassId).ToListAsync();
        Assert.Equal(2, enrolled.Count);
    }

    [Fact]
    public async Task CreateClass_EmptyClassName_ReturnsErrorMessage()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var result = await controller.CreateClass(new Class { ClassName = "" });
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Classes", redirect.ActionName);
    }

    [Fact]
    public async Task UpdateComputer_StatusUpdated()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        var comp = new Computer { LaboratoryStation = "Station-05", Status = "Available" };
        db.Computers.Add(comp);
        await db.SaveChangesAsync();

        var result = await controller.UpdateComputer(new Computer { ComputerId = comp.ComputerId, LaboratoryStation = "Station-05", Status = "Maintenance" });
        Assert.IsType<RedirectToActionResult>(result);

        var updated = await db.Computers.FindAsync(comp.ComputerId);
        Assert.Equal("Maintenance", updated?.Status);
    }

    [Fact]
    public async Task SendNotification_AddsNotification()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        var result = await controller.SendNotification("Warning", "Time Up", "Please save your work.");
        Assert.IsType<JsonResult>(result);

        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Title == "Time Up");
        Assert.NotNull(notification);
    }

    [Fact]
    public async Task ExportRecordsCsv_ReturnsFileResult()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        db.LabSessions.Add(new LabSession { TeacherId = 1, StartTime = DateTime.Now, Status = "Ended" });
        await db.SaveChangesAsync();

        var result = await controller.ExportRecordsCsv();
        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv; charset=utf-8", fileResult.ContentType);
    }

    [Fact]
    public async Task ClassDetails_ReturnsViewResult()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        var cls = new Class { ClassName = "Grade 10 - Acacia" };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();

        var result = await controller.ClassDetails(cls.ClassId);
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task EnrollAndRemoveStudent_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        var cls = new Class { ClassName = "Grade 11 - STEM A" };
        var student = new Student { FullName = "Apolinario Mabini", Username = "amabini", PasswordHash = "hash" };
        db.Classes.Add(cls);
        db.Students.Add(student);
        await db.SaveChangesAsync();

        // 1. Enroll
        var enrollResult = await controller.EnrollStudent(cls.ClassId, student.Id);
        Assert.IsType<RedirectToActionResult>(enrollResult);

        // 2. Remove
        var removeResult = await controller.RemoveStudent(cls.ClassId, student.Id);
        Assert.IsType<RedirectToActionResult>(removeResult);
    }
}
