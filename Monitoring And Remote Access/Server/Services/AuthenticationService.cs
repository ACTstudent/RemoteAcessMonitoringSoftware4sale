using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;

namespace Server.Services;

public class AuthenticationService : IAuthenticationService
{
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private readonly ApplicationDbContext _context;
    private readonly PasswordHasher<object> _hasher = new();

    public AuthenticationService(ApplicationDbContext context)
    {
        _context = context;
    }

    private bool VerifyPassword(string storedHash, string providedPassword)
    {
        if (string.IsNullOrEmpty(storedHash)) return false;
        try
        {
            return _hasher.VerifyHashedPassword(new object(), storedHash, providedPassword) == PasswordVerificationResult.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<LoginResult> LoginAsync(string username, string password, string pcName, string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return new LoginResult(AccountRole.None, null, null);
        }

        // 1. ADMIN LOGIN
        var admin = await _context.Admins.FirstOrDefaultAsync(a => a.Username == username);
        if (admin != null && admin.IsActive && !IsLocked(admin.LockoutEndUtc) && VerifyPassword(admin.PasswordHash, password))
        {
            ClearFailures(admin);
            await AuditAsync("Admin", admin.Id, "LoginSuccess", $"Admin {username} logged in from {ipAddress}", ipAddress);
            return new LoginResult(AccountRole.Admin, admin.Id, admin.FullName, admin.Username);
        }
        if (admin != null && admin.IsActive && !IsLocked(admin.LockoutEndUtc)) RecordFailure(admin);

        // 2. STUDENT LOGIN (Student takes priority if username exists in both Student & Teacher)
        var student = await _context.Students
            .FirstOrDefaultAsync(s => s.Username == username || s.StudentNumber == username);
        if (student != null && IsUsable(student.Status, student.LockoutEndUtc) && VerifyPassword(student.PasswordHash, password))
        {
            ClearFailures(student);
            var existingSession = await _context.LabSessions.FirstOrDefaultAsync(s => s.StudentId == student.Id && s.IsActive && s.Status != "Ended");
            if (existingSession != null)
            {
                if (!string.IsNullOrWhiteSpace(pcName) && !string.Equals(existingSession.PCName, pcName, StringComparison.OrdinalIgnoreCase))
                    return new LoginResult(AccountRole.Invalid, null, null);
                return new LoginResult(AccountRole.Student, student.Id, student.FullName, student.Username, student.StudentNumber);
            }
            if (!string.IsNullOrEmpty(student.Status) && !string.Equals(student.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                await AuditAsync("Student", student.Id, "LoginDenied",
                    $"Student {username} attempted login while account status is {student.Status}", ipAddress);
                return new LoginResult(AccountRole.Invalid, null, null);
            }

            var assignedPc = await _context.Computers.FirstOrDefaultAsync(c => c.AssignedTo == student.Id.ToString());
            if (assignedPc != null && !string.IsNullOrEmpty(pcName) && !string.Equals(assignedPc.LaboratoryStation, pcName, StringComparison.OrdinalIgnoreCase))
            {
                await AuditAsync("Student", student.Id, "LoginDenied",
                    $"Student {username} attempted from wrong station: {pcName} (needs {assignedPc.LaboratoryStation})", ipAddress);
                return new LoginResult(AccountRole.Invalid, null, null);
            }

            _context.LabSessions.Add(new LabSession
            {
                StudentId = student.Id,
                PCName = string.IsNullOrEmpty(pcName) ? "WebStation" : pcName,
                IPAddress = ipAddress,
                StartTime = DateTime.Now,
                Status = "Running",
                IsActive = true
            });
            await _context.SaveChangesAsync();

            await AuditAsync("Student", student.Id, "LoginSuccess", $"Student {username} logged in from {pcName} ({ipAddress})", ipAddress);
            return new LoginResult(AccountRole.Student, student.Id, student.FullName, student.Username, student.StudentNumber);
        }
        if (student != null && IsUsable(student.Status, student.LockoutEndUtc)) RecordFailure(student);

        // 3. TEACHER LOGIN
        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.Username == username);
        if (teacher != null && IsUsable(teacher.Status, teacher.LockoutEndUtc) && VerifyPassword(teacher.PasswordHash, password))
        {
            ClearFailures(teacher);
            if (teacher.Status != "Active" && !string.IsNullOrEmpty(teacher.Status))
            {
                await AuditAsync("Teacher", teacher.TeacherId, "LoginDenied",
                    $"Teacher {username} attempted login but account is inactive", ipAddress);
                return new LoginResult(AccountRole.Invalid, null, null);
            }

            string teacherDisplayName = !string.IsNullOrWhiteSpace(teacher.FirstName) || !string.IsNullOrWhiteSpace(teacher.LastName)
                ? $"{teacher.FirstName} {teacher.LastName}".Trim()
                : teacher.Username;

            await AuditAsync("Teacher", teacher.TeacherId, "LoginSuccess", $"Teacher {username} logged in from {ipAddress}", ipAddress);
            return new LoginResult(AccountRole.Teacher, teacher.TeacherId, teacherDisplayName, teacher.Username);
        }
        if (teacher != null && IsUsable(teacher.Status, teacher.LockoutEndUtc)) RecordFailure(teacher);

        await AuditAsync("System", null, "LoginFailed", $"Failed login attempt for '{username}' from {ipAddress}", ipAddress);
        return new LoginResult(AccountRole.Invalid, null, null);
    }

    private static bool IsLocked(DateTime? lockoutEndUtc) => lockoutEndUtc.HasValue && lockoutEndUtc > DateTime.UtcNow;
    private static bool IsUsable(string status, DateTime? lockoutEndUtc) =>
        (string.IsNullOrWhiteSpace(status) || string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)) && !IsLocked(lockoutEndUtc);
    private static void ClearFailures(Admin account) => (account.FailedLoginAttempts, account.LockoutEndUtc) = (0, null);
    private static void ClearFailures(Student account) => (account.FailedLoginAttempts, account.LockoutEndUtc) = (0, null);
    private static void ClearFailures(Teacher account) => (account.FailedLoginAttempts, account.LockoutEndUtc) = (0, null);

