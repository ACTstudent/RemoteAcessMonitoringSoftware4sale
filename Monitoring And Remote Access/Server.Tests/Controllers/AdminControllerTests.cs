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
    public async Task Classes_CRUD_WorksFlawlessly()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        // 1. Create Class
        var createResult = await controller.CreateClass(new Class
        {
            ClassName = "Grade 6 - Sampaguita",
            Section = "Section A",
            Subject = "Computer Education",
            GradeLevel = "Grade 6",
            Schedule = "M/W 8:00 AM"
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
}
