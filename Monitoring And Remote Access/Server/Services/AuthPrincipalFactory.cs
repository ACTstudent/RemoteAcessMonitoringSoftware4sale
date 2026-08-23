using System.Security.Claims;

namespace Server.Services;

public static class AuthPrincipalFactory
{
    public const string StudentNumberClaim = "cams:student_number";
    public const string PcNameClaim = "cams:pc_name";
    public const string ClientAgentClaim = "cams:client_agent";

    public static ClaimsPrincipal Create(LoginResult result, string? pcName = null, bool isClientAgent = false)
    {
        if (result.AccountId is null || result.Role is AccountRole.None or AccountRole.Invalid)
        {
            throw new ArgumentException("A valid account is required to create an authenticated principal.", nameof(result));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.AccountId.Value.ToString()),
            new(ClaimTypes.Role, result.Role.ToString()),
            new(ClaimTypes.Name, result.DisplayName ?? result.LoginName ?? string.Empty)
        };

        if (!string.IsNullOrWhiteSpace(result.LoginName))
        {
            claims.Add(new Claim(ClaimTypes.UserData, result.LoginName));
        }

        if (!string.IsNullOrWhiteSpace(result.StudentNumber))
        {
            claims.Add(new Claim(StudentNumberClaim, result.StudentNumber));
        }

        if (!string.IsNullOrWhiteSpace(pcName))
        {
            claims.Add(new Claim(PcNameClaim, pcName));
        }

        if (isClientAgent)
        {
            claims.Add(new Claim(ClientAgentClaim, bool.TrueString));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
