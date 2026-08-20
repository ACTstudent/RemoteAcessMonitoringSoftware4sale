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

    public async Task<LoginResult> LoginAsync(string username, string password, string pcName, string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return new LoginResult(AccountRole.None, null, null);
        }

        // Students can ONLY log in. There is no registration flow.
        var student = await _context.Students
            .FirstOrDefaultAsync(s => s.Username == username);

        if (student != null && _hasher.VerifyHashedPassword(null, student.PasswordHash, password) == PasswordVerificationResult.Success)
        {
            var assignedPc = await _context.Computers.FirstOrDefaultAsync(c => c.AssignedTo == student.Id.ToString());
            if (assignedPc != null && !string.Equals(assignedPc.LaboratoryStation, pcName, StringComparison.OrdinalIgnoreCase))
            {
                await AuditAsync("Student", student.Id, "LoginDenied",
                    $"Student {username} attempted from wrong station: {pcName} (needs {assignedPc.LaboratoryStation})", ipAddress);
                return new LoginResult(AccountRole.Invalid, null, null);
            }

            _context.LabSessions.Add(new LabSession
            {
                StudentId = student.Id,
                PCName = pcName,
                IPAddress = ipAddress,
                StartTime = DateTime.Now,
                Status = "Running",
                IsActive = true
            });
            await _context.SaveChangesAsync();

            await AuditAsync("Student", student.Id, "LoginSuccess",
                $"Student {username} logged in from {pcName} ({ipAddress})", ipAddress);
            return new LoginResult(AccountRole.Student, student.Id, student.FullName);
        }

        var teacher = await _context.Teachers
            .FirstOrDefaultAsync(t => t.Username == username);

        if (teacher != null && _hasher.VerifyHashedPassword(null, teacher.PasswordHash, password) == PasswordVerificationResult.Success)
        {
            if (teacher.Status != "Active")
            {
                await AuditAsync("Teacher", teacher.TeacherId, "LoginDenied",
                    $"Teacher {username} attempted login but account is inactive", ipAddress);
                return new LoginResult(AccountRole.Invalid, null, null);
            }

            await AuditAsync("Teacher", teacher.TeacherId, "LoginSuccess",
                $"Teacher {username} logged in from {ipAddress}", ipAddress);
            return new LoginResult(AccountRole.Teacher, teacher.TeacherId, $"{teacher.FirstName} {teacher.LastName}");
        }

        var admin = await _context.Admins
            .FirstOrDefaultAsync(a => a.Username == username);

        if (admin != null && _hasher.VerifyHashedPassword(null, admin.PasswordHash, password) == PasswordVerificationResult.Success)
        {
            await AuditAsync("Admin", admin.Id, "LoginSuccess",
                $"Admin {username} logged in from {ipAddress}", ipAddress);
            return new LoginResult(AccountRole.Admin, admin.Id, admin.FullName);
        }

        await AuditAsync("System", null, "LoginFailed",
            $"Failed login attempt for '{username}' from {ipAddress}", ipAddress);
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
            // Auditing must never block the login flow
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
