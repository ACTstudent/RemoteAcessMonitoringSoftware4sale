using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;

namespace Server.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
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
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "Username and password are required.";
                return View();
            }

            // Check against both Student and Admin tables
            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Username == username && s.PasswordHash == password);

            if (student != null)
            {
                var session = new LabSession
                {
                    StudentId = student.Id,
                    PCName = Request.Host.Host,
                    IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
                    StartTime = DateTime.Now,
                    IsActive = true
                };
                _context.LabSessions.Add(session);
                await _context.SaveChangesAsync();

                HttpContext.Session.SetInt32("StudentId", student.Id);
                HttpContext.Session.SetString("FullName", student.FullName);
                return RedirectToAction("Index", "Monitoring");
            }

            var admin = await _context.Admins
                .FirstOrDefaultAsync(a => a.Username == username && a.PasswordHash == password);

            if (admin != null)
            {
                HttpContext.Session.SetInt32("AdminId", admin.Id);
                HttpContext.Session.SetString("AdminName", admin.FullName);
                return RedirectToAction("Index", "Monitoring");
            }

            ViewBag.Error = "Invalid username or password.";
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            var studentId = HttpContext.Session.GetInt32("StudentId");
            if (studentId.HasValue)
            {
                var activeSessions = _context.LabSessions.Where(s => s.StudentId == studentId.Value && s.IsActive);
                foreach (var s in activeSessions)
                {
                    s.IsActive = false;
                    s.EndTime = DateTime.Now;
                }
                await _context.SaveChangesAsync();
            }

            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}
