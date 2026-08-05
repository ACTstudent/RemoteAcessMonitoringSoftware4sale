using Microsoft.AspNetCore.Mvc;
using Server.Services;

namespace Server.Controllers
{
    public class AccountController : Controller
    {
        private readonly IAuthenticationService _authService;

        public AccountController(IAuthenticationService authService)
        {
            _authService = authService;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        // Students can ONLY log in. There is no registration flow.
        [HttpPost]
        public async Task<IActionResult> Login(string username, string password)
        {
            var result = await _authService.LoginAsync(
                username,
                password,
                Request.Host.Host,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");

            switch (result.Role)
            {
                case AccountRole.Student:
                    HttpContext.Session.SetInt32("StudentId", result.AccountId!.Value);
                    HttpContext.Session.SetString("FullName", result.DisplayName ?? "");
                    return RedirectToAction("Index", "Monitoring");

                case AccountRole.Admin:
                    HttpContext.Session.SetInt32("AdminId", result.AccountId!.Value);
                    HttpContext.Session.SetString("AdminName", result.DisplayName ?? "");
                    return RedirectToAction("Index", "Monitoring");

                default:
                    ViewBag.Error = result.Role == AccountRole.None
                        ? "Username and password are required."
                        : "Invalid username or password.";
                    return View();
            }
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await _authService.LogoutAsync(HttpContext.Session.GetInt32("StudentId"));
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}
