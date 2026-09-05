using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;
using Microsoft.AspNetCore.SignalR;
using Server.Hubs;
using Shared.Contracts;
using Server.Authorization;

namespace Server.Controllers
{
    [Authorize(Roles = "Teacher")]
    [ServiceFilter(typeof(ActiveTeacherAuthorizationFilter))]
    [AutoValidateAntiforgeryToken]
    public class TeacherController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SessionManagerService _sessionManager;
        private readonly LabSessionLifecycleService _sessionLifecycle;
        private readonly IClassManagementService _classManagement;
        private readonly IAnalyticsService _analytics;
        private readonly IAuthenticationService _authentication;

        public TeacherController(
            ApplicationDbContext context,
            SessionManagerService sessionManager,
            LabSessionLifecycleService sessionLifecycle,
            IClassManagementService? classManagement = null,
            IAnalyticsService? analytics = null,
            IAuthenticationService? authentication = null)
        {
            _context = context;
            _sessionManager = sessionManager;
            _sessionLifecycle = sessionLifecycle;
            _classManagement = classManagement ?? new ClassManagementService(context);
            _analytics = analytics ?? new AnalyticsService(context);
            _authentication = authentication ?? new AuthenticationService(context);
        }

        private bool CheckAccess() => HttpContext.IsTeacher();

        private IActionResult Denied() => RedirectToAction("Login", "Account");

        private async Task<bool> LoginIdentifierInUseAsync(string value, int excludedStudentId)
        {
            value = value.Trim().ToLower();
            return await _context.Admins.AnyAsync(account => account.Username.ToLower() == value) ||
                   await _context.Teachers.AnyAsync(account => account.Username.ToLower() == value) ||
                   await _context.Students.AnyAsync(account => account.Id != excludedStudentId &&
                       (account.Username.ToLower() == value || account.StudentNumber.ToLower() == value));
        }

        // Global access: every teacher can monitor and manage every student, regardless of class assignment.
        private IQueryable<Student> AccessibleStudents(int teacherId) => _context.Students;

        private IQueryable<Computer> AccessibleComputers(List<int> studentIds)
        {
            var studentKeys = studentIds.Select(id => id.ToString()).ToList();
            return _context.Computers.Where(computer =>
                (computer.AssignedTo != null && studentKeys.Contains(computer.AssignedTo)) ||
                _context.LabSessions.Any(session =>
                    session.ComputerId == computer.ComputerId &&
                    session.IsActive &&
                    studentIds.Contains(session.StudentId)));
        }

        private static string? NormalizeComputerStatus(string? status) => status?.Trim().ToLowerInvariant() switch
        {
            "available" => "Available",
            "assigned" => "Assigned",
            "in use" => "In Use",
            "maintenance" => "Maintenance",
            "online" => "Online",
            "offline" => "Offline",
            _ => null
        };

