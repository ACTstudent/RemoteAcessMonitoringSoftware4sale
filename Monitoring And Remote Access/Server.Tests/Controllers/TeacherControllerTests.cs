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
    [Fact]
    public void TeacherController_DoesNotExposeGlobalPolicyMutationActions()
    {
        var actionNames = typeof(TeacherController).GetMethods().Select(method => method.Name).ToHashSet();

        Assert.DoesNotContain("CreateBlacklist", actionNames);
        Assert.DoesNotContain("UpdateBlacklist", actionNames);
        Assert.DoesNotContain("DeleteBlacklist", actionNames);
        Assert.DoesNotContain("CreateApplicationCategory", actionNames);
        Assert.DoesNotContain("CreateWebsiteCategory", actionNames);
    }

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
        clientsMock.Setup(c => c.User(It.IsAny<string>())).Returns(proxyMock.Object);
        clientsMock.Setup(c => c.Users(It.IsAny<IReadOnlyList<string>>())).Returns(proxyMock.Object);
        hubMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        var sessionManager = new SessionManagerService(hubMock.Object);
        var lifecycle = new LabSessionLifecycleService(context, hubMock.Object);
        var controller = new TeacherController(context, sessionManager, lifecycle);
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
    public async Task OpenAlertCount_UsesPersistedDistinctGroupsAndClassMembership()
    {
        using var db = GetDbContext();
        var student = new Student { StudentNumber = "S-ALERT", FullName = "Student", Username = "alert-student", PasswordHash = "hash", AdviserId = 2 };
        var cls = new Class { ClassName = "Teacher class", TeacherId = 1 };
        db.AddRange(student, cls);
        await db.SaveChangesAsync();
        db.ClassStudents.Add(new ClassStudent { ClassId = cls.ClassId, StudentId = student.Id });
        db.MonitoringAlerts.AddRange(
            new MonitoringAlert { StudentId = student.StudentNumber, PcName = "PC-01", Title = "Alert", Message = "One", GroupKey = "same" },
            new MonitoringAlert { StudentId = student.StudentNumber, PcName = "PC-01", Title = "Alert", Message = "Two", GroupKey = "same" },
            new MonitoringAlert { StudentId = student.StudentNumber, PcName = "PC-01", Title = "Old", Message = "Acknowledged", GroupKey = "old", IsAcknowledged = true });
        await db.SaveChangesAsync();

        var result = Assert.IsType<JsonResult>(await CreateController(db).OpenAlertCount());
        var count = result.Value!.GetType().GetProperty("count")!.GetValue(result.Value);

        Assert.Equal(1, count);
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
    public async Task BrowserMonitoringHistory_UsesStudentNumberAndScopesTeacherRoster()
    {
        using var db = GetDbContext();
        db.Students.AddRange(
            new Student { Id = 10, StudentNumber = "STU-A10", FullName = "Allowed", Username = "allowed", PasswordHash = "h", AdviserId = 1 },
            new Student { Id = 20, StudentNumber = "STU-B20", FullName = "Denied", Username = "denied", PasswordHash = "h", AdviserId = 2 });
        db.BrowserMonitoringRecords.AddRange(
            new BrowserMonitoringRecord { StudentId = "STU-A10", ConnectionId = "a", PcName = "PC-01", Browser = "chrome", Mode = Shared.Contracts.BrowserMonitoringMode.ManagedProtocol, Timestamp = DateTime.UtcNow },
            new BrowserMonitoringRecord { StudentId = "STU-B20", ConnectionId = "b", PcName = "PC-02", Browser = "brave", Mode = Shared.Contracts.BrowserMonitoringMode.ManagedProtocol, Timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = Assert.IsType<ViewResult>(await CreateController(db).BrowserMonitoringHistory());
        var records = Assert.IsAssignableFrom<IReadOnlyList<BrowserMonitoringRecord>>(result.Model);

        var record = Assert.Single(records);
        Assert.Equal("STU-A10", record.StudentId);
    }

    [Fact]
    public async Task Session_StartPauseEnd_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        var teacher = new Teacher
        {
            TeacherId = 1,
            FirstName = "Maria",
            LastName = "Santos",
            Username = "msantos",
            PasswordHash = "hash",
            Status = "Active"
        };
        var cls = new Class { ClassName = "Grade 6 - Test", TeacherId = 1 };
        db.Teachers.Add(teacher);
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        var student = new Student { FullName = "Test Student", Username = "teststudent", PasswordHash = "hash", ClassId = cls.ClassId };
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
        Assert.Equal("Assigned", freedComp?.Status);
        Assert.Equal(student.Id.ToString(), freedComp?.AssignedTo);
    }

    [Fact]
    public async Task Teacher_StudentCRUD_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var teacherAccount = new Teacher
        {
            TeacherId = 1,
            FirstName = "Maria",
            LastName = "Santos",
            Username = "msantos",
            PasswordHash = "hash",
            Status = "Active"
        };
        var cls = new Class { ClassName = "Grade 7 - Rizal", TeacherId = 1 };
        db.Teachers.Add(teacherAccount);
        db.Classes.Add(cls);
        await db.SaveChangesAsync();

        // 1. Create Student
        var createResult = await controller.CreateStudent(new Student
        {
            StudentNumber = "STU-101",
            FullName = "Jose Rizal",
            Username = "jrizal",
            PasswordHash = "pass123"
        }, cls.ClassId);
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
        var preservedStudent = await db.Students.FindAsync(student.Id);
        Assert.NotNull(preservedStudent);
        Assert.Null(preservedStudent?.ClassId);
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
        db.Teachers.Add(new Teacher
        {
            TeacherId = 1,
            FirstName = "Maria",
            LastName = "Santos",
            Username = "msantos",
            PasswordHash = "hash",
            Status = "Active"
        });
        await db.SaveChangesAsync();

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
            new List<string> { "pass123", "pass234" }
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

        var student = new Student
        {
            StudentNumber = "STU-COMP-1",
            FullName = "Accessible Student",
            Username = "accessible-computer-student",
            PasswordHash = "hash",
            AdviserId = 1
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var comp = new Computer { LaboratoryStation = "Station-05", Status = "Available", AssignedTo = student.Id.ToString() };
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
        var cls = new Class { ClassName = "Class A", TeacherId = 1 };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        var student = new Student { StudentNumber = "STU-1", Username = "student-1", FullName = "Student One", ClassId = cls.ClassId };
        db.Students.Add(student);
        await db.SaveChangesAsync();

        var result = await controller.SendNotification("Warning", "Time Up", "Please save your work.");
        Assert.IsType<JsonResult>(result);

        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Title == "Time Up");
        Assert.NotNull(notification);
        Assert.Equal(student.Id, notification.StudentId);
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

        db.Teachers.Add(new Teacher
        {
            TeacherId = 1,
            FirstName = "Maria",
            LastName = "Santos",
            Username = "msantos",
            PasswordHash = "hash",
            Status = "Active"
        });
        var cls = new Class { ClassName = "Grade 10 - Acacia", TeacherId = 1 };
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

        db.Teachers.Add(new Teacher
        {
            TeacherId = 1,
            FirstName = "Maria",
            LastName = "Santos",
            Username = "msantos",
            PasswordHash = "hash",
            Status = "Active"
        });
        var cls = new Class { ClassName = "Grade 11 - STEM A", TeacherId = 1 };
        var student = new Student { FullName = "Apolinario Mabini", Username = "amabini", PasswordHash = "hash", AdviserId = 1 };
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

    [Fact]
    public async Task Students_SearchReturnsOnlyAdvisedOrActiveClassStudents()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var activeClass = new Class { ClassName = "Active", TeacherId = 1, Status = "Active" };
        var archivedClass = new Class { ClassName = "Archived", TeacherId = 1, Status = "Archived", IsArchived = true };
        var foreignClass = new Class { ClassName = "Foreign", TeacherId = 2, Status = "Active" };
        db.Classes.AddRange(activeClass, archivedClass, foreignClass);
        await db.SaveChangesAsync();
        db.Students.AddRange(
            new Student { StudentNumber = "S-ADV", FirstName = "Alpha", LastName = "Advised", Username = "alpha-advised", PasswordHash = "hash", AdviserId = 1 },
            new Student { StudentNumber = "S-CLASS", FirstName = "Alpha", LastName = "Class", Username = "alpha-class", PasswordHash = "hash", ClassId = activeClass.ClassId },
            new Student { StudentNumber = "S-ARCH", FirstName = "Alpha", LastName = "Archived", Username = "alpha-archived", PasswordHash = "hash", ClassId = archivedClass.ClassId },
            new Student { StudentNumber = "S-FOREIGN", FirstName = "Alpha", LastName = "Foreign", Username = "alpha-foreign", PasswordHash = "hash", ClassId = foreignClass.ClassId });
        await db.SaveChangesAsync();

        var result = Assert.IsType<ViewResult>(await controller.Students("alpha"));
        var students = Assert.IsAssignableFrom<IEnumerable<Student>>(result.Model).ToList();

        Assert.Equal(2, students.Count);
        Assert.Contains(students, student => student.Username == "alpha-advised");
        Assert.Contains(students, student => student.Username == "alpha-class");
        Assert.DoesNotContain(students, student => student.Username == "alpha-archived");
        Assert.DoesNotContain(students, student => student.Username == "alpha-foreign");
    }

    [Fact]
    public async Task StudentMutations_InaccessibleStudentReturnNotFoundWithoutChanges()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var foreignStudent = new Student
        {
            StudentNumber = "S-FOREIGN",
            FullName = "Foreign Student",
            Username = "foreign-student",
            PasswordHash = "hash",
            AdviserId = 2
        };
        db.Students.Add(foreignStudent);
        await db.SaveChangesAsync();

        var update = await controller.UpdateStudent(new Student
        {
            Id = foreignStudent.Id,
            StudentNumber = "CHANGED",
            FullName = "Changed Name",
            Username = "changed-user"
        }, null);
        var delete = await controller.DeleteStudent(foreignStudent.Id);

        Assert.IsType<NotFoundResult>(update);
        Assert.IsType<NotFoundResult>(delete);
        var unchanged = await db.Students.FindAsync(foreignStudent.Id);
        Assert.Equal("S-FOREIGN", unchanged?.StudentNumber);
        Assert.Equal("foreign-student", unchanged?.Username);
        Assert.Equal(2, unchanged?.AdviserId);
    }

    [Fact]
    public async Task UpdateStudent_AdvisedStudentCanBeUpdated()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var student = new Student
        {
            StudentNumber = "S-ADVISED",
            FullName = "Advised Student",
            Username = "advised-student",
            PasswordHash = "hash",
            AdviserId = 1
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();

        var result = await controller.UpdateStudent(new Student
        {
            Id = student.Id,
            StudentNumber = student.StudentNumber,
            FullName = "Updated Student",
            Username = student.Username
        }, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Updated Student", (await db.Students.FindAsync(student.Id))?.FullName);
    }

    [Fact]
    public async Task Computers_AndUpdatesAreLimitedToAccessibleStudents()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var accessibleStudent = new Student { StudentNumber = "S-1", FullName = "Own Student", Username = "own-student", PasswordHash = "hash", AdviserId = 1 };
        var foreignStudent = new Student { StudentNumber = "S-2", FullName = "Other Student", Username = "other-student", PasswordHash = "hash", AdviserId = 2 };
        db.Students.AddRange(accessibleStudent, foreignStudent);
        await db.SaveChangesAsync();
        var accessibleComputer = new Computer { LaboratoryStation = "OWN-PC", Status = "Available", AssignedTo = accessibleStudent.Id.ToString() };
        var foreignComputer = new Computer { LaboratoryStation = "OTHER-PC", Status = "Available", AssignedTo = foreignStudent.Id.ToString() };
        db.Computers.AddRange(accessibleComputer, foreignComputer);
        await db.SaveChangesAsync();

        var listResult = Assert.IsType<ViewResult>(await controller.Computers());
        var computers = Assert.IsAssignableFrom<IEnumerable<Computer>>(listResult.Model).ToList();
        Assert.Single(computers);
        Assert.Equal(accessibleComputer.ComputerId, computers[0].ComputerId);

        var denied = await controller.UpdateComputer(new Computer
        {
            ComputerId = foreignComputer.ComputerId,
            LaboratoryStation = "HACKED",
            Status = "Maintenance"
        });
        Assert.IsType<NotFoundResult>(denied);
        Assert.Equal("OTHER-PC", (await db.Computers.FindAsync(foreignComputer.ComputerId))?.LaboratoryStation);

        var saved = await controller.UpdateComputer(new Computer
        {
            ComputerId = accessibleComputer.ComputerId,
            LaboratoryStation = "OWN-PC-EDITED",
            Status = "Maintenance",
            AssignedTo = foreignStudent.Id.ToString()
        });
        Assert.IsType<RedirectToActionResult>(saved);
        var updated = await db.Computers.FindAsync(accessibleComputer.ComputerId);
        Assert.Equal("OWN-PC-EDITED", updated?.LaboratoryStation);
        Assert.Equal("Maintenance", updated?.Status);
        Assert.Equal(accessibleStudent.Id.ToString(), updated?.AssignedTo);
    }

    [Fact]
    public async Task EnrollStudent_InaccessibleStudentReturnsNotFound()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var cls = new Class { ClassName = "Own Class", TeacherId = 1 };
        var student = new Student { StudentNumber = "S-3", FullName = "Unrelated Student", Username = "unrelated", PasswordHash = "hash" };
        db.Classes.Add(cls);
        db.Students.Add(student);
        await db.SaveChangesAsync();

        var result = await controller.EnrollStudent(cls.ClassId, student.Id);

        Assert.IsType<NotFoundResult>(result);
        Assert.Null((await db.Students.FindAsync(student.Id))?.ClassId);
    }
}
