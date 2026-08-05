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

public sealed record LoginResult(AccountRole Role, int? AccountId, string? DisplayName);

public interface IAuthenticationService
{
    Task<LoginResult> LoginAsync(string username, string password, string pcName, string ipAddress);
    Task LogoutAsync(int? studentId);
}