        private async Task AuditAsync(string action, string details)
        {
            _context.AuditLogs.Add(new AuditLog
            {
                UserType = "Teacher",
                UserId = HttpContext.Session.GetInt32("TeacherId"),
                Action = action,
                Details = details,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        // ---------- Account settings ----------
        public IActionResult Settings()
        {
            if (!CheckAccess()) return Denied();
            return View(new PasswordChangeInput());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword([Bind("CurrentPassword,NewPassword,ConfirmPassword")] PasswordChangeInput input)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            if (!ModelState.IsValid)
            {
                return View("Settings", input);
            }

            var changed = await _authentication.ChangeTeacherPasswordAsync(
                teacherId.Value,
                input.CurrentPassword,
                input.NewPassword,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
            if (!changed)
            {
                ModelState.AddModelError(nameof(input.CurrentPassword), "The current password is incorrect.");
                return View("Settings", input);
            }

            TempData["Message"] = "Your password was changed successfully.";
            return RedirectToAction(nameof(Settings));
        }

        // ---------- Dashboard ----------
        public async Task<IActionResult> Dashboard()
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var studentIds = AccessibleStudents(teacherId.Value).Select(s => s.Id);
            ViewBag.RunningSessions = await _context.LabSessions.CountAsync(s =>
                studentIds.Contains(s.StudentId) && s.Status == "Running");
            ViewBag.ActiveStudents = await _context.LabSessions.CountAsync(s =>
                studentIds.Contains(s.StudentId) && s.IsActive);
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
            ViewBag.GlobalSession = _sessionManager.Snapshot();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var students = await _classManagement.GetStudentsForTeacherAsync(teacherId.Value);
            ViewBag.Students = students;
            var assignedStudentIds = students.Select(student => student.Id.ToString()).ToList();
            ViewBag.Computers = await _context.Computers
                .Where(c => c.Status == "Available" ||
                    (c.Status == "Assigned" && c.AssignedTo != null && assignedStudentIds.Contains(c.AssignedTo)))
                .ToListAsync();
            var sessions = await _context.LabSessions
                .Include(s => s.Student)
                .Include(s => s.Teacher)
                .Include(s => s.Computer)
                .OrderByDescending(s => s.StartTime)
                .Take(100)
                .ToListAsync();
            return View(sessions);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> StartSession(int studentId, int? computerId, int? sessionRuleId)
        {
            if (!CheckAccess()) return Denied();

            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            var student = teacherId.HasValue
                ? await _context.Students
                    .Include(s => s.Class)
                    .FirstOrDefaultAsync(s => s.Id == studentId)
                : null;
            if (student == null)
            {
                TempData["ErrorMessage"] = "The selected student was not found.";
                return RedirectToAction("Sessions");
            }

            var rule = sessionRuleId.HasValue
                ? await _context.SessionRules.FirstOrDefaultAsync(r => r.SessionRuleId == sessionRuleId.Value && r.IsActive)
                // Must match the automatic session path, which only ever picks an
                // active default rule. Without IsActive a deactivated rule could
                // still be attached to a new session.
                : await _context.SessionRules.FirstOrDefaultAsync(r => r.IsActive && r.IsDefault);
            if (sessionRuleId.HasValue && rule is null)
            {
                TempData["ErrorMessage"] = "The selected session rule is unavailable.";
                return RedirectToAction(nameof(Sessions));
            }
            if (await _context.LabSessions.AnyAsync(s => s.StudentId == studentId && s.IsActive && s.Status != "Ended"))
            {
                TempData["ErrorMessage"] = "This student already has an active session.";
                return RedirectToAction(nameof(Sessions));
            }

            Computer? computer = null;
            if (computerId.HasValue)
            {
                computer = await _context.Computers.FirstOrDefaultAsync(c => c.ComputerId == computerId.Value);
                var assignedToStudent = computer?.AssignedTo == studentId.ToString();
                if (computer is null ||
                    (!string.Equals(computer.Status, "Available", StringComparison.OrdinalIgnoreCase) && !assignedToStudent) ||
                    (!string.IsNullOrWhiteSpace(computer.AssignedTo) && !assignedToStudent) ||
                    await _context.LabSessions.AnyAsync(s => s.ComputerId == computerId.Value && s.IsActive && s.Status != "Ended"))
                {
                    TempData["ErrorMessage"] = "The selected workstation is not available.";
                    return RedirectToAction(nameof(Sessions));
                }
            }

            var session = new LabSession
            {
                StudentId = studentId,
                TeacherId = teacherId,
                ComputerId = computerId,
                SessionRuleId = rule?.SessionRuleId,
                PCName = computer?.LaboratoryStation ?? string.Empty,
                MaxDurationMinutes = rule?.MaxDurationMinutes,
                 StartTime = DateTime.UtcNow,
                Status = "Running",
                IsActive = true
            };

            _context.LabSessions.Add(session);
            if (computer is not null)
            {
                computer.Status = "In Use";
                computer.AssignedTo = studentId.ToString();
            }
            await _context.SaveChangesAsync();
            await _sessionLifecycle.NotifyStateAsync(session);
            await AuditAsync("StartSession", $"Started session for student {studentId}");
            TempData["Message"] = "Lab Session started successfully!";
            return RedirectToAction("Sessions");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePause(int id)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var session = await _context.LabSessions.Include(s => s.SessionRule)
                .FirstOrDefaultAsync(s => s.Id == id && s.IsActive);
            if (session != null)
            {
                if (session.Status == "Running")
                {
                    if (session.SessionRule is { AllowPause: false })
                    {
                        TempData["ErrorMessage"] = "The session rule does not allow pausing.";
                        return RedirectToAction(nameof(Sessions));
                    }
                    session.Status = "Paused";
                     session.PauseTime = DateTime.UtcNow;
                }
                else if (session.Status == "Paused")
                {
                    if (session.PauseTime.HasValue)
                    {
                        session.AccumulatedPauseSeconds += Math.Max(0, (int)(DateTime.UtcNow - session.PauseTime.Value).TotalSeconds);
                        session.PauseTime = null;
                    }
                    session.Status = "Running";
                }
                await _context.SaveChangesAsync();
                await _sessionLifecycle.NotifyStateAsync(session);
                await AuditAsync("TogglePause", $"Session {id} -> {session.Status}");
                TempData["Message"] = $"Session status toggled to {session.Status}.";
            }
            return RedirectToAction("Sessions");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EndSession(int id)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var session = await _context.LabSessions.Include(s => s.Computer)
                .FirstOrDefaultAsync(s => s.Id == id);
            if (session != null)
            {
                await _sessionLifecycle.EndAsync(session);
                await AuditAsync("EndSession", $"Ended session {id}");
                TempData["Message"] = "Lab Session ended successfully!";
            }
            return RedirectToAction("Sessions");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> GlobalStartSession()
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var resumed = await _sessionLifecycle.ResumeAllSessionsAsync();
            await AuditAsync("GlobalStartSession", $"Started or resumed {resumed} paused sessions");
            return RedirectToAction(nameof(Sessions));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> GlobalPauseSession()
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var paused = await _sessionLifecycle.PauseAllSessionsAsync();
            await AuditAsync("GlobalPauseSession", $"Paused {paused} sessions");
            return RedirectToAction(nameof(Sessions));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> GlobalEndSession()
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var ended = await _sessionLifecycle.EndAllSessionsAsync();
            await AuditAsync("GlobalEndSession", $"Ended {ended} sessions");
            return RedirectToAction(nameof(Sessions));
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
            var students = svc?.ActiveStudents.ToList() ?? new List<StudentConnectionMessage>();
            return Json(new
            {
                Students = students,
                Idle = svc?.IdleStatus,
                Apps = svc?.ActiveApps,
                Browsers = svc?.BrowserMonitoringStatus
            });
        }

        // ---------- Remote-control history ----------
        public async Task<IActionResult> RemoteHistory(DateTime? from = null, DateTime? to = null, string? command = null, string? studentId = null, int page = 1)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            ViewBag.RemoteSessions = await _context.RemoteControlSessions.AsNoTracking()
                .OrderByDescending(s => s.StartedAt).Take(100).ToListAsync();
            ViewBag.From = from?.ToString("yyyy-MM-dd"); ViewBag.To = to?.ToString("yyyy-MM-dd");
            ViewBag.Command = command; ViewBag.StudentId = studentId;
            var result = await _analytics.GetRemoteHistoryAsync(teacherId.Value, from, to, command, studentId, page);
            return View(result);
        }

        [HttpGet]
        public async Task<IActionResult> ExportRemoteHistoryCsv(DateTime? from = null, DateTime? to = null, string? command = null, string? studentId = null)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var result = await _analytics.GetRemoteHistoryAsync(teacherId.Value, from, to, command, studentId, 1, 500);
            var csv = new System.Text.StringBuilder("Timestamp,Student,Command,Details,Session\n");
            foreach (var item in result.Items) csv.AppendLine($"{item.Timestamp:O},{Csv(item.StudentId)},{Csv(item.Command)},{Csv(item.Details)},{item.SessionId?.ToString() ?? ""}");
            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"CAMS-Remote-History-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
        }

        // ---------- Restrictions ----------
        public async Task<IActionResult> Restrictions()
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            ViewBag.TeacherId = teacherId.Value;
            ViewBag.SessionRules = await _context.SessionRules.Where(s => s.IsActive).ToListAsync();
            return View(new PolicyManagementViewModel
            {
                Restrictions = await _context.RestrictionRules
                    .Where(r => r.IsGlobal || r.TeacherId == teacherId.Value)
                    .OrderByDescending(r => r.CreatedAt).ToListAsync(),
                Blacklist = new List<BlacklistItem>(),
                ApplicationCategories = new List<ApplicationCategory>(),
                WebsiteCategories = new List<WebsiteCategory>()
            });
        }

        // ---------- Policy management ----------
        private static bool ValidMode(string? mode) => mode is "Block" or "Allow";
        private static bool ValidRuleType(string? type) => type is "Application" or "Website";
        private static bool ValidBlacklistType(string? type) => type is "Application" or "Website" or "Domain" or "Process";

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateRestriction([Bind("RuleType,Target,Description,Mode,IsGlobal,IsActive")] RestrictionRule rule)
        {
            if (!CheckAccess()) return Denied();
            if (!ValidRuleType(rule.RuleType) || string.IsNullOrWhiteSpace(rule.Target) || !ValidMode(rule.Mode))
            {
                TempData["ErrorMessage"] = "Choose a valid rule type and mode, and provide a target.";
                return RedirectToAction(nameof(Restrictions));
            }
            rule.RuleType = rule.RuleType.Trim(); rule.Target = rule.Target.Trim(); rule.Description = rule.Description?.Trim() ?? "";
            rule.TeacherId = HttpContext.Session.GetInt32("TeacherId");
            rule.IsGlobal = false;
            rule.CreatedAt = DateTime.UtcNow;
            _context.RestrictionRules.Add(rule);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateRestriction", $"Added {rule.Mode} restriction on {rule.Target}");
            return RedirectToAction(nameof(Restrictions));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateRestriction([Bind("RestrictionRuleId,RuleType,Target,Description,Mode,IsGlobal,IsActive")] RestrictionRule input)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            var rule = teacherId.HasValue
                ? await _context.RestrictionRules.FirstOrDefaultAsync(r => r.RestrictionRuleId == input.RestrictionRuleId && r.TeacherId == teacherId.Value)
                : null;
            if (rule == null || !ValidRuleType(input.RuleType) || string.IsNullOrWhiteSpace(input.Target) || !ValidMode(input.Mode))
                return RedirectToAction(nameof(Restrictions));
            rule.RuleType = input.RuleType.Trim(); rule.Target = input.Target.Trim(); rule.Description = input.Description?.Trim() ?? "";
            rule.Mode = input.Mode; rule.IsGlobal = false; rule.IsActive = input.IsActive;
            await _context.SaveChangesAsync();
            await AuditAsync("UpdateRestriction", $"Updated restriction {rule.RestrictionRuleId}");
            return RedirectToAction(nameof(Restrictions));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteRestriction(int id)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            var rule = teacherId.HasValue
                ? await _context.RestrictionRules.FirstOrDefaultAsync(r => r.RestrictionRuleId == id && r.TeacherId == teacherId.Value)
                : null;
            if (rule != null) { _context.RestrictionRules.Remove(rule); await _context.SaveChangesAsync(); await AuditAsync("DeleteRestriction", $"Removed restriction {id}"); }
            return RedirectToAction(nameof(Restrictions));
        }

        // ---------- Class records ----------
        public async Task<IActionResult> Records()
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            var sessions = await _context.LabSessions
                .Include(s => s.Student)
                .Include(s => s.Computer)
                .OrderByDescending(s => s.StartTime)
                .Take(500)
                .ToListAsync();

            ViewBag.TotalSessions = sessions.Count;
            ViewBag.TotalMinutes = sessions.Sum(s =>
                s.EndTime.HasValue ? (s.EndTime.Value - s.StartTime).TotalMinutes : 0);
            var studentIds = await _context.Students
                .Select(s => s.Id)
                .ToListAsync();
            ViewBag.ApplicationUsage = await _context.UsageLogs
                .Include(log => log.Student)
                .Where(log => log.StudentId.HasValue && studentIds.Contains(log.StudentId.Value))
                .OrderByDescending(log => log.Timestamp)
                .Take(500)
                .ToListAsync();
            ViewBag.WebsiteUsage = await _context.WebsiteUsageLogs
                .Include(log => log.Student)
                .Where(log => log.StudentId.HasValue && studentIds.Contains(log.StudentId.Value))
                .OrderByDescending(log => log.Timestamp)
                .Take(500)
                .ToListAsync();

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

        // ---------- Analytics and activity reporting ----------
        public async Task<IActionResult> StudentDetails(int id, DateTime? from = null, DateTime? to = null)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var report = await _analytics.GetStudentReportAsync(id, teacherId.Value,
                from ?? DateTime.UtcNow.Date, to ?? DateTime.UtcNow.Date.AddDays(1).AddTicks(-1));
            return report is null ? RedirectToAction("Students") : View(report);
        }

