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
    private readonly SessionManagerService? _lab;

    public ClientAuthController(
        ServerAuthService authenticationService,
        LabSessionLifecycleService sessionLifecycle,
        SessionManagerService? lab = null)
    {
        _authenticationService = authenticationService;
        _sessionLifecycle = sessionLifecycle;
        _lab = lab;
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

        // Students may only sign in to a running lab. Answered before the
        // password is checked, with its own status, so the client can tell the
        // pupil to wait for the teacher rather than that they typed it wrong.
        if (_lab is { IsLabOpen: false })
            return Conflict(WorkstationRegistrationService.NoLabMessage);

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
            return Unauthorized("Invalid student credentials or unavailable workstation.");
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

    /// <summary>
    /// Lets a signed-in student replace their own password from the agent. The
    /// web portal turns students away, so this is the only place they can.
    ///
    /// The current password is still required: a classmate at a workstation left
    /// signed in must not be able to take the account over. Wrong guesses are
    /// capped per student, not per address, because a classroom shares one.
    /// </summary>
    [Authorize(Roles = RoleNames.Student)]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(StudentClientPasswordChangeRequest request)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var studentId))
            return Unauthorized();

        if (request is null ||
            string.IsNullOrEmpty(request.CurrentPassword) ||
            string.IsNullOrEmpty(request.NewPassword) ||
            request.CurrentPassword.Length > StudentPasswordRules.MaximumLength ||
            request.NewPassword.Length > StudentPasswordRules.MaximumLength)
        {
            return BadRequest("Enter your current password and a new password.");
        }

        if (request.NewPassword.Length < StudentPasswordRules.MinimumLength)
            return BadRequest($"Your new password must be at least {StudentPasswordRules.MinimumLength} characters long.");

        if (request.NewPassword == request.CurrentPassword)
            return BadRequest("Your new password must be different from your current one.");

        var cacheKey = $"client-password:{studentId}";
        var failures = LoginCache.Get<int>(cacheKey);
        if (failures >= 5)
            return StatusCode(StatusCodes.Status429TooManyRequests, "Too many attempts. Wait a minute, then try again.");

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        if (!await _authenticationService.ChangeStudentPasswordAsync(
                studentId, request.CurrentPassword, request.NewPassword, ipAddress))
        {
            LoginCache.Set(cacheKey, failures + 1, TimeSpan.FromMinutes(1));
            return BadRequest("Your current password is incorrect.");
        }

        LoginCache.Remove(cacheKey);
        return NoContent();
    }

    [Authorize(Roles = RoleNames.Student)]
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
