using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Controllers
{
    [Authorize(Roles = "Student")]
    [AutoValidateAntiforgeryToken]
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
            var maxDuration = session?.MaxDurationMinutes ?? session?.SessionRule?.MaxDurationMinutes;
            if (session != null && maxDuration.HasValue)
            {
                var effectiveNow = session.Status == "Paused" && session.PauseTime.HasValue
                    ? session.PauseTime.Value.ToUniversalTime()
                    : DateTime.UtcNow;
                var elapsed = (effectiveNow - session.StartTime.ToUniversalTime()).TotalMinutes;
                ViewBag.Remaining = Math.Max(0, maxDuration.Value - (int)elapsed);
            }

            if (session?.Computer?.LaboratoryStation != null)
            {
                HttpContext.Session.SetString("AssignedUnit", session.Computer.LaboratoryStation);
            }

            ViewBag.CurrentSession = session;
            ViewBag.Rules = await _context.RestrictionRules
                .Where(r => r.IsActive && (r.IsGlobal || (session != null && r.TeacherId == session.TeacherId)))
                .ToListAsync();
            ViewBag.Blacklist = await _context.BlacklistItems.Where(b => b.IsActive).ToListAsync();
            return View();
        }

        // ---------- Alert center ----------
        public async Task<IActionResult> Alerts()
        {
            if (!CheckAccess()) return Denied();
            var studentId = HttpContext.Session.GetInt32("StudentId")!.Value;
            var notifications = await _context.Notifications
                .Where(n => n.StudentId == studentId || n.StudentId == null)
                .OrderByDescending(n => n.CreatedAt)
                .Take(100)
                .ToListAsync();
            return View(notifications);
        }

        [HttpPost]
        public async Task<IActionResult> MarkRead(int id)
        {
            if (!CheckAccess()) return Denied();
            var studentId = HttpContext.Session.GetInt32("StudentId")!.Value;
            var n = await _context.Notifications.FirstOrDefaultAsync(n => n.NotificationId == id &&
                (n.StudentId == studentId || n.StudentId == null));
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
        public async Task<IActionResult> ResetPassword(PasswordChangeInput input)
        {
            if (!CheckAccess()) return Denied();
            var studentId = HttpContext.Session.GetInt32("StudentId")!.Value;
            if (!ModelState.IsValid || !await new AuthenticationService(_context).ChangeStudentPasswordAsync(studentId, input.CurrentPassword, input.NewPassword))
            {
                ViewBag.Error = "Current password is incorrect.";
                return View("Settings");
            }
            ViewBag.Success = "Password updated successfully.";
            return View("Settings");
        }

        public async Task<IActionResult> _SessionStatusJson()
        {
            if (!CheckAccess()) return Json(new { });
            var studentId = HttpContext.Session.GetInt32("StudentId")!.Value;
            var session = await _context.LabSessions
                .Include(s => s.SessionRule)
                .Include(s => s.Computer)
                .Where(s => s.StudentId == studentId && s.IsActive)
                .OrderByDescending(s => s.StartTime)
                .FirstOrDefaultAsync();

            if (session == null) return Json(new { active = false });
            var effectiveNow = session.Status == "Paused" && session.PauseTime.HasValue
                ? session.PauseTime.Value.ToUniversalTime()
                : DateTime.UtcNow;
            var elapsed = session.StartTime != default ? (effectiveNow - session.StartTime.ToUniversalTime()).TotalMinutes : 0;
            var maxDuration = session.MaxDurationMinutes ?? session.SessionRule?.MaxDurationMinutes;
            var remaining = maxDuration.HasValue ? Math.Max(0, maxDuration.Value - (int)elapsed) : (int?)null;
            return Json(new { active = true, status = session.Status, remaining, station = session.Computer?.LaboratoryStation });
        }
    }
}