    private static (int Attempts, DateTime? LockoutEndUtc) NextFailure(int currentAttempts)
    {
        var attempts = currentAttempts + 1;
        return attempts >= MaxFailedLoginAttempts ? (0, DateTime.UtcNow.Add(LockoutDuration)) : (attempts, null);
    }

    private static void RecordFailure(Admin account) =>
        (account.FailedLoginAttempts, account.LockoutEndUtc) = NextFailure(account.FailedLoginAttempts);
    private static void RecordFailure(Student account) =>
        (account.FailedLoginAttempts, account.LockoutEndUtc) = NextFailure(account.FailedLoginAttempts);
    private static void RecordFailure(Teacher account) =>
        (account.FailedLoginAttempts, account.LockoutEndUtc) = NextFailure(account.FailedLoginAttempts);

    private async Task AuditAsync(string userType, int? userId, string action, string details, string ipAddress)
    {
        try
        {
            _context.AuditLogs.Add(new AuditLog
            {
                UserType = userType,
                UserId = userId,
                Action = action,
                Details = details,
                IpAddress = ipAddress,
                Timestamp = DateTime.Now
            });
            await _context.SaveChangesAsync();
        }
        catch
        {
            // Auditing must never block login flow
        }
    }

    public async Task LogoutAsync(int? studentId)
    {
        if (!studentId.HasValue)
        {
            return;
        }

        var activeSessions = _context.LabSessions.Where(s => s.StudentId == studentId.Value && s.IsActive);
        foreach (var session in activeSessions)
        {
            session.IsActive = false;
            session.Status = "Ended";
            session.EndTime = DateTime.Now;
        }

        await _context.SaveChangesAsync();
    }

    public async Task<bool> ChangeStudentPasswordAsync(int studentId, string currentPassword, string newPassword)
    {
        var student = await _context.Students.FindAsync(studentId);
        if (student == null || !VerifyPassword(student.PasswordHash, currentPassword) || string.IsNullOrWhiteSpace(newPassword)) return false;
        student.PasswordHash = _hasher.HashPassword(new object(), newPassword.Trim());
        await _context.SaveChangesAsync();
        return true;
    }
}