        public async Task<IActionResult> ClassAnalytics(int id, DateTime? from = null, DateTime? to = null, string? station = null)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var start = from ?? DateTime.UtcNow.Date;
            var end = to ?? DateTime.UtcNow.Date.AddDays(1);
            if (end <= start || end - start > TimeSpan.FromDays(366)) return BadRequest("Invalid date range.");
            var report = await _analytics.GetClassReportAsync(id, teacherId.Value, start, end, station);
            return report is null ? NotFound() : View(report);
        }

        public async Task<IActionResult> LabUtilization(DateTime? from = null, DateTime? to = null, string? station = null, int? classId = null)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var start = (from ?? DateTime.UtcNow.Date.AddDays(-30)).Date;
            var end = (to ?? DateTime.UtcNow.Date).Date.AddDays(1);
            if (end <= start || end - start > TimeSpan.FromDays(366)) return BadRequest("Invalid date range.");
            var report = await _analytics.GetLabUtilizationAsync(teacherId.Value, start, end, station, classId);
            return report is null ? NotFound() : View(report);
        }

        public async Task<IActionResult> UnifiedTimeline(
            DateTime? from = null,
            DateTime? to = null,
            int? studentId = null,
            int? classId = null,
            string? station = null,
            string? source = null,
            string? eventType = null,
            int page = 1,
            int pageSize = 100)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var start = (from ?? DateTime.UtcNow.Date.AddDays(-7)).Date;
            var end = (to ?? DateTime.UtcNow.Date).Date.AddDays(1);
            if (end <= start || end - start > TimeSpan.FromDays(366) || page < 1 || pageSize is < 1 or > 500)
                return BadRequest("Invalid timeline filters.");
            var report = await _analytics.GetUnifiedTimelineAsync(teacherId.Value,
                new UnifiedTimelineFilter(start, end, studentId, classId, station, source, eventType, page, pageSize));
            return report is null ? NotFound() : View(report);
        }

        public async Task<IActionResult> BrowserMonitoringHistory(
            DateTime? from = null,
            DateTime? to = null,
            string? browser = null,
            string? mode = null,
            int page = 1,
            int pageSize = 100)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            if (page < 1 || pageSize is < 1 or > 500) return BadRequest("Invalid paging.");
            var start = (from ?? DateTime.UtcNow.Date.AddDays(-7)).Date;
            var end = (to ?? DateTime.UtcNow.Date).Date.AddDays(1);
            if (end <= start || end - start > TimeSpan.FromDays(366)) return BadRequest("Invalid date range.");

            BrowserMonitoringMode? selectedMode = null;
            if (!string.IsNullOrWhiteSpace(mode))
            {
                if (!Enum.TryParse<BrowserMonitoringMode>(mode, true, out var parsedMode)) return BadRequest("Invalid browser mode.");
                selectedMode = parsedMode;
            }
            var studentIds = await AccessibleStudents(teacherId.Value).Select(student => student.StudentNumber).ToListAsync();
            var query = _context.BrowserMonitoringRecords.AsNoTracking()
                .Where(record => studentIds.Contains(record.StudentId) && record.Timestamp >= start && record.Timestamp < end);
            if (!string.IsNullOrWhiteSpace(browser)) query = query.Where(record => record.Browser == browser.Trim().ToLowerInvariant());
            if (selectedMode.HasValue) query = query.Where(record => record.Mode == selectedMode.Value);
            var total = await query.CountAsync();
            page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
            var records = await query.OrderByDescending(record => record.Timestamp)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            ViewBag.From = start.ToString("yyyy-MM-dd");
            ViewBag.To = end.AddDays(-1).ToString("yyyy-MM-dd");
            ViewBag.Browser = browser;
            ViewBag.Mode = selectedMode;
            ViewBag.Paging = new PagedResult<BrowserMonitoringRecord>(records, page, pageSize, total);
            return View(records);
        }

        public async Task<IActionResult> ExportBrowserMonitoringCsv(
            DateTime? from = null,
            DateTime? to = null,
            string? browser = null,
            string? mode = null)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var start = (from ?? DateTime.UtcNow.Date.AddDays(-7)).Date;
            var end = (to ?? DateTime.UtcNow.Date).Date.AddDays(1);
            if (end <= start || end - start > TimeSpan.FromDays(366)) return BadRequest("Invalid date range.");
            BrowserMonitoringMode? selectedMode = null;
            if (!string.IsNullOrWhiteSpace(mode))
            {
                if (!Enum.TryParse<BrowserMonitoringMode>(mode, true, out var parsedMode)) return BadRequest("Invalid browser mode.");
                selectedMode = parsedMode;
            }
            var studentIds = await AccessibleStudents(teacherId.Value).Select(student => student.StudentNumber).ToListAsync();
            var query = _context.BrowserMonitoringRecords.AsNoTracking()
                .Where(record => studentIds.Contains(record.StudentId) && record.Timestamp >= start && record.Timestamp < end);
            if (!string.IsNullOrWhiteSpace(browser)) query = query.Where(record => record.Browser == browser.Trim().ToLowerInvariant());
            if (selectedMode.HasValue) query = query.Where(record => record.Mode == selectedMode.Value);
            var records = await query.OrderByDescending(record => record.Timestamp).Take(5000).ToListAsync();
            var csv = new System.Text.StringBuilder("Timestamp,Student ID,Station,Browser,Mode,Detail\n");
            foreach (var record in records)
                csv.AppendLine($"{record.Timestamp:O},{Csv(record.StudentId)},{Csv(record.PcName)},{Csv(record.Browser)},{record.Mode},{Csv(record.Detail)}");
            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8",
                $"CAMS-Browser-Monitoring-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
        }

        public async Task<IActionResult> ExportStudentAnalyticsCsv(int id, DateTime? from = null, DateTime? to = null)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var report = await _analytics.GetStudentReportAsync(id, teacherId.Value,
                from ?? DateTime.UtcNow.Date, to ?? DateTime.UtcNow.Date.AddDays(1).AddTicks(-1));
            if (report is null) return NotFound();
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Timestamp,Event,Application,Details,Station");
            foreach (var item in report.Timeline)
                csv.AppendLine($"{item.Timestamp:O},{Csv(item.EventType)},{Csv(item.ApplicationName)},{Csv(item.Details)},{Csv(item.PcName)}");
            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8",
                $"CAMS-Student-{report.Student.StudentNumber}-Activity-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> ActivityTimeline(int id, DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 100, string? eventType = null)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var timeline = await _analytics.GetActivityTimelineAsync(id, teacherId.Value,
                from ?? DateTime.UtcNow.Date, to ?? DateTime.UtcNow.Date.AddDays(1).AddTicks(-1), page, pageSize, eventType);
            return Json(timeline);
        }

        public async Task<IActionResult> Alerts([FromQuery] AlertListFilter filter)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            // Out-of-range paging only reaches here from a hand-edited URL, so
            // pull it back into range rather than answering a shared link with a
            // bare 400.
            filter.ClampPaging();

            if (!filter.TryResolveStatus(out var selectedStatus)) return BadRequest("Invalid alert status.");

            // A teacher can produce a backwards range from the filter form. Keep
            // the entered dates on screen and say what is wrong instead of
            // replacing the page with an error.
            if (!filter.HasUsableDateRange)
            {
                var empty = new PagedResult<MonitoringAlert>(Array.Empty<MonitoringAlert>(), 1, filter.PageSize, 0);
                return View(new AlertListViewModel(empty, filter,
                    "The end date is before the start date, so no alerts can match. Adjust the range and apply the filters again."));
            }

            var alerts = await _analytics.GetAlertsAsync(teacherId.Value, filter.ToQueryFilter(selectedStatus));

            // Acting on the last group of a page leaves the teacher asking for a
            // page that no longer exists. The query already falls back to the last
            // real page, so move the address bar to match rather than showing page
            // 2 under a page=5 URL that would then be shared or bookmarked.
            if (alerts.Page != filter.Page)
            {
                return RedirectToAction(nameof(Alerts), filter.ToRouteValues(alerts.Page));
            }

            return View(new AlertListViewModel(alerts, filter));
        }

        [HttpGet]
        public async Task<IActionResult> OpenAlertCount()
        {
            if (!CheckAccess()) return Unauthorized();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Unauthorized();
            // Shares one implementation with the sidebar badge, so the two can no
            // longer drift apart as they did when the student scope changed.
            var count = await _analytics.GetOpenAlertGroupCountAsync(teacherId.Value, HttpContext.RequestAborted);
            return Json(new { count });
        }

        [HttpGet]
        public async Task<IActionResult> AlertHistory(int id)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var history = await _analytics.GetAlertHistoryAsync(id, teacherId.Value);
            if (history.Count == 0) return NotFound();
            return View(history);
        }

        [HttpGet]
        public async Task<IActionResult> ExportAlertsCsv([FromQuery] AlertListFilter filter)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            // The export previously ignored includeAcknowledged and defaulted to
            // every status, so a list filtered to open alerts exported handled
            // ones too. Resolving through the same filter keeps the file equal to
            // what the teacher is looking at.
            if (!filter.TryResolveStatus(out var selectedStatus)) return BadRequest("Invalid alert status.");
            if (!filter.HasUsableDateRange) return BadRequest("The end date is before the start date.");

            filter.Page = 1;
            filter.PageSize = AlertListFilter.MaxPageSize;
            var alerts = await _analytics.GetAlertExportAsync(teacherId.Value, filter.ToQueryFilter(selectedStatus));
            var csv = new System.Text.StringBuilder("First Seen,Last Seen,Student ID,Station,Severity,Title,Message,Occurrences,Status\n");
            foreach (var alert in alerts)
                csv.AppendLine($"{alert.FirstSeenAt:O},{alert.LastSeenAt:O},{Csv(alert.StudentId)},{Csv(alert.PcName)},{Csv(alert.Severity)},{Csv(alert.Title)},{Csv(alert.Message)},{alert.OccurrenceCount},{alert.Status}");
            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"CAMS-Alerts-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AcknowledgeAlert(int id, bool acknowledged, [FromForm] AlertListFilter filter)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            if (id <= 0 || !await _analytics.SetAlertAcknowledgedAsync(id, teacherId.Value, acknowledged)) return NotFound();

            // Say what happened, because the group may drop out of a filtered list
            // as a result and silence would read as the action having failed.
            TempData["Message"] = acknowledged ? "Alert group acknowledged." : "Alert group reopened.";
            return RedirectToAction(nameof(Alerts), filter.ToRouteValues());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkAcknowledgeAlerts(List<int>? alertIds, [FromForm] AlertListFilter filter) =>
            await ChangeAlertGroups(alertIds, filter, ids => _analytics.AcknowledgeAlertsAsync(ids, HttpContext.Session.GetInt32("TeacherId")!.Value));

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDismissAlerts(List<int>? alertIds, string? reason, [FromForm] AlertListFilter filter) =>
            await ChangeAlertGroups(alertIds, filter, ids => _analytics.DismissAlertsAsync(ids, HttpContext.Session.GetInt32("TeacherId")!.Value, reason));

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkReopenAlerts(List<int>? alertIds, [FromForm] AlertListFilter filter) =>
            await ChangeAlertGroups(alertIds, filter, ids => _analytics.ReopenAlertsAsync(ids, HttpContext.Session.GetInt32("TeacherId")!.Value));

        private async Task<IActionResult> ChangeAlertGroups(
            List<int>? alertIds,
            AlertListFilter filter,
            Func<IReadOnlyCollection<int>, Task<AlertBulkActionResult>> change)
        {
            if (!CheckAccess()) return Denied();
            if (!HttpContext.Session.GetInt32("TeacherId").HasValue) return Denied();
            var ids = alertIds?.Where(id => id > 0).Distinct().Take(AlertListFilter.MaxPageSize).ToList() ?? new List<int>();
            if (ids.Count == 0)
            {
                TempData["ErrorMessage"] = "Select at least one alert group.";
                return RedirectToAction(nameof(Alerts), filter.ToRouteValues());
            }
            var result = await change(ids);
            TempData["Message"] = $"Updated {result.ChangedGroupCount} alert group(s).";
            return RedirectToAction(nameof(Alerts), filter.ToRouteValues());
        }

        // ---------- Student Management ----------
        public async Task<IActionResult> Students(string? search = null)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            var query = AccessibleStudents(teacherId.Value)
                .Include(student => student.Class)
                .AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(student =>
                    student.StudentNumber.ToLower().Contains(term) ||
                    student.FirstName.ToLower().Contains(term) ||
                    student.LastName.ToLower().Contains(term) ||
                    student.FullName.ToLower().Contains(term) ||
                    student.Username.ToLower().Contains(term));
            }

            ViewBag.Search = search?.Trim();
            var students = await query
                .OrderBy(student => student.LastName)
                .ThenBy(student => student.FirstName)
                .ToListAsync();
            return View(students);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateStudent(
            [Bind("StudentNumber,FirstName,LastName,FullName,Username,PasswordHash")] Student student,
            int? classId = null)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue || !classId.HasValue)
            {
                TempData["ErrorMessage"] = "Create a student from one of your class rosters so the student is assigned immediately.";
                return RedirectToAction("Students");
            }

            var result = await _classManagement.CreateStudentInClassAsync(
                classId.Value,
                new NewStudentInput(student.StudentNumber, student.FirstName, student.LastName, student.FullName, student.Username, student.PasswordHash),
                teacherId.Value);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
                return RedirectToAction("Students");
            }

            await AuditAsync("CreateStudent", $"Created student {result.Name} in class {classId}");
            TempData["Message"] = $"Student '{result.Name}' registered successfully!";
            return RedirectToAction("Students");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStudent(
            [Bind("Id,StudentNumber,FirstName,LastName,FullName,Username")] Student student,
            string? newPassword)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            var existing = teacherId.HasValue
                ? await AccessibleStudents(teacherId.Value).FirstOrDefaultAsync(s => s.Id == student.Id)
                : null;
            if (existing == null) return NotFound();

            var requestedStudentNumber = string.IsNullOrWhiteSpace(student.StudentNumber) ? existing.StudentNumber : student.StudentNumber.Trim();
            var requestedUsername = string.IsNullOrWhiteSpace(student.Username) ? existing.Username : student.Username.Trim();
            if (await LoginIdentifierInUseAsync(requestedStudentNumber, existing.Id) ||
                await LoginIdentifierInUseAsync(requestedUsername, existing.Id))
            {
                TempData["ErrorMessage"] = "The student number or username is already in use.";
                return RedirectToAction("Students");
            }

            existing.StudentNumber = requestedStudentNumber;
            existing.Username = requestedUsername;

            if (!string.IsNullOrWhiteSpace(student.FullName))
            {
                existing.FullName = student.FullName.Trim();
                var parts = student.FullName.Trim().Split(' ', 2);
                existing.FirstName = parts.Length > 0 ? parts[0] : "";
                existing.LastName = parts.Length > 1 ? parts[1] : "";
            }
            else
            {
                existing.FirstName = string.IsNullOrWhiteSpace(student.FirstName) ? existing.FirstName : student.FirstName.Trim();
                existing.LastName = string.IsNullOrWhiteSpace(student.LastName) ? existing.LastName : student.LastName.Trim();
                existing.FullName = $"{existing.FirstName} {existing.LastName}".Trim();
            }

            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<object>();
                existing.PasswordHash = hasher.HashPassword(new object(), newPassword.Trim());
            }

            await _context.SaveChangesAsync();
            await AuditAsync("UpdateStudent", $"Updated student {existing.Id} ({existing.StudentNumber})");
            TempData["Message"] = $"Student '{existing.FullName}' updated successfully!";
            return RedirectToAction("Students");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteStudent(int studentId)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            var existing = await AccessibleStudents(teacherId.Value)
                .FirstOrDefaultAsync(student => student.Id == studentId);
            if (existing == null) return NotFound();

            var accessibleClassIds = await _context.Classes
                .Where(cls => cls.TeacherId == teacherId.Value &&
                              !cls.IsArchived &&
                              (cls.Status == "Active" || string.IsNullOrEmpty(cls.Status)) &&
                              (cls.ClassId == existing.ClassId ||
                               _context.ClassStudents.Any(link => link.ClassId == cls.ClassId && link.StudentId == existing.Id)))
                .Select(cls => cls.ClassId)
                .ToListAsync();

            foreach (var classId in accessibleClassIds)
            {
                var result = await _classManagement.RemoveStudentAsync(classId, studentId, teacherId.Value);
                if (!result.Success)
                {
                    TempData["ErrorMessage"] = result.Error;
                    return RedirectToAction(nameof(Students));
                }
            }

            if (existing.AdviserId == teacherId.Value)
            {
                existing.AdviserId = null;
                await _context.SaveChangesAsync();
            }

            await AuditAsync("RemoveStudent", $"Removed student {studentId} from the teacher's roster");
            TempData["Message"] = $"Student '{existing.FullName}' removed from your roster. The account was preserved.";
            return RedirectToAction(nameof(Students));
        }

        // ---------- Computer Management ----------
        public async Task<IActionResult> Computers()
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            var students = await AccessibleStudents(teacherId.Value)
                .AsNoTracking()
                .OrderBy(student => student.LastName)
                .ThenBy(student => student.FirstName)
                .ToListAsync();
            var studentIds = students.Select(student => student.Id).ToList();
            ViewBag.StudentNames = students.ToDictionary(student => student.Id.ToString(), student => student.FullName);
            var computers = await AccessibleComputers(studentIds)
                .AsNoTracking()
                .OrderBy(computer => computer.LaboratoryStation)
                .ToListAsync();
            return View(computers);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateComputer([Bind("ComputerId,LaboratoryStation,Status")] Computer computer)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            var studentIds = await AccessibleStudents(teacherId.Value)
                .Select(student => student.Id)
                .ToListAsync();
            var existing = await AccessibleComputers(studentIds)
                .FirstOrDefaultAsync(candidate => candidate.ComputerId == computer.ComputerId);
            if (existing == null) return NotFound();

            var station = computer.LaboratoryStation?.Trim();
            if (string.IsNullOrWhiteSpace(station) || station.Length > 50)
            {
                TempData["ErrorMessage"] = "A workstation name of 50 characters or fewer is required.";
                return RedirectToAction(nameof(Computers));
            }

            var status = NormalizeComputerStatus(computer.Status);
            if (status == null)
            {
                TempData["ErrorMessage"] = "Choose a valid workstation status.";
                return RedirectToAction(nameof(Computers));
            }

            var previousStatus = existing.Status;
            existing.LaboratoryStation = station;
            existing.Status = status;
            if (!string.Equals(previousStatus, existing.Status, StringComparison.OrdinalIgnoreCase))
            {
                _context.ComputerStatusHistories.Add(new ComputerStatusHistory
                {
                    ComputerId = existing.ComputerId,
                    Status = existing.Status,
                    ChangedByType = "Teacher",
                    ChangedById = teacherId.Value
                });
            }

            await _context.SaveChangesAsync();
            await AuditAsync("UpdateComputer", $"Updated computer {existing.ComputerId} ({existing.LaboratoryStation})");
            TempData["Message"] = $"Workstation '{existing.LaboratoryStation}' updated successfully!";
            return RedirectToAction(nameof(Computers));
        }

        // ---------- Notifications ----------
        [HttpPost]
        public async Task<IActionResult> SendNotification(string type, string title, string message)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message) ||
                title.Length > 120 || message.Length > 2000) return BadRequest();
            var studentIds = await _context.Students.Select(student => student.Id).ToListAsync();
            _context.Notifications.AddRange(studentIds.Select(studentId => new Notification
                {
                    StudentId = studentId,
                    Type = type,
                    Title = title.Trim(),
                    Message = message.Trim(),
                    CreatedAt = DateTime.UtcNow
                }));
            await _context.SaveChangesAsync();
            return Json(new { Ok = true });
        }

        // ---------- Class Management ----------
        public async Task<IActionResult> Classes()
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var classes = await _classManagement.GetClassesAsync(teacherId.Value);

            return View(classes);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateClass(Class cls)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue)
            {
                return Denied();
            }

            var result = await _classManagement.CreateClassAsync(
                new ClassInput(cls.ClassName, cls.Section, cls.Subject, cls.GradeLevel, cls.Schedule, cls.AcademicYear, teacherId),
                teacherId,
                isAdmin: false);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
                return RedirectToAction("Classes");
            }

            await AuditAsync("ClassCreated", $"Created class '{result.Name}'");
            TempData["Message"] = $"Class '{result.Name}' created successfully!";
            return RedirectToAction("Classes");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateClass(Class cls)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            var result = await _classManagement.UpdateClassAsync(
                cls.ClassId,
                new ClassInput(cls.ClassName, cls.Section, cls.Subject, cls.GradeLevel, cls.Schedule, cls.AcademicYear, teacherId),
                teacherId,
                isAdmin: false);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
                return RedirectToAction("Classes");
            }

            await AuditAsync("ClassUpdated", $"Updated class '{result.Name}'");
            TempData["Message"] = $"Class '{result.Name}' updated successfully!";
            return RedirectToAction("Classes");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult AssignTeacher(int classId, int? teacherId)
        {
            if (!CheckAccess()) return Denied();
            TempData["ErrorMessage"] = "Only administrators can assign or reassign teachers.";
            return RedirectToAction("ClassDetails", new { id = classId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ArchiveClass(int id)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            var existing = await _classManagement.GetClassAsync(id, teacherId.Value);
            if (existing == null)
            {
                TempData["ErrorMessage"] = "The class was not found or is not assigned to you.";
                return RedirectToAction("Classes");
            }

            var archived = !existing.IsArchived;
            var result = await _classManagement.SetArchiveStateAsync(id, archived, teacherId.Value);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
            }
            else
            {
                var state = archived ? "archived" : "restored";
                await AuditAsync(archived ? "ClassArchived" : "ClassRestored", $"{state} class '{result.Name}'");
                TempData["Message"] = $"Class '{result.Name}' {state} successfully.";
            }
            return RedirectToAction("Classes");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteClass(int classId)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            var result = await _classManagement.DeleteClassAsync(classId, teacherId.Value);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
            }
            else
            {
                await AuditAsync("ClassDeleted", $"Deleted class '{result.Name}'");
                TempData["Message"] = $"Class '{result.Name}' deleted successfully.";
            }
            return RedirectToAction("Classes");
        }

        public async Task<IActionResult> ClassDetails(int id)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var cls = await _classManagement.GetClassAsync(id, teacherId.Value);
            if (cls == null) return RedirectToAction("Classes");

            await _classManagement.EnsureMembershipLinksAsync(id);
            var roster = await _classManagement.GetRosterAsync(id);
            cls.ClassStudents = roster.ToList();
            ViewBag.EnrolledStudents = roster;
            ViewBag.AllStudents = await AccessibleStudents(teacherId.Value)
                .Include(s => s.Class)
                .OrderBy(s => s.LastName)
                .ThenBy(s => s.FirstName)
                .ToListAsync();
            return View(cls);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddStudentToClass(int classId, string firstName, string lastName, string? username, string? password)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            var result = await _classManagement.CreateStudentInClassAsync(
                classId,
                new NewStudentInput(null, firstName, lastName, null, username, password),
                teacherId.Value);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
                return RedirectToAction("ClassDetails", new { id = classId });
            }

            await AuditAsync("AddStudentToClass", $"Added student {result.Name} to class {classId}");
            TempData["Message"] = $"Student '{result.Name}' added successfully!";
            return RedirectToAction("ClassDetails", new { id = classId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkAddStudents(int classId, List<string>? bulkFirstNames, List<string>? bulkLastNames, List<string>? bulkUserNames, List<string>? bulkPasswords)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            var rowCount = new[]
            {
                bulkFirstNames?.Count ?? 0,
                bulkLastNames?.Count ?? 0,
                bulkUserNames?.Count ?? 0,
                bulkPasswords?.Count ?? 0
            }.Max();
            var rows = Enumerable.Range(0, rowCount)
                .Select(i => new NewStudentInput(
                    null,
                    i < (bulkFirstNames?.Count ?? 0) ? bulkFirstNames![i] : null,
                    i < (bulkLastNames?.Count ?? 0) ? bulkLastNames![i] : null,
                    null,
                    i < (bulkUserNames?.Count ?? 0) ? bulkUserNames![i] : null,
                    i < (bulkPasswords?.Count ?? 0) ? bulkPasswords![i] : null))
                .ToList();

            var result = await _classManagement.BulkCreateStudentsInClassAsync(classId, rows, teacherId.Value);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
                return RedirectToAction("ClassDetails", new { id = classId });
            }

            await AuditAsync("BulkAddStudents", $"Bulk added {result.Count} students to class {classId}");
            TempData["Message"] = $"Successfully added {result.Count} student(s) to the class.";
            return RedirectToAction("ClassDetails", new { id = classId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkPreviewCsv(int classId, IFormFile? file)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue || file == null || file.Length == 0) return RedirectToAction("ClassDetails", new { id = classId });
            using var reader = new StreamReader(file.OpenReadStream());
            var parsed = _classManagement.ParseBulkStudentsCsv(await reader.ReadToEndAsync());
            var import = await _classManagement.ValidateBulkStudentsAsync(classId, parsed.Rows, teacherId.Value);
            if (import.Errors.Count > 0) return File(BulkErrorCsv(import.Errors), "text/csv; charset=utf-8", $"CAMS-Student-Import-Errors-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
            var result = await _classManagement.BulkCreateStudentsInClassAsync(classId, import.Rows, teacherId.Value);
            TempData[result.Success ? "Message" : "ErrorMessage"] = result.Success ? $"Successfully added {result.Count} student(s) to the class." : result.Error;
            return RedirectToAction("ClassDetails", new { id = classId });
        }

        private static byte[] BulkErrorCsv(IEnumerable<BulkStudentRow> errors)
        {
            var csv = new System.Text.StringBuilder("Row,Student Number,First Name,Last Name,Full Name,Username,Error\n");
            foreach (var row in errors) csv.AppendLine($"{row.RowNumber},{Csv(row.Input.StudentNumber)},{Csv(row.Input.FirstName)},{Csv(row.Input.LastName)},{Csv(row.Input.FullName)},{Csv(row.Input.Username)},{Csv(row.Error)}");
            return System.Text.Encoding.UTF8.GetBytes(csv.ToString());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EnrollStudent(int classId, int studentId, bool moveStudent = false)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            if (!await AccessibleStudents(teacherId.Value).AnyAsync(student => student.Id == studentId))
            {
                return NotFound();
            }

            var result = await _classManagement.EnrollExistingStudentAsync(classId, studentId, moveStudent, teacherId.Value);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
            }
            else
            {
                await AuditAsync("EnrollStudent", $"Enrolled student {studentId} in class {classId}");
                TempData["Message"] = $"Student '{result.Name}' enrolled successfully.";
            }
            return RedirectToAction("ClassDetails", new { id = classId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EnrollStudents(int classId, List<int>? studentIds, bool moveStudent = false)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            var ids = studentIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            if (ids.Count == 0)
            {
                TempData["ErrorMessage"] = "Select at least one student to enroll.";
                return RedirectToAction("ClassDetails", new { id = classId });
            }

            var enrolled = 0;
            var failures = new List<string>();
            foreach (var studentId in ids)
            {
                if (!await AccessibleStudents(teacherId.Value).AnyAsync(student => student.Id == studentId)) continue;
                var result = await _classManagement.EnrollExistingStudentAsync(classId, studentId, moveStudent, teacherId.Value);
                if (result.Success) enrolled++;
                else failures.Add(result.Error ?? $"Student {studentId} could not be enrolled.");
            }

            if (enrolled > 0)
            {
                await AuditAsync("EnrollStudents", $"Enrolled {enrolled} student(s) in class {classId}");
                TempData["Message"] = $"Enrolled {enrolled} student(s) successfully.";
            }
            if (failures.Count > 0)
            {
                TempData["ErrorMessage"] = string.Join(" ", failures.Take(3));
            }
            return RedirectToAction("ClassDetails", new { id = classId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveStudent(int classId, int studentId)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            if (!await AccessibleStudents(teacherId.Value).AnyAsync(student => student.Id == studentId))
            {
                return NotFound();
            }

            var result = await _classManagement.RemoveStudentAsync(classId, studentId, teacherId.Value);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
            }
            else
            {
                await AuditAsync("RemoveStudent", $"Removed student {studentId} from class {classId}");
                TempData["Message"] = "Student removed from class.";
            }
            return RedirectToAction("ClassDetails", new { id = classId });
        }
    }
}
