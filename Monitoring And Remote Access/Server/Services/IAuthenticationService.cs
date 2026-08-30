using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;

namespace Server.Services;

public enum AccountRole
{
    None,
    Invalid,
    Student,
    Teacher,
    Admin
}

public sealed record LoginResult(
    AccountRole Role,
    int? AccountId,
    string? DisplayName,
    string? LoginName = null,
    string? StudentNumber = null);

public interface IAuthenticationService
{
    Task<LoginResult> LoginAsync(string username, string password, string pcName, string ipAddress);
    Task LogoutAsync(int? studentId);
    Task<bool> ChangeStudentPasswordAsync(int studentId, string currentPassword, string newPassword);
    Task<bool> ChangeTeacherPasswordAsync(int teacherId, string currentPassword, string newPassword, string ipAddress);
    Task<bool> ChangeAdminPasswordAsync(int adminId, string currentPassword, string newPassword, string ipAddress);
}
