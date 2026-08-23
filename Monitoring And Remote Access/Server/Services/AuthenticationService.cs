using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;

namespace Server.Services;

public class AuthenticationService : IAuthenticationService
{
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
        if (admin != null && VerifyPassword(admin.PasswordHash, password))
        {
            await AuditAsync("Admin", admin.Id, "LoginSuccess", $"Admin {username} logged in from {ipAddress}", ipAddress);
            return new LoginResult(AccountRole.Admin, admin.Id, admin.FullName, admin.Username);
        }

        // 2. STUDENT LOGIN (Student takes priority if username exists in both Student & Teacher)
        var student = await _context.Students
            .FirstOrDefaultAsync(s => s.Username == username || s.StudentNumber == username);
        if (student != null && VerifyPassword(student.PasswordHash, password))
        {
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

        // 3. TEACHER LOGIN
        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.Username == username);
        if (teacher != null && VerifyPassword(teacher.PasswordHash, password))
        {
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

        await AuditAsync("System", null, "LoginFailed", $"Failed login attempt for '{username}' from {ipAddress}", ipAddress);
        return new LoginResult(AccountRole.Invalid, null, null);
    }

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
}
