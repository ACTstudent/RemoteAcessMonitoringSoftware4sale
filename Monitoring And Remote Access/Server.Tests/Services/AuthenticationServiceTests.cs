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
            PasswordHash = hasher.HashPassword(new object(), "pass123")
        });
        context.Computers.Add(new Computer { LaboratoryStation = "PC01", AssignedTo = "1", Status = "Assigned" });
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
            PasswordHash = hasher.HashPassword(new object(), "correct")
        });
        await context.SaveChangesAsync();

        var service = new AuthenticationService(context);
        var result = await service.LoginAsync("student_wrong", "wrongpass", "PC01", "127.0.0.1");
        Assert.Equal(AccountRole.Invalid, result.Role);
    }

    [Fact]
    public async Task LoginAsync_FiveWrongPasswords_LocksStudentAccount()
    {
        var context = CreateContext();
        var hasher = new PasswordHasher<object>();
        var student = new Student
        {
            Id = 11,
            StudentNumber = "STU011",
            Username = "lockout_student",
            PasswordHash = hasher.HashPassword(new object(), "correct")
        };
        context.Students.Add(student);
        await context.SaveChangesAsync();

        var service = new AuthenticationService(context);
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(AccountRole.Invalid, (await service.LoginAsync("lockout_student", "wrong", "PC01", "127.0.0.1")).Role);

        Assert.NotNull(student.LockoutEndUtc);
        Assert.True(student.LockoutEndUtc > DateTime.UtcNow);
        Assert.Equal(AccountRole.Invalid, (await service.LoginAsync("lockout_student", "correct", "PC01", "127.0.0.1")).Role);
    }

    [Fact]
    public async Task LoginAsync_PlaintextStoredPassword_IsRejected()
    {
        var context = CreateContext();
        context.Students.Add(new Student
        {
            Id = 2,
            StudentNumber = "STU002",
            Username = "plaintext_student",
            PasswordHash = "plain-password"
        });
        await context.SaveChangesAsync();

        var service = new AuthenticationService(context);
        var result = await service.LoginAsync("plaintext_student", "plain-password", "PC01", "127.0.0.1");

        Assert.Equal(AccountRole.Invalid, result.Role);
    }

    [Fact]
    public async Task LoginAsync_StudentNumber_IsAcceptedAsLoginName()
    {
        var context = CreateContext();
        var hasher = new PasswordHasher<object>();
        context.Students.Add(new Student
        {
            Id = 3,
            StudentNumber = "STU003",
            Username = "student_three",
            PasswordHash = hasher.HashPassword(new object(), "pass123")
        });
        context.Computers.Add(new Computer { LaboratoryStation = "PC01", AssignedTo = "3", Status = "Assigned" });
        await context.SaveChangesAsync();

        var service = new AuthenticationService(context);
        var result = await service.LoginAsync("STU003", "pass123", "PC01", "127.0.0.1");

        Assert.Equal(AccountRole.Student, result.Role);
        Assert.Equal("student_three", result.LoginName);
        Assert.Equal("STU003", result.StudentNumber);
    }

    [Fact]
    public async Task LoginAsync_StudentChangesStation_MovesAssignmentWhenSafe()
    {
        var context = CreateContext();
        var hasher = new PasswordHasher<object>();
        var student = new Student
        {
            Id = 5,
            StudentNumber = "STU005",
            Username = "bound_student",
            PasswordHash = hasher.HashPassword(new object(), "pass")
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
        Assert.Equal(AccountRole.Student, result.Role);
        Assert.Null((await context.Computers.SingleAsync(c => c.LaboratoryStation == "LAB-PC-12")).AssignedTo);
        Assert.Equal("5", (await context.Computers.SingleAsync(c => c.LaboratoryStation == "LAB-PC-99")).AssignedTo);
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
            PasswordHash = hasher.HashPassword(new object(), "test")
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
            PasswordHash = hasher.HashPassword(new object(), "pass"),
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
            PasswordHash = hasher.HashPassword(new object(), "pass"),
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
            PasswordHash = hasher.HashPassword(new object(), "adminpass"),
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
            PasswordHash = hasher.HashPassword(new object(), "pass")
        });
        context.Teachers.Add(new Teacher
        {
            TeacherId = 1,
            FirstName = "T",
            LastName = "T",
            Email = "",
            Username = "sameuser",
            PasswordHash = hasher.HashPassword(new object(), "pass"),
            ContactNumber = "",
            Status = "Active"
        });
        await context.SaveChangesAsync();

        var service = new AuthenticationService(context);
        var result = await service.LoginAsync("sameuser", "pass", "", "127.0.0.1");
        Assert.Equal(AccountRole.Student, result.Role);
    }

    [Fact]
    public async Task LoginAsync_StaleAssignmentWithoutActiveSession_CanBeReassigned()
    {
        var context = CreateContext();
        var hasher = new PasswordHasher<object>();
        context.Students.Add(new Student
        {
            Id = 20,
            StudentNumber = "STU020",
            Username = "student_twenty",
            PasswordHash = hasher.HashPassword(new object(), "pass")
        });
        context.Computers.Add(new Computer
        {
            LaboratoryStation = "PC-TAKEN",
            AssignedTo = "21",
            Status = "Assigned"
        });
        await context.SaveChangesAsync();

        var result = await new AuthenticationService(context)
            .LoginAsync("student_twenty", "pass", "PC-TAKEN", "127.0.0.1");

        Assert.Equal(AccountRole.Student, result.Role);
        Assert.Equal("20", (await context.Computers.SingleAsync()).AssignedTo);
        Assert.Single(await context.LabSessions.ToListAsync());
    }

    [Fact]
    public async Task LoginAsync_FirstUseAutomaticallyCreatesWorkstationAndSession()
    {
        var context = CreateContext();
        var hasher = new PasswordHasher<object>();
        context.Students.Add(new Student
        {
            Id = 25,
            StudentNumber = "STU025",
            Username = "first_use_student",
            PasswordHash = hasher.HashPassword(new object(), "pass"),
            Status = "Active"
        });
        await context.SaveChangesAsync();

        var result = await new AuthenticationService(context)
            .LoginAsync("first_use_student", "pass", "LAB2-PC26", "127.0.0.1");

        Assert.Equal(AccountRole.Student, result.Role);
        Assert.Equal("25", (await context.Computers.SingleAsync()).AssignedTo);
        Assert.Equal("Running", (await context.LabSessions.SingleAsync()).Status);
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

    [Fact]
    public async Task ChangeTeacherPasswordAsync_VerifiesCurrentPasswordMinimumLengthAndAudits()
    {
        var context = CreateContext();
        var hasher = new PasswordHasher<object>();
        var teacher = new Teacher
        {
            TeacherId = 15,
            FirstName = "Password",
            LastName = "Teacher",
            Username = "password-teacher",
            PasswordHash = hasher.HashPassword(new object(), "old-password"),
            Status = "Active"
        };
        context.Teachers.Add(teacher);
        await context.SaveChangesAsync();
        var service = new AuthenticationService(context);

        Assert.False(await service.ChangeTeacherPasswordAsync(teacher.TeacherId, "wrong-password", "valid-new-password", "127.0.0.1"));
        Assert.False(await service.ChangeTeacherPasswordAsync(teacher.TeacherId, "old-password", "short", "127.0.0.1"));
        Assert.True(await service.ChangeTeacherPasswordAsync(teacher.TeacherId, "old-password", "valid-new-password", "127.0.0.1"));

        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(new object(), teacher.PasswordHash, "valid-new-password"));
        Assert.Equal(2, await context.AuditLogs.CountAsync(log => log.UserType == "Teacher" && log.Action == "PasswordChangeFailed"));
        Assert.Single(await context.AuditLogs.Where(log => log.UserType == "Teacher" && log.Action == "PasswordChanged").ToListAsync());
    }

    [Fact]
    public async Task ChangeAdminPasswordAsync_VerifiesCurrentPasswordAndAudits()
    {
        var context = CreateContext();
        var hasher = new PasswordHasher<object>();
        var admin = new Admin
        {
            Id = 8,
            Username = "password-admin",
            FullName = "Password Admin",
            PasswordHash = hasher.HashPassword(new object(), "old-password")
        };
        context.Admins.Add(admin);
        await context.SaveChangesAsync();
        var service = new AuthenticationService(context);

        Assert.True(await service.ChangeAdminPasswordAsync(admin.Id, "old-password", "new-password", "10.0.0.1"));

        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(new object(), admin.PasswordHash, "new-password"));
        var audit = await context.AuditLogs.SingleAsync(log => log.UserType == "Admin" && log.Action == "PasswordChanged");
        Assert.Equal(admin.Id, audit.UserId);
        Assert.Equal("10.0.0.1", audit.IpAddress);
    }
}
