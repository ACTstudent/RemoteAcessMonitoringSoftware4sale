using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using Server.Controllers;
using Server.Data;
using Server.Models;

namespace Server.Tests.Controllers;

public class AdminControllerTests
{
    private ApplicationDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private AdminController CreateController(ApplicationDbContext context, bool isAdmin = true)
    {
        var controller = new AdminController(context);
        var httpContext = new DefaultHttpContext();
        httpContext.Session = new FakeSession();
        if (isAdmin)
        {
            httpContext.Session.SetString("Role", "Admin");
            httpContext.Session.SetInt32("AdminId", 1);
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    [Fact]
    public async Task Index_UnauthorizedUser_RedirectsToLogin()
    {
        using var db = GetDbContext();
        var controller = CreateController(db, isAdmin: false);
        var result = await controller.Index();
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Login", redirect.ActionName);
        Assert.Equal("Account", redirect.ControllerName);
    }

    [Fact]
    public async Task Index_AuthorizedUser_ReturnsViewWithCounts()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var result = await controller.Index();
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Teachers_CRUD_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        // 1. Create Teacher
        var createResult = await controller.CreateTeacher(new Teacher
        {
            FirstName = "Maria",
            LastName = "Santos",
            Username = "msantos",
            PasswordHash = "pass123",
            Email = "msantos@pardo.edu.ph",
            Status = "Active"
        });
        Assert.IsType<RedirectToActionResult>(createResult);

        var teacher = await db.Teachers.FirstOrDefaultAsync(t => t.Username == "msantos");
        Assert.NotNull(teacher);
        Assert.Equal("Maria", teacher.FirstName);

        // 2. Update Teacher (preserving password when newPassword is blank)
        var updateResult = await controller.UpdateTeacher(new Teacher
        {
            TeacherId = teacher.TeacherId,
            FirstName = "Maria Clara",
            LastName = "Santos",
            Username = "msantos_updated",
            Email = "mclara@pardo.edu.ph",
            Status = "Active"
        }, newPassword: null);
        Assert.IsType<RedirectToActionResult>(updateResult);

        var updatedTeacher = await db.Teachers.FindAsync(teacher.TeacherId);
        Assert.NotNull(updatedTeacher);
        Assert.Equal("Maria Clara", updatedTeacher.FirstName);
        Assert.Equal("msantos_updated", updatedTeacher.Username);
        Assert.NotNull(updatedTeacher.PasswordHash);

        // 3. Delete Teacher
        var deleteResult = await controller.DeleteTeacher(teacher.TeacherId);
        Assert.IsType<RedirectToActionResult>(deleteResult);
        Assert.Null(await db.Teachers.FindAsync(teacher.TeacherId));
    }

    [Fact]
    public async Task CreateTeacher_EmptyUsername_ReturnsErrorMessage()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var result = await controller.CreateTeacher(new Teacher { Username = "" });
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Teachers", redirect.ActionName);
    }

    [Fact]
    public async Task DeleteTeacherWithActiveClass_IsRejected()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var teacher = new Teacher { FirstName = "Maria", LastName = "Santos", Username = "msantos", PasswordHash = "hash", Status = "Active" };
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync();
        var cls = new Class { ClassName = "Grade 6 - Rose", TeacherId = teacher.TeacherId };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();

