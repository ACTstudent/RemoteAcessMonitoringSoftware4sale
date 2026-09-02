using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Moq;
using Server.Controllers;
using Server.Authorization;
using Server.Data;
using Server.Hubs;
using Server.Models;
using Server.Services;

namespace Server.Tests.Controllers;

public class AdminControllerTests
{
    [Theory]
    [InlineData(nameof(AdminController.PauseAllSessions))]
    [InlineData(nameof(AdminController.ResumeAllSessions))]
    [InlineData(nameof(AdminController.EndAllSessions))]
    public void GlobalSessionActions_ArePostOnlyAndValidateAntiforgery(string actionName)
    {
        var action = typeof(AdminController).GetMethod(actionName)!;

        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    private ApplicationDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private AdminController CreateController(ApplicationDbContext context, bool isAdmin = true, int? teacherId = null)
    {
        var hub = new Mock<IHubContext<RemoteMonitoringHub>>();
        var clients = new Mock<IHubClients>();
        clients.Setup(value => value.User(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(value => value.Users(It.IsAny<IReadOnlyList<string>>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(value => value.Client(It.IsAny<string>())).Returns(Mock.Of<ISingleClientProxy>());
        hub.SetupGet(value => value.Clients).Returns(clients.Object);
        var controller = new AdminController(context, new LabSessionLifecycleService(context, hub.Object));
        var httpContext = new DefaultHttpContext();
        httpContext.Session = new FakeSession();
        if (isAdmin)
        {
            httpContext.Session.SetString("Role", "Admin");
            httpContext.Session.SetInt32("AdminId", 1);
            httpContext.User = Principal("Admin", 1);
        }
        else if (teacherId.HasValue)
        {
            httpContext.Session.SetString("Role", "Teacher");
            httpContext.Session.SetInt32("TeacherId", teacherId.Value);
            httpContext.User = Principal("Teacher", teacherId.Value);
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static ClaimsPrincipal Principal(string role, int id) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.Role, role),
        new Claim(ClaimTypes.NameIdentifier, id.ToString())
    }, "test"));

    private static AuthorizationFilterContext FilterContext(ClaimsPrincipal principal, string actionName)
    {
        var httpContext = new DefaultHttpContext { User = principal };
        var action = new ControllerActionDescriptor
        {
            MethodInfo = typeof(AdminController).GetMethod(actionName)!
        };
        var actionContext = new ActionContext(httpContext, new RouteData(), action, new ModelStateDictionary());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }

    [Fact]
    public async Task AuthorizationFilter_DefaultDeniesTeachersAndVerifiesActiveAccount()
    {
        using var db = GetDbContext();
        db.Teachers.AddRange(
            new Teacher { TeacherId = 10, FirstName = "Active", LastName = "Teacher", Username = "active", PasswordHash = "hash", Status = "" },
            new Teacher { TeacherId = 11, FirstName = "Inactive", LastName = "Teacher", Username = "inactive", PasswordHash = "hash", Status = "Inactive" });
        await db.SaveChangesAsync();
        var filter = new AdminControllerAuthorizationFilter(db);

        var activeShared = FilterContext(Principal("Teacher", 10), nameof(AdminController.Teachers));
        await filter.OnAuthorizationAsync(activeShared);
        Assert.Null(activeShared.Result);

        var activeAdminOnly = FilterContext(Principal("Teacher", 10), nameof(AdminController.Settings));
        await filter.OnAuthorizationAsync(activeAdminOnly);
        Assert.IsType<ForbidResult>(activeAdminOnly.Result);

        var inactiveShared = FilterContext(Principal("Teacher", 11), nameof(AdminController.Teachers));
        await filter.OnAuthorizationAsync(inactiveShared);
        Assert.IsType<ForbidResult>(inactiveShared.Result);

        var adminOnly = FilterContext(Principal("Admin", 1), nameof(AdminController.Settings));
        await filter.OnAuthorizationAsync(adminOnly);
        Assert.Null(adminOnly.Result);
    }

    [Fact]
    public async Task ActiveTeacherFilter_RejectsInactiveTeacher()
    {
        using var db = GetDbContext();
        db.Teachers.AddRange(
            new Teacher { TeacherId = 12, FirstName = "Active", LastName = "Teacher", Username = "active-12", PasswordHash = "hash", Status = "Active" },
            new Teacher { TeacherId = 13, FirstName = "Inactive", LastName = "Teacher", Username = "inactive-13", PasswordHash = "hash", Status = "Inactive" });
        await db.SaveChangesAsync();
        var filter = new ActiveTeacherAuthorizationFilter(db);

        var active = FilterContext(Principal("Teacher", 12), nameof(AdminController.Index));
        await filter.OnAuthorizationAsync(active);
        Assert.Null(active.Result);

        var inactive = FilterContext(Principal("Teacher", 13), nameof(AdminController.Index));
        await filter.OnAuthorizationAsync(inactive);
        Assert.IsType<ForbidResult>(inactive.Result);
    }

    [Theory]
    [InlineData(nameof(AdminController.Index))]
    [InlineData(nameof(AdminController.DeleteTeacher))]
    [InlineData(nameof(AdminController.AssignStudentToClass))]
    [InlineData(nameof(AdminController.BulkPreviewCsv))]
    [InlineData(nameof(AdminController.ComputerHistory))]
    [InlineData(nameof(AdminController.UpdateWebsiteCategory))]
    [InlineData(nameof(AdminController.SessionRules))]
    public void SharedActions_AreExplicitlyMarked(string actionName)
    {
        Assert.NotNull(typeof(AdminController).GetMethod(actionName)!.GetCustomAttribute<TeacherSharedActionAttribute>());
    }

    [Theory]
    [InlineData(nameof(AdminController.Settings))]
    [InlineData(nameof(AdminController.Roles))]
    [InlineData(nameof(AdminController.LanConfig))]
    [InlineData(nameof(AdminController.Reports))]
    [InlineData(nameof(AdminController.ExportReportsCsv))]
    [InlineData(nameof(AdminController.AuditLogs))]
    [InlineData(nameof(AdminController.SystemLogs))]
    public void AdminOnlyActions_AreNotTeacherMarked(string actionName)
    {
        Assert.Null(typeof(AdminController).GetMethod(actionName)!.GetCustomAttribute<TeacherSharedActionAttribute>());
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
    public async Task Admins_CRUD_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        await db.Admins.AddAsync(new Admin { Username = "root", FullName = "Root Admin", PasswordHash = "hash", IsActive = true });
        await db.SaveChangesAsync();

        // 1. Create admin
        var createResult = await controller.CreateAdmin(new Admin { Username = "admin2", FullName = "Second Administrator", PasswordHash = "pass123456" });
        Assert.IsType<RedirectToActionResult>(createResult);
        var created = await db.Admins.FirstOrDefaultAsync(a => a.Username == "admin2");
        Assert.NotNull(created);
        Assert.True(created.IsActive);

        // 2. Update admin
        var updateResult = await controller.UpdateAdmin(new Admin { Id = created.Id, Username = "admin2", FullName = "Second Admin Updated" }, newPassword: null);
        Assert.IsType<RedirectToActionResult>(updateResult);
        var updated = await db.Admins.FindAsync(created.Id);
        Assert.Equal("Second Admin Updated", updated!.FullName);

        // 3. Delete admin (a non-current account)
        var deleteResult = await controller.DeleteAdmin(created.Id);
        Assert.IsType<RedirectToActionResult>(deleteResult);
        Assert.Null(await db.Admins.FindAsync(created.Id));
    }

    [Fact]
    public async Task GlobalSessionActions_PersistTransitionsAndAuditThem()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var student = new Student { StudentNumber = "S-GLOBAL", FullName = "Student", Username = "global", PasswordHash = "hash" };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        db.LabSessions.Add(new LabSession { StudentId = student.Id, PCName = "PC-G", Status = "Running", IsActive = true });
        await db.SaveChangesAsync();

        await controller.PauseAllSessions();
        Assert.Equal("Paused", (await db.LabSessions.SingleAsync()).Status);
        await controller.ResumeAllSessions();
        Assert.Equal("Running", (await db.LabSessions.SingleAsync()).Status);
        await controller.EndAllSessions();

        Assert.Equal("Ended", (await db.LabSessions.SingleAsync()).Status);
        Assert.False((await db.LabSessions.SingleAsync()).IsActive);
        Assert.Equal(3, await db.AuditLogs.CountAsync(log => log.Action.StartsWith("Global")));
    }

    [Fact]
    public async Task Teachers_CRUD_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        db.Teachers.Add(new Teacher { FirstName = "Other", LastName = "Teacher", Username = "other-teacher", PasswordHash = "hash", Status = "Active" });
        await db.SaveChangesAsync();

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
        db.RestrictionRules.Add(new RestrictionRule
        {
            TeacherId = teacher.TeacherId,
            IsGlobal = false,
            RuleType = "Website",
            Target = "teacher-rule.test"
        });
        await db.SaveChangesAsync();

        // 3. Delete Teacher
        var deleteResult = await controller.DeleteTeacher(teacher.TeacherId);
        Assert.IsType<RedirectToActionResult>(deleteResult);
        Assert.Equal("Inactive", (await db.Teachers.FindAsync(teacher.TeacherId))?.Status);
        Assert.Single(await db.RestrictionRules.ToListAsync());
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
        Assert.Equal("Inactive", (await db.Students.FindAsync(student.Id))?.Status);

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
        Assert.Equal("Archived", (await db.Computers.FindAsync(computer.ComputerId))?.Status);
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
    public async Task TeacherCreatedRestriction_IsForcedGlobalAndOwnerless()
    {
        using var db = GetDbContext();
        var actor = new Teacher { TeacherId = 60, FirstName = "Policy", LastName = "Teacher", Username = "policy-teacher", PasswordHash = "hash", Status = "Active" };
        db.Teachers.Add(actor);
        await db.SaveChangesAsync();
        var controller = CreateController(db, isAdmin: false, teacherId: actor.TeacherId);

        await controller.CreateRestriction(new RestrictionRule
        {
            RuleType = "Website",
            Target = "example.test",
            Mode = "Block",
            IsGlobal = false,
            TeacherId = actor.TeacherId,
            IsActive = true
        });

        var rule = await db.RestrictionRules.SingleAsync();
        Assert.True(rule.IsGlobal);
        Assert.Null(rule.TeacherId);
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
        Assert.False((await db.SessionRules.FindAsync(rule.SessionRuleId))?.IsActive);
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
    public void LanConfig_IsDetectedReadOnlyStatus()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        controller.HttpContext.Request.Scheme = "https";
        controller.HttpContext.Request.Host = new HostString("cams.test", 5000);

        Assert.IsType<ViewResult>(controller.LanConfig());
        Assert.Equal("https://cams.test:5000", controller.ViewBag.DetectedEndpoint);
        Assert.Empty(db.LanConfigurations);
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

    [Fact]
    public async Task UnlockAccount_ClearsLockoutForEveryAccountRole()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var lockout = DateTime.UtcNow.AddMinutes(10);
        var admin = new Admin { Id = 1, Username = "admin", FullName = "Admin", PasswordHash = "hash", FailedLoginAttempts = 3, LockoutEndUtc = lockout };
        var teacher = new Teacher { TeacherId = 2, FirstName = "Locked", LastName = "Teacher", Username = "teacher", PasswordHash = "hash", FailedLoginAttempts = 4, LockoutEndUtc = lockout };
        var student = new Student { Id = 3, StudentNumber = "S-LOCK", FullName = "Locked Student", Username = "student", PasswordHash = "hash", FailedLoginAttempts = 2, LockoutEndUtc = lockout };
        db.AddRange(admin, teacher, student);
        await db.SaveChangesAsync();

        Assert.IsType<RedirectToActionResult>(await controller.UnlockAccount(AccountRole.Admin, admin.Id));
        Assert.IsType<RedirectToActionResult>(await controller.UnlockAccount(AccountRole.Teacher, teacher.TeacherId));
        Assert.IsType<RedirectToActionResult>(await controller.UnlockAccount(AccountRole.Student, student.Id));

        Assert.Equal(0, admin.FailedLoginAttempts);
        Assert.Null(admin.LockoutEndUtc);
        Assert.Equal(0, teacher.FailedLoginAttempts);
        Assert.Null(teacher.LockoutEndUtc);
        Assert.Equal(0, student.FailedLoginAttempts);
        Assert.Null(student.LockoutEndUtc);
        Assert.Equal(3, await db.AuditLogs.CountAsync(log => log.Action == "UnlockAccount"));
    }

    [Fact]
    public async Task SetAccountActive_RejectsDeactivatingLastActiveAdmin()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var admin = new Admin { Id = 1, Username = "only-admin", FullName = "Only Admin", PasswordHash = "hash", IsActive = true };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        var result = await controller.SetAccountActive(AccountRole.Admin, admin.Id, false);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Settings", redirect.ActionName);
        Assert.True((await db.Admins.FindAsync(admin.Id))?.IsActive);
        Assert.Empty(await db.AuditLogs.Where(log => log.Action == "DeactivateAccount").ToListAsync());
    }

    [Fact]
    public async Task SetAccountActive_UpdatesAdminTeacherAndStudentSafely()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var currentAdmin = new Admin { Id = 1, Username = "current", FullName = "Current", PasswordHash = "hash", IsActive = true };
        var otherAdmin = new Admin { Id = 2, Username = "other", FullName = "Other", PasswordHash = "hash", IsActive = false };
        var teacher = new Teacher { TeacherId = 3, FirstName = "A", LastName = "Teacher", Username = "teacher", PasswordHash = "hash", Status = "Active" };
        var otherTeacher = new Teacher { TeacherId = 5, FirstName = "Other", LastName = "Teacher", Username = "other-teacher", PasswordHash = "hash", Status = "Active" };
        var student = new Student { Id = 4, StudentNumber = "S-STATE", FullName = "A Student", Username = "student", PasswordHash = "hash", Status = "Active" };
        db.AddRange(currentAdmin, otherAdmin, teacher, otherTeacher, student);
        await db.SaveChangesAsync();

        await controller.SetAccountActive(AccountRole.Admin, otherAdmin.Id, true);
        await controller.SetAccountActive(AccountRole.Teacher, teacher.TeacherId, false);
        await controller.SetAccountActive(AccountRole.Student, student.Id, false);

        Assert.True(otherAdmin.IsActive);
        Assert.Equal("Inactive", teacher.Status);
        Assert.Equal("Inactive", student.Status);
        Assert.Equal(3, await db.AuditLogs.CountAsync(log => log.Action == "ActivateAccount" || log.Action == "DeactivateAccount"));
    }

