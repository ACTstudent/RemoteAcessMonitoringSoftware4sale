using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using Server.Services;
using Shared.Contracts;
using ServerAuthService = Server.Services.IAuthenticationService;

namespace Server.Controllers;

[ApiController]
[Route("api/client")]
public sealed class ClientAuthController : ControllerBase
{
    private static readonly MemoryCache LoginCache = new(new MemoryCacheOptions());
    private readonly ServerAuthService _authenticationService;
    private readonly LabSessionLifecycleService _sessionLifecycle;

    public ClientAuthController(
        ServerAuthService authenticationService,
        LabSessionLifecycleService sessionLifecycle)
    {
        _authenticationService = authenticationService;
        _sessionLifecycle = sessionLifecycle;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<StudentClientLoginResponse>> Login(StudentClientLoginRequest request)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.PcName) ||
            request.Username.Length > 50 ||
            request.Password.Length > 256 ||
            request.PcName.Length > 100)
        {
            return BadRequest("Username, password, and workstation name are required.");
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var cacheKey = $"client-login:{ipAddress}";
        var failures = LoginCache.Get<int>(cacheKey);
        if (failures >= 5)
            return StatusCode(StatusCodes.Status429TooManyRequests, "Too many login attempts. Try again later.");

        var result = await _authenticationService.LoginAsync(
            request.Username.Trim(),
            request.Password,
            request.PcName.Trim(),
            ipAddress);

        if (result.Role != AccountRole.Student || result.AccountId is null)
        {
            LoginCache.Set(cacheKey, failures + 1, TimeSpan.FromMinutes(1));
            return Unauthorized("Invalid student credentials or workstation assignment.");
        }

        LoginCache.Remove(cacheKey);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AuthPrincipalFactory.Create(result, request.PcName.Trim(), isClientAgent: true),
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

        return Ok(new StudentClientLoginResponse(
            result.StudentNumber ?? result.LoginName ?? request.Username.Trim(),
            result.DisplayName ?? request.Username.Trim()));
    }

    [Authorize(Roles = "Student")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var studentId))
        {
            await _sessionLifecycle.EndStudentSessionsAsync(
                studentId,
                User.FindFirstValue(AuthPrincipalFactory.PcNameClaim));
        }
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();
        return NoContent();
    }
}