        var result = await controller.DeleteTeacher(teacher.TeacherId);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(await db.Teachers.FindAsync(teacher.TeacherId));
        Assert.Equal(teacher.TeacherId, (await db.Classes.FindAsync(cls.ClassId))?.TeacherId);
    }

    [Fact]
    public async Task Students_CRUD_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        // 1. Create Student
        var createResult = await controller.CreateStudent(new Student
        {
            StudentNumber = "STU-2026-001",
            FullName = "Juan Dela Cruz",
            Username = "jdelacruz",
            PasswordHash = "student123"
        });
        Assert.IsType<RedirectToActionResult>(createResult);

        var student = await db.Students.FirstOrDefaultAsync(s => s.Username == "jdelacruz");
        Assert.NotNull(student);
        Assert.Equal("Juan", student.FirstName);
        Assert.Equal("Dela Cruz", student.LastName);

        // 2. Update Student
        var updateResult = await controller.UpdateStudent(new Student
        {
            Id = student.Id,
            StudentNumber = "STU-2026-001",
            FullName = "Juanito Dela Cruz",
            Username = "jdelacruz"
        }, newPassword: "newpassword123");
        Assert.IsType<RedirectToActionResult>(updateResult);

        var updatedStudent = await db.Students.FindAsync(student.Id);
        Assert.NotNull(updatedStudent);
        Assert.Equal("Juanito Dela Cruz", updatedStudent.FullName);

        // 3. Assign Computer
        var comp = new Computer { LaboratoryStation = "PC-01", Status = "Available" };
        db.Computers.Add(comp);
        await db.SaveChangesAsync();

        var assignResult = await controller.AssignComputer(student.Id, comp.ComputerId);
        Assert.IsType<RedirectToActionResult>(assignResult);

        var assignedComp = await db.Computers.FindAsync(comp.ComputerId);
        Assert.Equal(student.Id.ToString(), assignedComp?.AssignedTo);
        Assert.Equal("Assigned", assignedComp?.Status);

        // 4. Delete Student
        var deleteResult = await controller.DeleteStudent(student.Id);
        Assert.IsType<RedirectToActionResult>(deleteResult);
        Assert.Null(await db.Students.FindAsync(student.Id));

        var unassignedComp = await db.Computers.FindAsync(comp.ComputerId);
        Assert.Null(unassignedComp?.AssignedTo);
        Assert.Equal("Available", unassignedComp?.Status);
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
    public async Task Classes_CRUD_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var teacher = new Teacher
        {
            FirstName = "Maria",
            LastName = "Santos",
            Username = "msantos",
            PasswordHash = "hash",
            Status = "Active"
        };
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync();

        // 1. Create Class
        var createResult = await controller.CreateClass(new Class
        {
            ClassName = "Grade 6 - Sampaguita",
            Section = "Section A",
            Subject = "Computer Education",
            GradeLevel = "Grade 6",
            Schedule = "M/W 8:00 AM",
            TeacherId = teacher.TeacherId
        });
        Assert.IsType<RedirectToActionResult>(createResult);

        var cls = await db.Classes.FirstOrDefaultAsync(c => c.ClassName == "Grade 6 - Sampaguita");
        Assert.NotNull(cls);

        // 2. Add Student to Class
        var addStudentResult = await controller.AddStudentToClass(cls.ClassId, "Pedro", "Penduko", "ppenduko", "pass123");
        Assert.IsType<RedirectToActionResult>(addStudentResult);

        var student = await db.Students.FirstOrDefaultAsync(s => s.Username == "ppenduko");
        Assert.NotNull(student);
        Assert.Equal(cls.ClassId, student.ClassId);

        // 3. Archive Class
        var archiveResult = await controller.ArchiveClass(cls.ClassId);
        Assert.IsType<RedirectToActionResult>(archiveResult);
        var archivedClass = await db.Classes.FindAsync(cls.ClassId);
        Assert.True(archivedClass?.IsArchived);

        // 4. Delete Class
        var deleteResult = await controller.DeleteClass(cls.ClassId);
        Assert.IsType<RedirectToActionResult>(deleteResult);
        Assert.Null(await db.Classes.FindAsync(cls.ClassId));
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
    public async Task Computers_CRUD_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        // 1. Create Computer
        var createResult = await controller.CreateComputer(new Computer
        {
            LaboratoryStation = "PC-Lab-10",
            Status = "Available"
        });
        Assert.IsType<RedirectToActionResult>(createResult);

        var computer = await db.Computers.FirstOrDefaultAsync(c => c.LaboratoryStation == "PC-Lab-10");
        Assert.NotNull(computer);

        // 2. Update Computer
        var updateResult = await controller.UpdateComputer(new Computer
        {
            ComputerId = computer.ComputerId,
            LaboratoryStation = "PC-Lab-10",
            Status = "Maintenance"
        });
        Assert.IsType<RedirectToActionResult>(updateResult);

        var updatedComp = await db.Computers.FindAsync(computer.ComputerId);
        Assert.Equal("Maintenance", updatedComp?.Status);

        // 3. Delete Computer
        var deleteResult = await controller.DeleteComputer(computer.ComputerId);
        Assert.IsType<RedirectToActionResult>(deleteResult);
        Assert.Null(await db.Computers.FindAsync(computer.ComputerId));
    }

    [Fact]
    public async Task CreateComputer_EmptyStation_ReturnsErrorMessage()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var result = await controller.CreateComputer(new Computer { LaboratoryStation = "" });
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Computers", redirect.ActionName);
    }

    [Fact]
    public async Task Roles_CRUD_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        // 1. Create Role
        var createResult = await controller.CreateRole(new Role { Name = "Lab Assistant", Description = "Assistant role" });
        Assert.IsType<RedirectToActionResult>(createResult);

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Lab Assistant");
        Assert.NotNull(role);

        // 2. Delete Role
        var deleteResult = await controller.DeleteRole(role.RoleId);
        Assert.IsType<RedirectToActionResult>(deleteResult);
        Assert.Null(await db.Roles.FindAsync(role.RoleId));
    }

    [Fact]
    public async Task CreateRole_EmptyName_ReturnsErrorMessage()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var result = await controller.CreateRole(new Role { Name = "" });
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Roles", redirect.ActionName);
    }

    [Fact]
    public async Task Restrictions_CRUD_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        // 1. Create Restriction
        var createResult = await controller.CreateRestriction(new RestrictionRule { RuleType = "BlockWebsite", Target = "facebook.com" });
        Assert.IsType<RedirectToActionResult>(createResult);

        var rule = await db.RestrictionRules.FirstOrDefaultAsync(r => r.Target == "facebook.com");
        Assert.NotNull(rule);

        // 2. Delete Restriction
        var deleteResult = await controller.DeleteRestriction(rule.RestrictionRuleId);
        Assert.IsType<RedirectToActionResult>(deleteResult);
        Assert.Null(await db.RestrictionRules.FindAsync(rule.RestrictionRuleId));
    }

    [Fact]
    public async Task Blacklists_CRUD_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        // 1. Create Blacklist
        var createResult = await controller.CreateBlacklist(new BlacklistItem { TargetType = "Process", Value = "game.exe" });
        Assert.IsType<RedirectToActionResult>(createResult);

        var item = await db.BlacklistItems.FirstOrDefaultAsync(b => b.Value == "game.exe");
        Assert.NotNull(item);

        // 2. Delete Blacklist
        var deleteResult = await controller.DeleteBlacklist(item.BlacklistItemId);
        Assert.IsType<RedirectToActionResult>(deleteResult);
        Assert.Null(await db.BlacklistItems.FindAsync(item.BlacklistItemId));
    }

    [Fact]
    public async Task SessionRules_CRUD_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        // 1. Create Session Rule
        var createResult = await controller.CreateSessionRule(new SessionRule { Name = "Exam Rule", MaxDurationMinutes = 60, IsDefault = true });
        Assert.IsType<RedirectToActionResult>(createResult);

        var rule = await db.SessionRules.FirstOrDefaultAsync(r => r.Name == "Exam Rule");
        Assert.NotNull(rule);
        Assert.True(rule.IsDefault);

        // 2. Delete Session Rule
        var deleteResult = await controller.DeleteSessionRule(rule.SessionRuleId);
        Assert.IsType<RedirectToActionResult>(deleteResult);
        Assert.Null(await db.SessionRules.FindAsync(rule.SessionRuleId));
    }

    [Fact]
    public async Task UpdateSessionRule_UpdatesAndDeactivatesWithoutChangingIdentity()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var rule = new SessionRule { Name = "Old", MaxDurationMinutes = 30, IsActive = true };
        db.SessionRules.Add(rule);
        await db.SaveChangesAsync();

        await controller.UpdateSessionRule(new SessionRule
        {
            SessionRuleId = rule.SessionRuleId,
            Name = "Updated",
            MaxDurationMinutes = 90,
            AllowPause = false,
            AllowRemoteControl = false,
            IsActive = false
        });

        var updated = await db.SessionRules.FindAsync(rule.SessionRuleId);
        Assert.Equal("Updated", updated?.Name);
        Assert.Equal(90, updated?.MaxDurationMinutes);
        Assert.False(updated?.IsActive);
    }

    [Fact]
    public async Task LanConfig_Save_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        var saveResult = await controller.SaveLanConfig(new LanConfiguration
        {
            ServerAddress = "192.168.1.100",
            ServerPort = 5000,
            Gateway = "192.168.1.1",
            DhcpRangeStart = "192.168.1.10",
            DhcpRangeEnd = "192.168.1.200",
            DnsServer = "8.8.8.8",
            IsActive = true
        });
        Assert.IsType<RedirectToActionResult>(saveResult);

        var config = await db.LanConfigurations.FirstOrDefaultAsync();
        Assert.NotNull(config);
        Assert.Equal("192.168.1.100", config.ServerAddress);
        Assert.Equal(5000, config.ServerPort);
    }

    [Fact]
    public async Task ExportAuditCsv_ReturnsFileResult()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        db.AuditLogs.Add(new AuditLog { Action = "Test", Details = "Testing audit", Timestamp = DateTime.Now });
        await db.SaveChangesAsync();

        var result = await controller.ExportAuditCsv();
        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv; charset=utf-8", fileResult.ContentType);
    }

    [Fact]
    public async Task ExportReportsCsv_ReturnsFileResult()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        db.LabSessions.Add(new LabSession { StartTime = DateTime.Now, Status = "Ended" });
        await db.SaveChangesAsync();

        var result = await controller.ExportReportsCsv();
        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv; charset=utf-8", fileResult.ContentType);
    }

    [Fact]
    public async Task ExportUsageCsv_ReturnsFileResult()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        db.UsageLogs.Add(new UsageLog { AppName = "chrome.exe", Timestamp = DateTime.Now });
        await db.SaveChangesAsync();

        var result = await controller.ExportUsageCsv(null, null);
        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv; charset=utf-8", fileResult.ContentType);
    }

    [Fact]
    public async Task ClassDetails_ReturnsViewResult()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var cls = new Class { ClassName = "Grade 8 - Daisy" };
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

        var teacher = new Teacher { FirstName = "Maria", LastName = "Santos", Username = "msantos", PasswordHash = "hash", Status = "Active" };
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync();
        var cls = new Class { ClassName = "Grade 9 - Lily", TeacherId = teacher.TeacherId };
        var student = new Student { FullName = "Maria Clara", Username = "mclara", PasswordHash = "hash" };
        db.Classes.Add(cls);
        db.Students.Add(student);
        await db.SaveChangesAsync();

        // 1. Enroll
        var enrollResult = await controller.EnrollStudent(cls.ClassId, student.Id);
        Assert.IsType<RedirectToActionResult>(enrollResult);

        var enrolled = await db.Students.FindAsync(student.Id);
        Assert.Equal(cls.ClassId, enrolled?.ClassId);

        // 2. Remove
        var removeResult = await controller.RemoveStudent(cls.ClassId, student.Id);
        Assert.IsType<RedirectToActionResult>(removeResult);

        var removed = await db.Students.FindAsync(student.Id);
        Assert.Null(removed?.ClassId);
    }
}
