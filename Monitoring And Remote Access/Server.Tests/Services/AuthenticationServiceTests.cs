using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Tests.Services;

public class AuthenticationServiceTests
{
    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task LoginAsync_EmptyUsername_ReturnsNone()
    {
        var context = CreateContext();
        var service = new AuthenticationService(context);
        var result = await service.LoginAsync("", "password", "PC01", "127.0.0.1");
        Assert.Equal(AccountRole.None, result.Role);
    }

    [Fact]
    public async Task LoginAsync_EmptyPassword_ReturnsNone()
    {
        var context = CreateContext();
        var service = new AuthenticationService(context);
        var result = await service.LoginAsync("user", "", "PC01", "127.0.0.1");
        Assert.Equal(AccountRole.None, result.Role);
    }

    [Fact]
    public async Task LoginAsync_UnknownUser_ReturnsInvalid()
    {
        var context = CreateContext();
        var service = new AuthenticationService(context);
        var result = await service.LoginAsync("nobody", "pass", "PC01", "127.0.0.1");
        Assert.Equal(AccountRole.Invalid, result.Role);
        Assert.Null(result.AccountId);
    }

    [Fact]
    public async Task LoginAsync_ValidStudent_ReturnsStudent()
    {
        var context = CreateContext();
        var hasher = new PasswordHasher<object>();
        context.Students.Add(new Student
        {
            Id = 1,
            StudentNumber = "S001",
            FullName = "Test Student",
            Username = "student_test",
            PasswordHash = hasher.HashPassword(null, "pass123")
        });
        await context.SaveChangesAsync();

        var service = new AuthenticationService(context);
        var result = await service.LoginAsync("student_test", "pass123", "PC01", "127.0.0.1");

        Assert.Equal(AccountRole.Student, result.Role);
        Assert.Equal(1, result.AccountId);
        Assert.Equal("Test Student", result.DisplayName);
    }

    [Fact]
    public async Task LoginAsync_StudentWrongPassword_ReturnsInvalid()
    {
        var context = CreateContext();
        var hasher = new PasswordHasher<object>();
        context.Students.Add(new Student
        {
            Id = 1,
            StudentNumber = "STU001",
            Username = "student_wrong",
            PasswordHash = hasher.HashPassword(null, "correct")
        });
        await context.SaveChangesAsync();

        var service = new AuthenticationService(context);
        var result = await service.LoginAsync("student_wrong", "wrongpass", "PC01", "127.0.0.1");
        Assert.Equal(AccountRole.Invalid, result.Role);
    }

    [Fact]
    public async Task LoginAsync_StudentAssignedToWrongStation_ReturnsInvalid()
    {
        var context = CreateContext();
        var hasher = new PasswordHasher<object>();
        var student = new Student
        {
            Id = 5,
            StudentNumber = "STU005",
            Username = "bound_student",
            PasswordHash = hasher.HashPassword(null, "pass")
        };
        context.Students.Add(student);
        context.Computers.Add(new Computer
        {
            ComputerId = 1,
            LaboratoryStation = "LAB-PC-12",
            AssignedTo = "5"
        });
        await context.SaveChangesAsync();

        var service = new AuthenticationService(context);
        var result = await service.LoginAsync("bound_student", "pass", "LAB-PC-99", "192.168.1.5");
        Assert.Equal(AccountRole.Invalid, result.Role);
    }

    [Fact]
    public async Task LoginAsync_StudentAssignedToCorrectStation_ReturnsStudent()
    {
        var context = CreateContext();
        var hasher = new PasswordHasher<object>();
        context.Students.Add(new Student
        {
            Id = 7,
            StudentNumber = "STU007",
            Username = "station_student",
            PasswordHash = hasher.HashPassword(null, "test")
        });
        context.Computers.Add(new Computer
        {
            ComputerId = 1,
            LaboratoryStation = "LAB-PC-07",
            AssignedTo = "7"
        });
        await context.SaveChangesAsync();

        var service = new AuthenticationService(context);
        var result = await service.LoginAsync("station_student", "test", "LAB-PC-07", "127.0.0.1");
        Assert.Equal(AccountRole.Student, result.Role);
    }

    [Fact]
    public async Task LoginAsync_ValidTeacher_ReturnsTeacher()
    {
        var context = CreateContext();
        var hasher = new PasswordHasher<object>();
        context.Teachers.Add(new Teacher
        {
            TeacherId = 1,
            FirstName = "Jane",
            LastName = "Doe",
            Email = "",
            Username = "teacher_j",
            PasswordHash = hasher.HashPassword(null, "pass"),
            ContactNumber = "",
            Status = "Active"
        });
        await context.SaveChangesAsync();

        var service = new AuthenticationService(context);
        var result = await service.LoginAsync("teacher_j", "pass", "PC", "127.0.0.1");

        Assert.Equal(AccountRole.Teacher, result.Role);
        Assert.Equal(1, result.AccountId);
        Assert.Equal("Jane Doe", result.DisplayName);
    }

