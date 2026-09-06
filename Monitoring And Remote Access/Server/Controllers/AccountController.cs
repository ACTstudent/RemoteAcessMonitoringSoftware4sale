using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Server.Services;
using ServerAuthService = Server.Services.IAuthenticationService;

namespace Server.Controllers
{
    public class AccountController : Controller
    {
        private readonly ServerAuthService _authService;
        private readonly LabSessionLifecycleService? _sessionLifecycle;
        private static readonly MemoryCache _loginCache = new(new MemoryCacheOptions());

        public AccountController(ServerAuthService authService, LabSessionLifecycleService? sessionLifecycle = null)
        {
            _authService = authService;
            _sessionLifecycle = sessionLifecycle;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        /// <summary>
        /// Where an authenticated user lands when their role does not reach a
        /// page. This used to be the login form, which told someone who was
        /// already signed in to sign in, and offered no way onward but the
        /// browser's Back button.
        /// </summary>
        [HttpGet]
        public IActionResult AccessDenied(string? returnUrl = null)
        {
            // Someone with no session at all belongs on the sign-in form; this
            // page is for a role that is real but insufficient.
            var role = HttpContext.Session.GetString("Role");
            if (string.IsNullOrEmpty(role))
            {
                return RedirectToAction(nameof(Login));
            }

            Response.StatusCode = StatusCodes.Status403Forbidden;
            ViewBag.AttemptedPath = returnUrl;
            ViewBag.Role = role;
            ViewBag.DisplayName = HttpContext.Session.GetString("TeacherName")
                ?? HttpContext.Session.GetString("AdminName")
                ?? HttpContext.Session.GetString("FullName");
            return View();
        }

        // Students, Teachers, and Admins can log in
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var key = $"login:{ip}";
            var fails = _loginCache.Get<int>(key);

            if (fails >= 5)
            {
                ViewBag.Error = "Too many login attempts. Please wait 60 seconds.";
                return View();
            }

            var result = await _authService.LoginAsync(
                username,
                password,
                string.Empty,
                ip);

            switch (result.Role)
            {
                case AccountRole.Student:
                    await SignInAsync(result, string.Empty);
                    HttpContext.Session.SetInt32("StudentId", result.AccountId!.Value);
                    HttpContext.Session.SetString("FullName", result.DisplayName ?? "");
                    HttpContext.Session.SetString("Role", "Student");
                    _loginCache.Remove(key);
                    return RedirectToAction("Index", "Monitoring");

                case AccountRole.Teacher:
                    await SignInAsync(result, string.Empty);
                    HttpContext.Session.SetInt32("TeacherId", result.AccountId!.Value);
                    HttpContext.Session.SetString("TeacherName", result.DisplayName ?? "");
                    HttpContext.Session.SetString("Role", "Teacher");
                    _loginCache.Remove(key);
                    return RedirectToAction("Dashboard", "Teacher");

                case AccountRole.Admin:
                    await SignInAsync(result, string.Empty);
                    HttpContext.Session.SetInt32("AdminId", result.AccountId!.Value);
                    HttpContext.Session.SetString("AdminName", result.DisplayName ?? "");
                    HttpContext.Session.SetString("Role", "Admin");
                    _loginCache.Remove(key);
                    return RedirectToAction("Index", "Admin");

                default:
                    _loginCache.Set(key, fails + 1, TimeSpan.FromMinutes(1));
                    ViewBag.Error = result.Role == AccountRole.None
                        ? "Username and password are required."
                        : "Invalid username or password.";
                    return View();
            }
        }

        private Task SignInAsync(LoginResult result, string pcName)
        {
            return HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                AuthPrincipalFactory.Create(result, pcName),
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    AllowRefresh = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                });
        }

        [HttpGet, HttpPost]
        public async Task<IActionResult> Logout()
        {
            var studentId = HttpContext.Session.GetInt32("StudentId");
            if (studentId.HasValue)
            {
                if (_sessionLifecycle is not null)
                    await _sessionLifecycle.EndStudentSessionsAndNotifyAsync(studentId.Value);
                else
                    await _authService.LogoutAsync(studentId.Value);
            }
            HttpContext.Session.Clear();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }
    }
}
