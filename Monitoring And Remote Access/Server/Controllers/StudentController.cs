using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Controllers
{
    public class StudentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PasswordHasher<object> _hasher = new();

        public StudentController(ApplicationDbContext context)
        {
            _context = context;
        }

        private bool CheckAccess() => HttpContext.IsStudent();

        private IActionResult Denied() => RedirectToAction("Login", "Account");

        // ---------- Dashboard / Session info ----------
        public async Task<IActionResult> Index()
        {
            if (!CheckAccess()) return Denied();

            var studentId = HttpContext.Session.GetInt32("StudentId")!.Value;
            var session = await _context.LabSessions
                .Include(s => s.Computer)
                .Include(s => s.SessionRule)
                .Where(s => s.StudentId == studentId && s.IsActive)
                .OrderByDescending(s => s.StartTime)
                .FirstOrDefaultAsync();

            ViewBag.Remaining = 0;
            if (session != null && session.MaxDurationMinutes.HasValue && session.Status == "Running")
            {
                var elapsed = (DateTime.Now - session.StartTime).TotalMinutes;
                ViewBag.Remaining = Math.Max(0, session.MaxDurationMinutes.Value - (int)elapsed);
            }

            if (session?.Computer?.LaboratoryStation != null)
            {
                HttpContext.Session.SetString("AssignedUnit", session.Computer.LaboratoryStation);
            }

            ViewBag.CurrentSession = session;
            ViewBag.Rules = await _context.RestrictionRules.Where(r => r.IsActive).ToListAsync();
            ViewBag.Blacklist = await _context.BlacklistItems.Where(b => b.IsActive).ToListAsync();
            return View();
        }

        // ---------- Alert center ----------
        public async Task<IActionResult> Alerts()
        {
            if (!CheckAccess()) return Denied();
            var notifications = await _context.Notifications
                .OrderByDescending(n => n.CreatedAt)
                .Take(100)
                .ToListAsync();
            return View(notifications);
        }

        [HttpPost]
        public async Task<IActionResult> MarkRead(int id)
        {
            if (!CheckAccess()) return Denied();
            var n = await _context.Notifications.FindAsync(id);
            if (n != null) { n.IsRead = true; await _context.SaveChangesAsync(); }
            return RedirectToAction("Alerts");
        }

        // ---------- Account settings ----------
        public IActionResult Settings()
        {
            if (!CheckAccess()) return Denied();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(string currentPassword, string newPassword)
        {
            if (!CheckAccess()) return Denied();
            var studentId = HttpContext.Session.GetInt32("StudentId")!.Value;
            var student = await _context.Students.FindAsync(studentId);
            if (student == null) return RedirectToAction("Login", "Account");

            if (_hasher.VerifyHashedPassword(null, student.PasswordHash, currentPassword) != PasswordVerificationResult.Success)
            {
                ViewBag.Error = "Current password is incorrect.";
                return View("Settings");
            }

            student.PasswordHash = _hasher.HashPassword(null, newPassword);
            await _context.SaveChangesAsync();
            ViewBag.Success = "Password updated successfully.";
            return View("Settings");
        }

        public async Task<IActionResult> _SessionStatusJson()
        {
            if (!CheckAccess()) return Json(new { });
            var studentId = HttpContext.Session.GetInt32("StudentId")!.Value;
            var session = await _context.LabSessions
                .Where(s => s.StudentId == studentId && s.IsActive)
                .OrderByDescending(s => s.StartTime)
                .FirstOrDefaultAsync();

            if (session == null) return Json(new { active = false });
            var elapsed = (session.StartTime != default) ? (DateTime.Now - session.StartTime).TotalMinutes : 0;
            var remaining = session.MaxDurationMinutes.HasValue ? Math.Max(0, session.MaxDurationMinutes.Value - (int)elapsed) : (int?)null;
            return Json(new { active = true, status = session.Status, remaining, station = session.Computer?.LaboratoryStation });
        }
    }
}