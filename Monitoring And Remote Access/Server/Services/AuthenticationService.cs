using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;

namespace Server.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly ApplicationDbContext _context;

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
            .FirstOrDefaultAsync(s => s.Username == username && s.PasswordHash == password);

        if (student != null)
        {
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

            return new LoginResult(AccountRole.Student, student.Id, student.FullName);
        }

        var teacher = await _context.Teachers
            .FirstOrDefaultAsync(t => t.Username == username && t.PasswordHash == password);

        if (teacher != null)
        {
            if (teacher.Status != "Active")
            {
                return new LoginResult(AccountRole.Invalid, null, null);
            }

            return new LoginResult(AccountRole.Teacher, teacher.TeacherId, $"{teacher.FirstName} {teacher.LastName}");
        }

        var admin = await _context.Admins
            .FirstOrDefaultAsync(a => a.Username == username && a.PasswordHash == password);

        if (admin != null)
        {
            return new LoginResult(AccountRole.Admin, admin.Id, admin.FullName);
        }

        return new LoginResult(AccountRole.Invalid, null, null);
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
