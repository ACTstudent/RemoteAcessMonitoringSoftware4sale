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
            _context.Teachers.Add(teacher);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateTeacher", $"Created teacher {teacher.Username}");
            return RedirectToAction("Teachers");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateTeacher(Teacher teacher)
        {
            if (!CheckAccess()) return Denied();
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
            return View(await _context.Students.ToListAsync());
        }

        [HttpPost]
        public async Task<IActionResult> CreateStudent(Student student)
        {
            if (!CheckAccess()) return Denied();
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
            return RedirectToAction("Blacklists");
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
            }
            return RedirectToAction("Blacklists");
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
            }
            return RedirectToAction("Computers");
        }

        // ---------- Reports ----------
        public async Task<IActionResult> Reports()
        {
            if (!CheckAccess()) return Denied();

            var sessions = await _context.LabSessions
                .Include(s => s.Student)
                .Include(s => s.Teacher)
                .Include(s => s.Computer)
                .OrderByDescending(s => s.StartTime)
                .Take(500)
                .ToListAsync();

            ViewBag.TotalSessions = sessions.Count;
            ViewBag.TotalMinutes = sessions.Sum(s =>
                (s.EndTime.HasValue ? (s.EndTime.Value - s.StartTime).TotalMinutes : 0));
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