    [Fact]
    public async Task SetAccountActive_RejectsTeacherWithActiveClass()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var teacher = new Teacher { TeacherId = 5, FirstName = "Busy", LastName = "Teacher", Username = "busy", PasswordHash = "hash", Status = "Active" };
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync();
        db.Classes.Add(new Class { ClassName = "Active Class", TeacherId = teacher.TeacherId });
        await db.SaveChangesAsync();

        var result = await controller.SetAccountActive(AccountRole.Teacher, teacher.TeacherId, false);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Active", teacher.Status);
    }

    [Fact]
    public async Task TeacherActor_CannotManageSelfOrTargetAdminThroughGenericActions()
    {
        using var db = GetDbContext();
        var actor = new Teacher
        {
            TeacherId = 20,
            FirstName = "Actor",
            LastName = "Teacher",
            Username = "actor",
            PasswordHash = "hash",
            Status = "Active",
            FailedLoginAttempts = 3,
            LockoutEndUtc = DateTime.UtcNow.AddMinutes(10)
        };
        var peer = new Teacher { TeacherId = 21, FirstName = "Peer", LastName = "Teacher", Username = "peer", PasswordHash = "hash", Status = "Active" };
        var admin = new Admin { Id = 30, Username = "admin-target", FullName = "Admin", PasswordHash = "hash", IsActive = true };
        db.AddRange(actor, peer, admin);
        await db.SaveChangesAsync();
        var controller = CreateController(db, isAdmin: false, teacherId: actor.TeacherId);

        await controller.UpdateTeacher(new Teacher { TeacherId = actor.TeacherId, Username = "changed", Status = "Inactive" }, "new-password");
        await controller.UnlockAccount(AccountRole.Teacher, actor.TeacherId);
        await controller.SetAccountActive(AccountRole.Teacher, actor.TeacherId, false);
        await controller.DeleteTeacher(actor.TeacherId);

        Assert.Equal("actor", actor.Username);
        Assert.Equal("Active", actor.Status);
        Assert.Equal(3, actor.FailedLoginAttempts);
        Assert.NotNull(actor.LockoutEndUtc);
        Assert.IsType<ForbidResult>(await controller.UnlockAccount(AccountRole.Admin, admin.Id));
        Assert.IsType<ForbidResult>(await controller.SetAccountActive(AccountRole.Admin, admin.Id, false));
        Assert.True(admin.IsActive);
    }

    [Fact]
    public async Task LastActiveTeacher_CannotBeDeactivatedOrDeleted()
    {
        using var db = GetDbContext();
        var teacher = new Teacher { TeacherId = 40, FirstName = "Last", LastName = "Teacher", Username = "last", PasswordHash = "hash", Status = "" };
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync();
        var controller = CreateController(db, isAdmin: false, teacherId: teacher.TeacherId);

        await controller.SetAccountActive(AccountRole.Teacher, teacher.TeacherId, false);
        Assert.Equal("", teacher.Status);

        await controller.UpdateTeacher(new Teacher { TeacherId = teacher.TeacherId, Username = teacher.Username, Status = "Inactive" }, null);
        Assert.Equal("", teacher.Status);

        await controller.DeleteTeacher(teacher.TeacherId);
        Assert.Equal("", teacher.Status);
    }

    [Fact]
    public async Task TeacherActor_AuditAndComputerHistoryUseTeacherIdentity()
    {
        using var db = GetDbContext();
        var actor = new Teacher { TeacherId = 50, FirstName = "Audit", LastName = "Teacher", Username = "audit-teacher", PasswordHash = "hash", Status = "Active" };
        var computer = new Computer { LaboratoryStation = "PC-AUDIT", Status = "Available" };
        db.AddRange(actor, computer);
        await db.SaveChangesAsync();
        var controller = CreateController(db, isAdmin: false, teacherId: actor.TeacherId);

        await controller.UpdateComputer(new Computer { ComputerId = computer.ComputerId, LaboratoryStation = computer.LaboratoryStation, Status = "Maintenance" });

        var audit = await db.AuditLogs.SingleAsync(log => log.Action == "UpdateComputer");
        Assert.Equal("Teacher", audit.UserType);
        Assert.Equal(actor.TeacherId, audit.UserId);
        var history = await db.ComputerStatusHistories.SingleAsync();
        Assert.Equal("Teacher", history.ChangedByType);
        Assert.Equal(actor.TeacherId, history.ChangedById);
    }

    [Fact]
    public async Task ChangePassword_VerifiesCurrentPasswordAndCreatesAudit()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<object>();
        var admin = new Admin
        {
            Id = 1,
            Username = "admin",
            FullName = "Admin",
            PasswordHash = hasher.HashPassword(new object(), "old-password")
        };
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        var result = await controller.ChangePassword(new PasswordChangeInput
        {
            CurrentPassword = "old-password",
            NewPassword = "new-password",
            ConfirmPassword = "new-password"
        });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(
            Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(new object(), admin.PasswordHash, "new-password"));
        Assert.NotNull(await db.AuditLogs.FirstOrDefaultAsync(log => log.Action == "PasswordChanged" && log.UserType == "Admin"));
    }
}
