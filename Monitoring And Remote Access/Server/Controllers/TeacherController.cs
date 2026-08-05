using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Controllers
{
    public class TeacherController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SessionManagerService _sessionManager;

        public TeacherController(ApplicationDbContext context, SessionManagerService sessionManager)
        {
            _context = context;
            _sessionManager = sessionManager;
        }

        private bool CheckAccess() => HttpContext.IsTeacher();

        private IActionResult Denied() => RedirectToAction("Login", "Account");

        private async Task AuditAsync(string action, string details)
        {
            _context.AuditLogs.Add(new AuditLog
            {
                UserType = "Teacher",
                UserId = HttpContext.Session.GetInt32("TeacherId"),
                Action = action,
                Details = details,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });
            await _context.SaveChangesAsync();
        }

        // ---------- Dashboard ----------
        public async Task<IActionResult> Dashboard()
        {
            if (!CheckAccess()) return Denied();
            ViewBag.RunningSessions = await _context.LabSessions.CountAsync(s => s.Status == "Running");
            ViewBag.ActiveStudents = await _context.LabSessions.CountAsync(s => s.IsActive);
            ViewBag.GlobalSession = _sessionManager.Snapshot();
            return View();
        }

        // ---------- Global session state (JSON for the control panel header) ----------
        public IActionResult GlobalSessionState()
        {
            if (!CheckAccess()) return Denied();
            return Json(_sessionManager.Snapshot());
        }

        // ---------- Session management (start/pause/end) ----------
        public async Task<IActionResult> Sessions()
        {
            if (!CheckAccess()) return Denied();
            ViewBag.SessionRules = await _context.SessionRules.Where(s => s.IsActive).ToListAsync();
            ViewBag.Computers = await _context.Computers.Where(c => c.Status == "Available").ToListAsync();
            ViewBag.Students = await _context.Students.ToListAsync();
            var sessions = await _context.LabSessions
                .Include(s => s.Student)
                .Include(s => s.Teacher)
                .Include(s => s.Computer)
                .OrderByDescending(s => s.StartTime)
                .Take(100)
                .ToListAsync();
            return View(sessions);
        }

        [HttpPost]
        public async Task<IActionResult> StartSession(int studentId, int? computerId, int? sessionRuleId)
        {
            if (!CheckAccess()) return Denied();

            var rule = sessionRuleId.HasValue
                ? await _context.SessionRules.FindAsync(sessionRuleId.Value)
                : await _context.SessionRules.FirstOrDefaultAsync(r => r.IsDefault);

            var session = new LabSession
            {
                StudentId = studentId,
                TeacherId = HttpContext.Session.GetInt32("TeacherId"),
                ComputerId = computerId,
                SessionRuleId = rule?.SessionRuleId,
                PCName = computerId.HasValue
                    ? (await _context.Computers.FindAsync(computerId.Value))?.LaboratoryStation ?? ""
                    : (await _context.Students.FindAsync(studentId))?.Username ?? "",
                MaxDurationMinutes = rule?.MaxDurationMinutes,
                StartTime = DateTime.Now,
                Status = "Running",
                IsActive = true
            };

            _context.LabSessions.Add(session);
            if (computerId.HasValue)
            {
                var computer = await _context.Computers.FindAsync(computerId.Value);
                if (computer != null)
                {
                    computer.Status = "In Use";
                    computer.AssignedTo = studentId.ToString();
                }
            }
            await _context.SaveChangesAsync();
            await AuditAsync("StartSession", $"Started session for student {studentId}");
            return RedirectToAction("Sessions");
        }

        [HttpPost]
        public async Task<IActionResult> TogglePause(int id)
        {
            if (!CheckAccess()) return Denied();
            var session = await _context.LabSessions.FindAsync(id);
            if (session != null)
            {
                if (session.Status == "Running")
                {
                    session.Status = "Paused";
                    session.PauseTime = DateTime.Now;
                }
                else if (session.Status == "Paused")
                {
                    // Offset elapsed paused time against the start time
                    if (session.PauseTime.HasValue)
                    {
                        session.StartTime = session.StartTime.Add(DateTime.Now - session.PauseTime.Value);
                        session.PauseTime = null;
                    }
                    session.Status = "Running";
                }
                await _context.SaveChangesAsync();
                await AuditAsync("TogglePause", $"Session {id} -> {session.Status}");
            }
            return RedirectToAction("Sessions");
        }

        [HttpPost]
        public async Task<IActionResult> EndSession(int id)
        {
            if (!CheckAccess()) return Denied();
            var session = await _context.LabSessions.FindAsync(id);
            if (session != null)
            {
                session.Status = "Ended";
                session.IsActive = false;
                session.EndTime = DateTime.Now;

                if (session.ComputerId.HasValue)
                {
                    var computer = await _context.Computers.FindAsync(session.ComputerId.Value);
                    if (computer != null)
                    {
                        computer.Status = "Available";
                        computer.AssignedTo = null;
                    }
                }
                await _context.SaveChangesAsync();
                await AuditAsync("EndSession", $"Ended session {id}");
            }
            return RedirectToAction("Sessions");
        }

        // ---------- Live monitoring ----------
        public IActionResult Monitoring()
        {
            if (!CheckAccess()) return Denied();
            ViewBag.GlobalSession = _sessionManager.Snapshot();
            return View();
        }

        // ---------- Snapshot of current monitoring state ----------
        public IActionResult LiveState()
        {
            if (!CheckAccess()) return Denied();
            var svc = HttpContext.RequestServices.GetService(typeof(IMonitoringService)) as IMonitoringService;
            return Json(new
            {
                Students = svc?.ActiveStudents,
                Idle = svc?.IdleStatus,
                Apps = svc?.ActiveApps
            });
        }

        // ---------- Restrictions ----------
        public async Task<IActionResult> Restrictions()
        {
            if (!CheckAccess()) return Denied();
            ViewBag.SessionRules = await _context.SessionRules.Where(s => s.IsActive).ToListAsync();
            var global = await _context.RestrictionRules.Where(r => r.IsGlobal && r.IsActive).ToListAsync();
            var blacklist = await _context.BlacklistItems.Where(b => b.IsActive).ToListAsync();
            ViewBag.Blacklist = blacklist;
            return View(global);
        }

        // ---------- Class records ----------
        public async Task<IActionResult> Records()
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            var sessions = await _context.LabSessions
                .Include(s => s.Student)
                .Include(s => s.Computer)
                .Where(s => s.TeacherId == teacherId)
                .OrderByDescending(s => s.StartTime)
                .Take(500)
                .ToListAsync();

            ViewBag.TotalSessions = sessions.Count;
            ViewBag.TotalMinutes = sessions.Sum(s =>
                s.EndTime.HasValue ? (s.EndTime.Value - s.StartTime).TotalMinutes : 0);

            return View(sessions);
        }

        // ---------- Export classroom records as CSV ----------
        public async Task<IActionResult> ExportRecordsCsv()
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            var sessions = await _context.LabSessions
                .Include(s => s.Student)
                .Include(s => s.Computer)
                .Where(s => s.TeacherId == teacherId)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Student Number,Student Name,Station,Start Time,End Time,Duration (min),Status");
            foreach (var s in sessions)
            {
                var duration = s.EndTime.HasValue ? Math.Round((s.EndTime.Value - s.StartTime).TotalMinutes, 1) : 0;
                csv.AppendLine($"{Csv(s.Student?.StudentNumber)},{Csv(s.Student?.FullName)},{Csv(s.Computer?.LaboratoryStation ?? s.PCName)},{s.StartTime:yyyy-MM-dd HH:mm},{s.EndTime?.ToString("yyyy-MM-dd HH:mm")},{duration},{s.Status}");
            }

            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()),
                "text/csv; charset=utf-8",
                $"CAMS-Classroom-Records-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        }

        private static string Csv(string? value)
        {
            var v = value ?? "";
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        // ---------- Notifications ----------
        [HttpPost]
        public async Task<IActionResult> SendNotification(string type, string title, string message)
        {
            if (!CheckAccess()) return Denied();
            _context.Notifications.Add(new Notification
            {
                StudentId = null,
                Type = type,
                Title = title,
                Message = message,
                CreatedAt = DateTime.Now
            });
            await _context.SaveChangesAsync();

            var svc = HttpContext.RequestServices.GetService(typeof(IMonitoringService)) as IMonitoringService;
            return Json(new { Ok = true });
        }
    }
}