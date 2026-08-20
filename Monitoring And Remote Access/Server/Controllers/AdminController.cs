using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Controllers
{
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PasswordHasher<object> _hasher = new();

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        private bool CheckAccess() => HttpContext.IsAdmin();

        private IActionResult Denied() => RedirectToAction("Login", "Account");

        private async Task AuditAsync(string action, string details)
        {
            _context.AuditLogs.Add(new AuditLog
            {
                UserType = "Admin",
                UserId = HttpContext.Session.GetInt32("AdminId"),
                Action = action,
                Details = details,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Timestamp = DateTime.Now
            });
            await _context.SaveChangesAsync();
        }

        // ---------- Dashboard ----------
        public async Task<IActionResult> Index()
        {
            if (!CheckAccess()) return Denied();

            ViewBag.StudentCount = await _context.Students.CountAsync();
            ViewBag.TeacherCount = await _context.Teachers.CountAsync();
            ViewBag.ComputerCount = await _context.Computers.CountAsync();
            ViewBag.ActiveSessions = await _context.LabSessions.CountAsync(s => s.IsActive);
            return View();
        }

        // ---------- Teacher accounts ----------
        public async Task<IActionResult> Teachers()
        {
            if (!CheckAccess()) return Denied();
            return View(await _context.Teachers.ToListAsync());
        }

        [HttpPost]
        public async Task<IActionResult> CreateTeacher(Teacher teacher)
        {
            if (!CheckAccess()) return Denied();
            teacher.PasswordHash = _hasher.HashPassword(null, teacher.PasswordHash);
            _context.Teachers.Add(teacher);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateTeacher", $"Created teacher {teacher.Username}");
            return RedirectToAction("Teachers");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateTeacher(Teacher teacher)
        {
            if (!CheckAccess()) return Denied();
            teacher.PasswordHash = _hasher.HashPassword(null, teacher.PasswordHash);
            _context.Teachers.Update(teacher);
            await _context.SaveChangesAsync();
            await AuditAsync("UpdateTeacher", $"Updated teacher {teacher.Username}");
            return RedirectToAction("Teachers");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteTeacher(int id)
        {
            if (!CheckAccess()) return Denied();
            var teacher = await _context.Teachers.FindAsync(id);
            if (teacher != null)
            {
                _context.Teachers.Remove(teacher);
                await _context.SaveChangesAsync();
                await AuditAsync("DeleteTeacher", $"Deleted teacher {teacher.Username}");
            }
            return RedirectToAction("Teachers");
        }

        // ---------- Student accounts ----------
        public async Task<IActionResult> Students()
        {
            if (!CheckAccess()) return Denied();
            ViewBag.Computers = await _context.Computers.ToListAsync();
            return View(await _context.Students.ToListAsync());
        }

        [HttpPost]
        public async Task<IActionResult> CreateStudent(Student student)
        {
            if (!CheckAccess()) return Denied();
            student.PasswordHash = _hasher.HashPassword(null, student.PasswordHash);
            _context.Students.Add(student);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateStudent", $"Created student {student.Username}");
            return RedirectToAction("Students");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStudent(Student student)
        {
            if (!CheckAccess()) return Denied();
            _context.Students.Update(student);
            await _context.SaveChangesAsync();
            await AuditAsync("UpdateStudent", $"Updated student {student.Username}");
            return RedirectToAction("Students");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteStudent(int id)
        {
            if (!CheckAccess()) return Denied();
            var student = await _context.Students.FindAsync(id);
            if (student != null)
            {
                _context.Students.Remove(student);
                await _context.SaveChangesAsync();
                await AuditAsync("DeleteStudent", $"Deleted student {student.Username}");
            }
            return RedirectToAction("Students");
        }

        // ---------- Roles & Permissions ----------
        public async Task<IActionResult> Roles()
        {
            if (!CheckAccess()) return Denied();
            var roles = await _context.Roles
                .Include(r => r.Permissions)
                .ToListAsync();
            ViewBag.Permissions = await _context.Permissions.ToListAsync();
            return View(roles);
        }

        [HttpPost]
        public async Task<IActionResult> CreateRole(Role role)
        {
            if (!CheckAccess()) return Denied();
            _context.Roles.Add(role);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateRole", $"Created role {role.Name}");
            return RedirectToAction("Roles");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteRole(int id)
        {
            if (!CheckAccess()) return Denied();
            var role = await _context.Roles.FindAsync(id);
            if (role != null)
            {
                _context.Roles.Remove(role);
                await _context.SaveChangesAsync();
                await AuditAsync("DeleteRole", $"Deleted role {role.Name}");
            }
            return RedirectToAction("Roles");
        }

        // ---------- Restriction rules ----------
        public async Task<IActionResult> Restrictions()
        {
            if (!CheckAccess()) return Denied();
            return View(await _context.RestrictionRules.OrderByDescending(r => r.CreatedAt).ToListAsync());
        }

        [HttpPost]
        public async Task<IActionResult> CreateRestriction(RestrictionRule rule)
        {
            if (!CheckAccess()) return Denied();
            _context.RestrictionRules.Add(rule);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateRestriction", $"Added restriction on {rule.Target}");
            return RedirectToAction("Restrictions");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteRestriction(int id)
        {
            if (!CheckAccess()) return Denied();
            var rule = await _context.RestrictionRules.FindAsync(id);
            if (rule != null)
            {
                _context.RestrictionRules.Remove(rule);
                await _context.SaveChangesAsync();
                await AuditAsync("DeleteRestriction", $"Removed restriction on {rule.Target}");
            }
            return RedirectToAction("Restrictions");
        }

        // ---------- Blacklists ----------
        public async Task<IActionResult> Blacklists()
        {
            if (!CheckAccess()) return Denied();
            return View(await _context.BlacklistItems.OrderByDescending(b => b.CreatedAt).ToListAsync());
        }

        [HttpPost]
        public async Task<IActionResult> CreateBlacklist(BlacklistItem item)
        {
            if (!CheckAccess()) return Denied();
            _context.BlacklistItems.Add(item);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateBlacklist", $"Blacklisted {item.TargetType}: {item.Value}");
            return RedirectToAction(nameof(Blacklists));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteBlacklist(int id)
        {
            if (!CheckAccess()) return Denied();
            var item = await _context.BlacklistItems.FindAsync(id);
            if (item != null)
            {
                _context.BlacklistItems.Remove(item);
                await _context.SaveChangesAsync();
                await AuditAsync("DeleteBlacklist", $"Removed blacklist {item.TargetType}: {item.Value}");
            }
            return RedirectToAction(nameof(Blacklists));
        }

        // ---------- Session rules ----------
        public async Task<IActionResult> SessionRules()
        {
            if (!CheckAccess()) return Denied();
            return View(await _context.SessionRules.OrderByDescending(s => s.CreatedAt).ToListAsync());
        }

        [HttpPost]
        public async Task<IActionResult> CreateSessionRule(SessionRule rule)
        {
            if (!CheckAccess()) return Denied();
            if (rule.IsDefault)
            {
                var defaults = _context.SessionRules.Where(s => s.IsDefault);
                foreach (var d in defaults) d.IsDefault = false;
            }
            _context.SessionRules.Add(rule);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateSessionRule", $"Created session rule {rule.Name}");
            return RedirectToAction("SessionRules");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteSessionRule(int id)
        {
            if (!CheckAccess()) return Denied();
            var rule = await _context.SessionRules.FindAsync(id);
            if (rule != null)
            {
                _context.SessionRules.Remove(rule);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("SessionRules");
        }

        // ---------- LAN configuration ----------
        public async Task<IActionResult> LanConfig()
        {
            if (!CheckAccess()) return Denied();
            var config = await _context.LanConfigurations.FirstOrDefaultAsync();
            return View(config ?? new LanConfiguration());
        }

        [HttpPost]
        public async Task<IActionResult> SaveLanConfig(LanConfiguration config)
        {
            if (!CheckAccess()) return Denied();
            config.UpdatedAt = DateTime.Now;
            var existing = await _context.LanConfigurations.FirstOrDefaultAsync();
            if (existing == null)
            {
                _context.LanConfigurations.Add(config);
            }
            else
            {
                existing.ServerAddress = config.ServerAddress;
                existing.ServerPort = config.ServerPort;
                existing.DhcpRangeStart = config.DhcpRangeStart;
                existing.DhcpRangeEnd = config.DhcpRangeEnd;
                existing.Gateway = config.Gateway;
                existing.DnsServer = config.DnsServer;
                existing.IsActive = config.IsActive;
                existing.UpdatedAt = config.UpdatedAt;
            }
            await _context.SaveChangesAsync();
            await AuditAsync("SaveLanConfig", "Updated LAN configuration");
            return RedirectToAction("LanConfig");
        }

        // ---------- Computers ----------
        public async Task<IActionResult> Computers()
        {
            if (!CheckAccess()) return Denied();
            return View(await _context.Computers.ToListAsync());
        }

        [HttpPost]
        public async Task<IActionResult> CreateComputer(Computer computer)
        {
            if (!CheckAccess()) return Denied();
            _context.Computers.Add(computer);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateComputer", $"Added {computer.LaboratoryStation}");
            return RedirectToAction("Computers");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteComputer(int id)
        {
            if (!CheckAccess()) return Denied();
            var computer = await _context.Computers.FindAsync(id);
            if (computer != null)
            {
                _context.Computers.Remove(computer);
                await _context.SaveChangesAsync();
                await AuditAsync("DeleteComputer", $"Removed {computer.LaboratoryStation}");
            }
            return RedirectToAction(nameof(Computers));
        }

        // ---------- Workstation-to-Student mapping ----------
        [HttpPost]
        public async Task<IActionResult> AssignComputer(int studentId, int? computerId)
        {
            if (!CheckAccess()) return Denied();

            // Un-assign the student from any previously assigned workstation
            var previous = await _context.Computers
                .FirstOrDefaultAsync(c => c.AssignedTo == studentId.ToString());
            if (previous != null)
            {
                previous.AssignedTo = null;
                if (previous.Status == "Assigned") previous.Status = "Available";
            }

            if (computerId.HasValue)
            {
                var computer = await _context.Computers.FindAsync(computerId.Value);
                if (computer != null)
                {
                    computer.AssignedTo = studentId.ToString();
                    computer.Status = "Assigned";
                }
            }

            await _context.SaveChangesAsync();
            await AuditAsync("AssignComputer",
                computerId.HasValue ? $"Assigned student {studentId} to workstation {computerId}" : $"Unassigned student {studentId}");
            return RedirectToAction("Students");
        }

        // ---------- Class Management ----------
        public async Task<IActionResult> Classes()
        {
            if (!CheckAccess()) return Denied();
            var classes = await _context.Classes
                .Include(c => c.ClassStudents)
                .OrderBy(c => c.ClassName)
                .ToListAsync();
            return View(classes);
        }

        [HttpPost]
        public async Task<IActionResult> CreateClass(Class cls)
        {
            if (!CheckAccess()) return Denied();
            cls.CreatedAt = DateTime.Now;
            _context.Classes.Add(cls);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateClass", $"Created class '{cls.ClassName}'");
            return RedirectToAction("Classes");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateClass(Class cls)
        {
            if (!CheckAccess()) return Denied();
            var existing = await _context.Classes.FindAsync(cls.ClassId);
            if (existing == null) return RedirectToAction("Classes");
            existing.ClassName = cls.ClassName;
            existing.Section = cls.Section;
            existing.Subject = cls.Subject;
            existing.GradeLevel = cls.GradeLevel;
            existing.Schedule = cls.Schedule;
            await _context.SaveChangesAsync();
            await AuditAsync("UpdateClass", $"Updated class '{cls.ClassName}'");
            return RedirectToAction("Classes");
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveClass(int id)
        {
            if (!CheckAccess()) return Denied();
            var existing = await _context.Classes.FindAsync(id);
            if (existing != null)
            {
                existing.IsArchived = true;
                await _context.SaveChangesAsync();
                await AuditAsync("ArchiveClass", $"Archived class '{existing.ClassName}'");
            }
            return RedirectToAction("Classes");
        }

        public async Task<IActionResult> ClassDetails(int id)
        {
            if (!CheckAccess()) return Denied();
            var cls = await _context.Classes
                .Include(c => c.ClassStudents)
                    .ThenInclude(cs => cs.Student)
                .FirstOrDefaultAsync(c => c.ClassId == id);
            if (cls == null) return RedirectToAction("Classes");
            ViewBag.AllStudents = await _context.Students.OrderBy(s => s.FullName).ToListAsync();
            return View(cls);
        }

        [HttpPost]
        public async Task<IActionResult> EnrollStudent(int classId, int studentId)
        {
            if (!CheckAccess()) return Denied();
            var exists = await _context.ClassStudents
                .AnyAsync(cs => cs.ClassId == classId && cs.StudentId == studentId);
            if (!exists)
            {
                _context.ClassStudents.Add(new ClassStudent { ClassId = classId, StudentId = studentId });
                await _context.SaveChangesAsync();
                await AuditAsync("EnrollStudent", $"Enrolled student {studentId} in class {classId}");
            }
            return RedirectToAction("ClassDetails", new { id = classId });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveStudent(int classId, int studentId)
        {
            if (!CheckAccess()) return Denied();
            var cs = await _context.ClassStudents
                .FirstOrDefaultAsync(cs => cs.ClassId == classId && cs.StudentId == studentId);
            if (cs != null)
            {
                _context.ClassStudents.Remove(cs);
                await _context.SaveChangesAsync();
                await AuditAsync("RemoveStudent", $"Removed student {studentId} from class {classId}");
            }
            return RedirectToAction("ClassDetails", new { id = classId });
        }

        // ---------- Reports ----------
        public async Task<IActionResult> Reports(DateTime? from, DateTime? to)
        {
            if (!CheckAccess()) return Denied();

            var fromDate = from ?? DateTime.Now.AddDays(-30);
            var toDate = to ?? DateTime.Now.AddDays(1);

            var sessions = await _context.LabSessions
                .Include(s => s.Student)
                .Include(s => s.Teacher)
                .Include(s => s.Computer)
                .Where(s => s.StartTime >= fromDate && s.StartTime < toDate)
                .OrderByDescending(s => s.StartTime)
                .Take(500)
                .ToListAsync();

            var usage = await _context.UsageLogs
                .Where(u => u.Timestamp >= fromDate && u.Timestamp < toDate)
                .OrderByDescending(u => u.Timestamp)
                .Take(500)
                .ToListAsync();

            ViewBag.FromDate = fromDate.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate.ToString("yyyy-MM-dd");
            ViewBag.TotalSessions = sessions.Count;
            ViewBag.TotalMinutes = sessions.Sum(s =>
                (s.EndTime.HasValue ? (s.EndTime.Value - s.StartTime).TotalMinutes : 0));
            ViewBag.TopApps = usage
                .GroupBy(u => u.AppName)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => new { App = g.Key, Count = g.Count() })
                .ToList();
            ViewBag.UsageLogs = usage;
            ViewBag.SessionsByTeacher = sessions
                .GroupBy(s => s.Teacher?.Username ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count());
            ViewBag.SessionsByStation = sessions
                .GroupBy(s => s.Computer?.LaboratoryStation ?? s.PCName ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count());

            return View(sessions);
        }

        // ---------- Audit trail ----------
        public async Task<IActionResult> AuditLogs()
        {
            if (!CheckAccess()) return Denied();
            return View(await _context.AuditLogs.OrderByDescending(a => a.Timestamp).Take(500).ToListAsync());
        }

        // ---------- Export audit trail as CSV ----------
        public async Task<IActionResult> ExportAuditCsv()
        {
            if (!CheckAccess()) return Denied();
            var logs = await _context.AuditLogs.OrderByDescending(a => a.Timestamp).Take(2000).ToListAsync();

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Timestamp,User Type,User ID,Action,Details,IP Address");
            foreach (var l in logs)
            {
                csv.AppendLine($"{l.Timestamp:yyyy-MM-dd HH:mm:ss},{Csv(l.UserType)},{l.UserId},{Csv(l.Action)},{Csv(l.Details)},{Csv(l.IpAddress)}");
            }

            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()),
                "text/csv; charset=utf-8",
                $"CAMS-AuditLog-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        }

        // ---------- Export usage report as CSV ----------
        public async Task<IActionResult> ExportReportsCsv()
        {
            if (!CheckAccess()) return Denied();
            var sessions = await _context.LabSessions
                .Include(s => s.Student)
                .Include(s => s.Teacher)
                .Include(s => s.Computer)
                .OrderByDescending(s => s.StartTime)
                .Take(2000)
                .ToListAsync();

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Student,Teacher,Station,Start Time,End Time,Duration (min),Status");
            foreach (var s in sessions)
            {
                var duration = s.EndTime.HasValue ? Math.Round((s.EndTime.Value - s.StartTime).TotalMinutes, 1) : 0;
                csv.AppendLine($"{Csv(s.Student?.FullName)},{Csv(s.Teacher?.Username ?? "System")},{Csv(s.Computer?.LaboratoryStation ?? s.PCName)},{s.StartTime:yyyy-MM-dd HH:mm},{s.EndTime?.ToString("yyyy-MM-dd HH:mm")},{duration},{s.Status}");
            }

            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()),
                "text/csv; charset=utf-8",
                $"CAMS-UsageReport-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> ExportUsageCsv(DateTime? from, DateTime? to)
        {
            if (!CheckAccess()) return Denied();
            var fromDate = from ?? DateTime.Now.AddDays(-30);
            var toDate = to ?? DateTime.Now.AddDays(1);
            var logs = await _context.UsageLogs
                .Where(u => u.Timestamp >= fromDate && u.Timestamp < toDate)
                .OrderByDescending(u => u.Timestamp)
                .Take(1000)
                .ToListAsync();

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Timestamp,Student ID,PC,Application");
            foreach (var l in logs)
                csv.AppendLine($"{l.Timestamp:yyyy-MM-dd HH:mm:ss},{l.StudentId},{Csv(l.PcName)},{Csv(l.AppName)}");

            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()),
                "text/csv; charset=utf-8",
                $"CAMS-UsageLog-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        }

        private static string Csv(string? value)
        {
            var v = value ?? "";
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        // ---------- System error logs ----------
        public async Task<IActionResult> SystemLogs()
        {
            if (!CheckAccess()) return Denied();
            return View(await _context.SystemLogs.OrderByDescending(l => l.Timestamp).Take(500).ToListAsync());
        }

        public async Task<IActionResult> LogSystem(string level, string message)
        {
            _context.SystemLogs.Add(new SystemLog { Level = level, Message = message });
            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}