    [Fact]
    public async Task LoginAsync_InactiveTeacher_ReturnsInvalid()
    {
        var context = CreateContext();
        var hasher = new PasswordHasher<object>();
        context.Teachers.Add(new Teacher
        {
            TeacherId = 2,
            FirstName = "Inactive",
            LastName = "Teacher",
            Email = "",
            Username = "teach_inactive",
            PasswordHash = hasher.HashPassword(null, "pass"),
            ContactNumber = "",
            Status = "Inactive"
        });
        await context.SaveChangesAsync();

        var service = new AuthenticationService(context);
        var result = await service.LoginAsync("teach_inactive", "pass", "PC", "127.0.0.1");
        Assert.Equal(AccountRole.Invalid, result.Role);
    }

    [Fact]
    public async Task LoginAsync_ValidAdmin_ReturnsAdmin()
    {
        var context = CreateContext();
        var hasher = new PasswordHasher<object>();
        context.Admins.Add(new Admin
        {
            Id = 1,
            Username = "admin_test",
            PasswordHash = hasher.HashPassword(null, "adminpass"),
            FullName = "Big Admin"
        });
        await context.SaveChangesAsync();

        var service = new AuthenticationService(context);
        var result = await service.LoginAsync("admin_test", "adminpass", "PC", "127.0.0.1");

        Assert.Equal(AccountRole.Admin, result.Role);
        Assert.Equal(1, result.AccountId);
        Assert.Equal("Big Admin", result.DisplayName);
    }

    [Fact]
    public async Task LoginAsync_StudentPriorityOverTeacher_ReturnsStudent()
    {
        var context = CreateContext();
        var hasher = new PasswordHasher<object>();
        context.Students.Add(new Student
        {
            Id = 1,
            StudentNumber = "STU001",
            Username = "sameuser",
            PasswordHash = hasher.HashPassword(null, "pass")
        });
        context.Teachers.Add(new Teacher
        {
            TeacherId = 1,
            FirstName = "T",
            LastName = "T",
            Email = "",
            Username = "sameuser",
            PasswordHash = hasher.HashPassword(null, "pass"),
            ContactNumber = "",
            Status = "Active"
        });
        await context.SaveChangesAsync();

        var service = new AuthenticationService(context);
        var result = await service.LoginAsync("sameuser", "pass", "PC1", "127.0.0.1");
        Assert.Equal(AccountRole.Student, result.Role);
    }

    [Fact]
    public async Task LogoutAsync_WithStudentId_EndsActiveSessions()
    {
        var context = CreateContext();
        context.LabSessions.Add(new LabSession
        {
            Id = 1,
            StudentId = 10,
            PCName = "PC1",
            IPAddress = "127.0.0.1",
            Status = "Running",
            IsActive = true,
            StartTime = DateTime.Now.AddMinutes(-5)
        });
        context.LabSessions.Add(new LabSession
        {
            Id = 2,
            StudentId = 10,
            PCName = "PC2",
            IPAddress = "127.0.0.2",
            Status = "Running",
            IsActive = true,
            StartTime = DateTime.Now.AddMinutes(-2)
        });
        await context.SaveChangesAsync();

        var service = new AuthenticationService(context);
        await service.LogoutAsync(10);

        var sessions = await context.LabSessions.Where(s => s.StudentId == 10).ToListAsync();
        Assert.All(sessions, s => Assert.False(s.IsActive));
        Assert.All(sessions, s => Assert.Equal("Ended", s.Status));
        Assert.All(sessions, s => Assert.NotNull(s.EndTime));
    }

    [Fact]
    public async Task LogoutAsync_NullStudentId_DoesNothing()
    {
        var context = CreateContext();
        var service = new AuthenticationService(context);
        await service.LogoutAsync(null);
        Assert.Empty(await context.LabSessions.ToListAsync());
    }

    [Fact]
    public async Task LoginAsync_AuditLogCreatedForFailedLogin()
    {
        var context = CreateContext();
        var service = new AuthenticationService(context);
        await service.LoginAsync("ghost", "bad", "PC", "127.0.0.1");

        var audit = await context.AuditLogs.FirstOrDefaultAsync(a => a.Action == "LoginFailed");
        Assert.NotNull(audit);
        Assert.Equal("System", audit.UserType);
    }
}