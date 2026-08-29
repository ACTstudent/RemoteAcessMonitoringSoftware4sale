using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Controllers
{
    [Authorize(Roles = "Admin")]
    [AutoValidateAntiforgeryToken]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PasswordHasher<object> _hasher = new();
        private readonly IClassManagementService _classManagement;

        public AdminController(ApplicationDbContext context, IClassManagementService? classManagement = null)
        {
            _context = context;
            _classManagement = classManagement ?? new ClassManagementService(context);
        }

        private bool CheckAccess() => HttpContext.IsAdmin();

        private static bool ValidMode(string? mode) => mode is "Block" or "Allow";
        private static bool ValidRuleType(string? type) => type is "Application" or "Website" or "BlockApplication" or "BlockWebsite";
        private static bool ValidBlacklistType(string? type) => type is "Application" or "Website" or "Domain" or "Process";
        private static string NormalizeRuleType(string type) => type switch
        {
            "BlockApplication" => "Application",
            "BlockWebsite" => "Website",
            _ => type.Trim()
        };

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
            return View(await _context.Teachers.OrderBy(t => t.LastName).ThenBy(t => t.FirstName).ToListAsync());
        }

        [HttpPost]
        public async Task<IActionResult> CreateTeacher(Teacher teacher)
        {
            if (!CheckAccess()) return Denied();
            
            if (string.IsNullOrWhiteSpace(teacher.Username))
            {
                TempData["ErrorMessage"] = "Username is required.";
                return RedirectToAction("Teachers");
            }
            if (string.IsNullOrWhiteSpace(teacher.PasswordHash))
            {
                TempData["ErrorMessage"] = "A password is required for a new teacher.";
                return RedirectToAction("Teachers");
            }

            teacher.Username = teacher.Username.Trim();
            if (await _context.Teachers.AnyAsync(t => t.Username.ToLower() == teacher.Username.ToLower()))
            {
                TempData["ErrorMessage"] = $"The username '{teacher.Username}' is already in use.";
                return RedirectToAction("Teachers");
            }

            teacher.FirstName = string.IsNullOrWhiteSpace(teacher.FirstName) ? teacher.Username : teacher.FirstName.Trim();
            teacher.LastName = string.IsNullOrWhiteSpace(teacher.LastName) ? "Teacher" : teacher.LastName.Trim();
            teacher.Status = string.IsNullOrWhiteSpace(teacher.Status) ? "Active" : teacher.Status.Trim();
            teacher.PasswordHash = _hasher.HashPassword(new object(), teacher.PasswordHash.Trim());
            
            _context.Teachers.Add(teacher);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateTeacher", $"Created teacher {teacher.Username}");
            TempData["Message"] = $"Teacher '{teacher.FirstName} {teacher.LastName}' registered successfully!";
            return RedirectToAction("Teachers");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateTeacher(Teacher teacher, string? newPassword)
        {
            if (!CheckAccess()) return Denied();
            var existing = await _context.Teachers.FindAsync(teacher.TeacherId);
            if (existing == null) return RedirectToAction("Teachers");

            var requestedUsername = string.IsNullOrWhiteSpace(teacher.Username) ? existing.Username : teacher.Username.Trim();
            if (await _context.Teachers.AnyAsync(t =>
                    t.TeacherId != existing.TeacherId &&
                    t.Username.ToLower() == requestedUsername.ToLower()))
            {
                TempData["ErrorMessage"] = $"The username '{requestedUsername}' is already in use.";
                return RedirectToAction("Teachers");
            }

            var requestedStatus = string.IsNullOrWhiteSpace(teacher.Status) ? existing.Status : teacher.Status.Trim();
            if (string.Equals(requestedStatus, "Inactive", StringComparison.OrdinalIgnoreCase) &&
                await _context.Classes.AnyAsync(c => c.TeacherId == existing.TeacherId && !c.IsArchived))
            {
                TempData["ErrorMessage"] = "Reassign or archive this teacher's active classes before deactivating the account.";
                return RedirectToAction("Teachers");
            }

            existing.FirstName = string.IsNullOrWhiteSpace(teacher.FirstName) ? existing.FirstName : teacher.FirstName.Trim();
            existing.LastName = string.IsNullOrWhiteSpace(teacher.LastName) ? existing.LastName : teacher.LastName.Trim();
            existing.Username = requestedUsername;
            existing.Email = string.IsNullOrWhiteSpace(teacher.Email) ? existing.Email : teacher.Email.Trim();
            existing.ContactNumber = string.IsNullOrWhiteSpace(teacher.ContactNumber) ? existing.ContactNumber : teacher.ContactNumber.Trim();
            existing.Status = requestedStatus;

            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                existing.PasswordHash = _hasher.HashPassword(new object(), newPassword.Trim());
            }

            await _context.SaveChangesAsync();
            await AuditAsync("UpdateTeacher", $"Updated teacher {existing.Username}");
            TempData["Message"] = $"Teacher '{existing.FirstName} {existing.LastName}' updated successfully!";
            return RedirectToAction("Teachers");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteTeacher(int id)
        {
            if (!CheckAccess()) return Denied();
            var teacher = await _context.Teachers.FindAsync(id);
            if (teacher != null)
            {
                var classes = await _context.Classes.Where(c => c.TeacherId == id).ToListAsync();
                if (classes.Any(c => !c.IsArchived))
                {
                    TempData["ErrorMessage"] = "Reassign or archive this teacher's active classes before deleting the account.";
                    return RedirectToAction("Teachers");
                }

                foreach (var c in classes) c.TeacherId = null;

                var students = await _context.Students.Where(s => s.AdviserId == id).ToListAsync();
                foreach (var st in students) st.AdviserId = null;

                _context.Teachers.Remove(teacher);
                await _context.SaveChangesAsync();
                await AuditAsync("DeleteTeacher", $"Deleted teacher {teacher.Username}");
                TempData["Message"] = $"Teacher '{teacher.Username}' deleted successfully!";
            }
            return RedirectToAction("Teachers");
        }

        // ---------- Student accounts ----------
        public async Task<IActionResult> Students()
        {
            if (!CheckAccess()) return Denied();
            ViewBag.Computers = await _context.Computers.ToListAsync();
            ViewBag.Classes = await _context.Classes
                .Where(c => !c.IsArchived && c.TeacherId.HasValue &&
                            (c.Teacher!.Status == "Active" || string.IsNullOrEmpty(c.Teacher.Status)))
                .Include(c => c.Teacher)
                .OrderBy(c => c.ClassName)
                .ToListAsync();
            return View(await _context.Students
                .Include(s => s.Class)
                .OrderBy(s => s.LastName)
                .ThenBy(s => s.FirstName)
                .ToListAsync());
        }

        [HttpPost]
        public async Task<IActionResult> CreateStudent(Student student)
        {
            if (!CheckAccess()) return Denied();

            if (string.IsNullOrWhiteSpace(student.Username))
            {
                TempData["ErrorMessage"] = "Username is required.";
                return RedirectToAction("Students");
            }
            if (string.IsNullOrWhiteSpace(student.PasswordHash))
            {
                TempData["ErrorMessage"] = "A password is required for a new student.";
                return RedirectToAction("Students");
            }

            student.Username = student.Username.Trim();
            if (await _context.Students.AnyAsync(s => s.Username.ToLower() == student.Username.ToLower()))
            {
                TempData["ErrorMessage"] = $"The username '{student.Username}' is already in use.";
                return RedirectToAction("Students");
            }

            student.PasswordHash = _hasher.HashPassword(new object(), student.PasswordHash.Trim());
            student.Status = string.IsNullOrWhiteSpace(student.Status) ? "Active" : student.Status;

            if (string.IsNullOrWhiteSpace(student.StudentNumber))
            {
                student.StudentNumber = $"STU-{DateTime.Now:yyyy}-{new Random().Next(100, 999)}";
            }

            if (await _context.Students.AnyAsync(s => s.StudentNumber.ToLower() == student.StudentNumber.ToLower()))
            {
                TempData["ErrorMessage"] = $"The student number '{student.StudentNumber}' is already in use.";
                return RedirectToAction("Students");
            }

            if (!string.IsNullOrWhiteSpace(student.FullName))
            {
                student.FullName = student.FullName.Trim();
                var parts = student.FullName.Split(' ', 2);
                student.FirstName = parts.Length > 0 ? parts[0] : "Student";
                student.LastName = parts.Length > 1 ? parts[1] : "";
            }
            else
            {
                student.FirstName = string.IsNullOrWhiteSpace(student.FirstName) ? "Student" : student.FirstName.Trim();
                student.LastName = string.IsNullOrWhiteSpace(student.LastName) ? "" : student.LastName.Trim();
                student.FullName = $"{student.FirstName} {student.LastName}".Trim();
            }

            _context.Students.Add(student);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateStudent", $"Created student {student.Username}");
            TempData["Message"] = $"Student '{student.FullName}' created successfully!";
            return RedirectToAction("Students");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStudent(Student student, string? newPassword)
        {
            if (!CheckAccess()) return Denied();
            var existing = await _context.Students.FindAsync(student.Id);
            if (existing == null) return RedirectToAction("Students");

            var requestedStudentNumber = string.IsNullOrWhiteSpace(student.StudentNumber) ? existing.StudentNumber : student.StudentNumber.Trim();
            var requestedUsername = string.IsNullOrWhiteSpace(student.Username) ? existing.Username : student.Username.Trim();
            if (await _context.Students.AnyAsync(s =>
                    s.Id != existing.Id &&
                    (s.StudentNumber.ToLower() == requestedStudentNumber.ToLower() ||
                     s.Username.ToLower() == requestedUsername.ToLower())))
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

            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                existing.PasswordHash = _hasher.HashPassword(new object(), newPassword.Trim());
            }

            await _context.SaveChangesAsync();
            await AuditAsync("UpdateStudent", $"Updated student {existing.Username}");
            TempData["Message"] = $"Student '{existing.FullName}' updated successfully!";
            return RedirectToAction("Students");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteStudent(int id)
        {
            if (!CheckAccess()) return Denied();
            var student = await _context.Students.FindAsync(id);
            if (student != null)
            {
                var computer = await _context.Computers.FirstOrDefaultAsync(c => c.AssignedTo == id.ToString());
                if (computer != null)
                {
                    computer.AssignedTo = null;
                    if (computer.Status == "Assigned") computer.Status = "Available";
                }

                var joinRecords = await _context.ClassStudents.Where(cs => cs.StudentId == id).ToListAsync();
                _context.ClassStudents.RemoveRange(joinRecords);

                _context.Students.Remove(student);
                await _context.SaveChangesAsync();
                await AuditAsync("DeleteStudent", $"Deleted student {student.Username}");
                TempData["Message"] = $"Student '{student.Username}' deleted successfully!";
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
            if (string.IsNullOrWhiteSpace(role.Name))
            {
                TempData["ErrorMessage"] = "Role name is required.";
                return RedirectToAction("Roles");
            }
            _context.Roles.Add(role);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateRole", $"Created role {role.Name}");
            TempData["Message"] = $"Role '{role.Name}' created successfully!";
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
                TempData["Message"] = $"Role '{role.Name}' deleted successfully!";
            }
            return RedirectToAction("Roles");
        }

        // ---------- Restriction rules ----------
        public async Task<IActionResult> Restrictions()
        {
            if (!CheckAccess()) return Denied();
            return View(new PolicyManagementViewModel
            {
                Restrictions = await _context.RestrictionRules.OrderByDescending(r => r.CreatedAt).ToListAsync(),
                Blacklist = await _context.BlacklistItems.OrderByDescending(b => b.CreatedAt).ToListAsync(),
                ApplicationCategories = await _context.ApplicationCategories.OrderBy(c => c.Name).ToListAsync(),
                WebsiteCategories = await _context.WebsiteCategories.OrderBy(c => c.Name).ToListAsync()
            });
        }

        [HttpPost]
        public async Task<IActionResult> CreateRestriction(RestrictionRule rule)
        {
            if (!CheckAccess()) return Denied();
            if (!ValidRuleType(rule.RuleType) || string.IsNullOrWhiteSpace(rule.Target) || !ValidMode(rule.Mode))
            {
                TempData["ErrorMessage"] = "Choose a valid rule type and mode, and provide a target.";
                return RedirectToAction("Restrictions");
            }
            rule.RuleType = NormalizeRuleType(rule.RuleType);
            rule.Target = rule.Target.Trim();
            rule.Description = rule.Description?.Trim() ?? "";
            rule.CreatedAt = DateTime.Now;
            _context.RestrictionRules.Add(rule);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateRestriction", $"Added restriction on {rule.Target}");
            TempData["Message"] = $"Restriction rule on '{rule.Target}' saved!";
            return RedirectToAction("Restrictions");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateRestriction(RestrictionRule input)
        {
            if (!CheckAccess()) return Denied();
            var rule = await _context.RestrictionRules.FindAsync(input.RestrictionRuleId);
            if (rule == null || !ValidRuleType(input.RuleType) || string.IsNullOrWhiteSpace(input.Target) || !ValidMode(input.Mode))
                return RedirectToAction("Restrictions");

            rule.RuleType = NormalizeRuleType(input.RuleType);
            rule.Target = input.Target.Trim();
            rule.Description = input.Description?.Trim() ?? "";
            rule.Mode = input.Mode;
            rule.IsGlobal = input.IsGlobal;
            rule.IsActive = input.IsActive;
            await _context.SaveChangesAsync();
            await AuditAsync("UpdateRestriction", $"Updated restriction {rule.RestrictionRuleId}");
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
                TempData["Message"] = $"Restriction rule removed.";
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
            if (!ValidBlacklistType(item.TargetType) || string.IsNullOrWhiteSpace(item.Value))
            {
                TempData["ErrorMessage"] = "Choose a valid target type and provide a value.";
                return RedirectToAction(nameof(Blacklists));
            }
            item.TargetType = item.TargetType.Trim();
            item.Value = item.Value.Trim();
            item.Reason = item.Reason?.Trim() ?? "";
            item.CreatedAt = DateTime.Now;
            _context.BlacklistItems.Add(item);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateBlacklist", $"Blacklisted {item.TargetType}: {item.Value}");
            TempData["Message"] = $"Blacklist entry '{item.Value}' created!";
            return RedirectToAction(nameof(Blacklists));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateBlacklist(BlacklistItem input)
        {
            if (!CheckAccess()) return Denied();
            var item = await _context.BlacklistItems.FindAsync(input.BlacklistItemId);
            if (item == null || !ValidBlacklistType(input.TargetType) || string.IsNullOrWhiteSpace(input.Value))
                return RedirectToAction(nameof(Blacklists));

            item.TargetType = input.TargetType.Trim();
            item.Value = input.Value.Trim();
            item.Reason = input.Reason?.Trim() ?? "";
            item.IsActive = input.IsActive;
            await _context.SaveChangesAsync();
            await AuditAsync("UpdateBlacklist", $"Updated blacklist {item.BlacklistItemId}");
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
                TempData["Message"] = "Blacklist entry deleted.";
            }
            return RedirectToAction(nameof(Blacklists));
        }

        [HttpPost]
        public async Task<IActionResult> CreateApplicationCategory(ApplicationCategory category)
            => await SaveApplicationCategory(category, null);

        [HttpPost]
        public async Task<IActionResult> UpdateApplicationCategory(ApplicationCategory input)
            => await SaveApplicationCategory(input, input.ApplicationCategoryId);

        private async Task<IActionResult> SaveApplicationCategory(ApplicationCategory input, int? id)
        {
            if (!CheckAccess()) return Denied();
            var category = id.HasValue ? await _context.ApplicationCategories.FindAsync(id.Value) : new ApplicationCategory();
            if (category == null || string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Pattern) || !ValidMode(input.Mode))
                return RedirectToAction(nameof(Restrictions));
            category.Name = input.Name.Trim();
            category.Pattern = input.Pattern.Trim();
            category.Description = input.Description?.Trim() ?? "";
            category.Mode = input.Mode;
            category.IsActive = input.IsActive;
            if (!id.HasValue) _context.ApplicationCategories.Add(category);
            await _context.SaveChangesAsync();
            await AuditAsync(id.HasValue ? "UpdateApplicationCategory" : "CreateApplicationCategory", $"Policy category {category.Name}");
            return RedirectToAction(nameof(Restrictions));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteApplicationCategory(int id)
        {
            if (!CheckAccess()) return Denied();
            var category = await _context.ApplicationCategories.FindAsync(id);
            if (category != null)
            {
                _context.ApplicationCategories.Remove(category);
                await _context.SaveChangesAsync();
                await AuditAsync("DeleteApplicationCategory", $"Removed category {id}");
            }
            return RedirectToAction(nameof(Restrictions));
        }

        [HttpPost]
        public async Task<IActionResult> CreateWebsiteCategory(WebsiteCategory category)
            => await SaveWebsiteCategory(category, null);

        [HttpPost]
        public async Task<IActionResult> UpdateWebsiteCategory(WebsiteCategory input)
            => await SaveWebsiteCategory(input, input.WebsiteCategoryId);

        private async Task<IActionResult> SaveWebsiteCategory(WebsiteCategory input, int? id)
        {
            if (!CheckAccess()) return Denied();
            var category = id.HasValue ? await _context.WebsiteCategories.FindAsync(id.Value) : new WebsiteCategory();
            if (category == null || string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.DomainPattern) || !ValidMode(input.Mode))
                return RedirectToAction(nameof(Restrictions));
            category.Name = input.Name.Trim();
            category.DomainPattern = input.DomainPattern.Trim();
            category.Description = input.Description?.Trim() ?? "";
            category.Mode = input.Mode;
            category.IsActive = input.IsActive;
            if (!id.HasValue) _context.WebsiteCategories.Add(category);
            await _context.SaveChangesAsync();
            await AuditAsync(id.HasValue ? "UpdateWebsiteCategory" : "CreateWebsiteCategory", $"Policy category {category.Name}");
            return RedirectToAction(nameof(Restrictions));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteWebsiteCategory(int id)
        {
            if (!CheckAccess()) return Denied();
            var category = await _context.WebsiteCategories.FindAsync(id);
            if (category != null)
            {
                _context.WebsiteCategories.Remove(category);
                await _context.SaveChangesAsync();
                await AuditAsync("DeleteWebsiteCategory", $"Removed category {id}");
            }
            return RedirectToAction(nameof(Restrictions));
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
            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                TempData["ErrorMessage"] = "Session rule name is required.";
                return RedirectToAction("SessionRules");
            }
            if (rule.IsDefault)
            {
                var defaults = _context.SessionRules.Where(s => s.IsDefault);
                foreach (var d in defaults) d.IsDefault = false;
            }
            rule.CreatedAt = DateTime.Now;
            _context.SessionRules.Add(rule);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateSessionRule", $"Created session rule {rule.Name}");
            TempData["Message"] = $"Session rule '{rule.Name}' created!";
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
                TempData["Message"] = "Session rule deleted.";
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
            TempData["Message"] = "LAN Configuration saved successfully!";
            return RedirectToAction("LanConfig");
        }

        // ---------- Computers ----------
        public async Task<IActionResult> Computers()
        {
            if (!CheckAccess()) return Denied();
            return View(await _context.Computers.OrderBy(c => c.LaboratoryStation).ToListAsync());
        }

        public async Task<IActionResult> ComputerHistory(int id)
        {
            if (!CheckAccess()) return Denied();
            var computer = await _context.Computers.AsNoTracking().FirstOrDefaultAsync(c => c.ComputerId == id);
            if (computer == null) return NotFound();
            ViewBag.Computer = computer;
            return View(await _context.ComputerStatusHistories.AsNoTracking().Where(h => h.ComputerId == id).OrderByDescending(h => h.ChangedAt).ToListAsync());
        }

        [HttpPost]
        public async Task<IActionResult> CreateComputer(Computer computer)
        {
            if (!CheckAccess()) return Denied();
            if (string.IsNullOrWhiteSpace(computer.LaboratoryStation))
            {
                TempData["ErrorMessage"] = "Laboratory station name is required.";
                return RedirectToAction("Computers");
            }
            computer.Status = string.IsNullOrWhiteSpace(computer.Status) ? "Available" : computer.Status;
            _context.Computers.Add(computer);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateComputer", $"Added {computer.LaboratoryStation}");
            TempData["Message"] = $"Workstation '{computer.LaboratoryStation}' added!";
            return RedirectToAction("Computers");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateComputer(Computer computer)
        {
            if (!CheckAccess()) return Denied();
            var existing = await _context.Computers.FindAsync(computer.ComputerId);
            if (existing != null)
            {
                var previousStatus = existing.Status;
                existing.LaboratoryStation = string.IsNullOrWhiteSpace(computer.LaboratoryStation) ? existing.LaboratoryStation : computer.LaboratoryStation.Trim();
                existing.Status = string.IsNullOrWhiteSpace(computer.Status) ? existing.Status : computer.Status.Trim();
                existing.AssignedTo = computer.AssignedTo;
                if (!string.Equals(previousStatus, existing.Status, StringComparison.OrdinalIgnoreCase))
                    _context.ComputerStatusHistories.Add(new ComputerStatusHistory { ComputerId = existing.ComputerId, Status = existing.Status, ChangedByType = "Admin", ChangedById = HttpContext.Session.GetInt32("AdminId") });
                await _context.SaveChangesAsync();
                await AuditAsync("UpdateComputer", $"Updated computer {existing.LaboratoryStation}");
                TempData["Message"] = $"Workstation '{existing.LaboratoryStation}' updated successfully!";
            }
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
                TempData["Message"] = $"Workstation '{computer.LaboratoryStation}' deleted.";
            }
            return RedirectToAction(nameof(Computers));
        }

        // ---------- Workstation-to-Student mapping ----------
        [HttpPost]
        public async Task<IActionResult> AssignComputer(int studentId, int? computerId)
        {
            if (!CheckAccess()) return Denied();

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
            TempData["Message"] = "Workstation assignment updated successfully!";
            return RedirectToAction("Students");
        }

        [HttpPost]
        public async Task<IActionResult> AssignStudentToClass(int studentId, int? classId, bool moveStudent = false)
        {
            if (!CheckAccess()) return Denied();

            var student = await _context.Students.FindAsync(studentId);
            if (student == null)
            {
                TempData["ErrorMessage"] = "The student was not found.";
                return RedirectToAction("Students");
            }

            ClassOperationResult result;
            if (classId.HasValue)
            {
                result = await _classManagement.EnrollExistingStudentAsync(studentId: studentId, classId: classId.Value, moveStudent: moveStudent);
            }
            else if (student.ClassId.HasValue)
            {
                result = await _classManagement.RemoveStudentAsync(student.ClassId.Value, studentId);
            }
            else
            {
                TempData["Message"] = "Student is already unassigned.";
                return RedirectToAction("Students");
            }

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
            }
            else
            {
                await AuditAsync("AssignStudentToClass", classId.HasValue
                    ? $"Assigned student {studentId} to class {classId}"
                    : $"Unassigned student {studentId} from class");
                TempData["Message"] = classId.HasValue
                    ? $"Student '{result.Name}' assigned successfully."
                    : $"Student '{result.Name}' unassigned. The account was preserved.";
            }

            return RedirectToAction("Students");
        }

        // ---------- Class Management ----------
        public async Task<IActionResult> Classes()
        {
            if (!CheckAccess()) return Denied();
            var classes = await _classManagement.GetClassesAsync();

            ViewBag.TeacherList = await _context.Teachers
                .Where(t => t.Status == "Active" || string.IsNullOrEmpty(t.Status))
                .OrderBy(t => t.FirstName)
                .ToListAsync();

            return View(classes);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateClass(Class cls)
        {
            if (!CheckAccess()) return Denied();
            var result = await _classManagement.CreateClassAsync(
                new ClassInput(cls.ClassName, cls.Section, cls.Subject, cls.GradeLevel, cls.Schedule, cls.AcademicYear, cls.TeacherId),
                actorTeacherId: null,
                isAdmin: true);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
                return RedirectToAction("Classes");
            }

            await AuditAsync("CreateClass", $"Created class '{result.Name}'");
            TempData["Message"] = $"Class '{result.Name}' created successfully!";
            return RedirectToAction("Classes");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateClass(Class cls)
        {
            if (!CheckAccess()) return Denied();
            var result = await _classManagement.UpdateClassAsync(
                cls.ClassId,
                new ClassInput(cls.ClassName, cls.Section, cls.Subject, cls.GradeLevel, cls.Schedule, cls.AcademicYear, cls.TeacherId),
                actorTeacherId: null,
                isAdmin: true);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
                return RedirectToAction("Classes");
            }

            await AuditAsync("UpdateClass", $"Updated class '{result.Name}'");
            TempData["Message"] = $"Class '{result.Name}' updated successfully!";
            return RedirectToAction("Classes");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignTeacher(int classId, int? teacherId)
        {
            if (!CheckAccess()) return Denied();
            var result = teacherId.HasValue
                ? await _classManagement.AssignTeacherAsync(classId, teacherId.Value)
                : ClassOperationResult.Fail("Select an active teacher before assigning the class.");

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
            }
            else
            {
                await AuditAsync("AssignTeacher", $"Assigned teacher {teacherId} to class '{result.Name}'");
                TempData["Message"] = $"Teacher assigned successfully to '{result.Name}'.";
            }
            return RedirectToAction("ClassDetails", new { id = classId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ArchiveClass(int id)
        {
            if (!CheckAccess()) return Denied();
            var existing = await _classManagement.GetClassAsync(id);
            if (existing == null)
            {
                TempData["ErrorMessage"] = "The class was not found.";
                return RedirectToAction("Classes");
            }

            var result = await _classManagement.SetArchiveStateAsync(id, !existing.IsArchived);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
            }
            else
            {
                var state = existing.IsArchived ? "restored" : "archived";
                await AuditAsync("ArchiveClass", $"{state} class '{result.Name}'");
                TempData["Message"] = $"Class '{result.Name}' {state} successfully.";
            }
            return RedirectToAction("Classes");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteClass(int classId)
        {
            if (!CheckAccess()) return Denied();
            var result = await _classManagement.DeleteClassAsync(classId);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
            }
            else
            {
                await AuditAsync("DeleteClass", $"Deleted class '{result.Name}'");
                TempData["Message"] = $"Class '{result.Name}' deleted successfully.";
            }
            return RedirectToAction("Classes");
        }

        public async Task<IActionResult> ClassDetails(int id)
        {
            if (!CheckAccess()) return Denied();
            await _classManagement.EnsureMembershipLinksAsync(id);
            var cls = await _classManagement.GetClassAsync(id);
            if (cls == null) return RedirectToAction("Classes");

            var roster = await _classManagement.GetRosterAsync(id);
            cls.ClassStudents = roster.ToList();
            ViewBag.EnrolledStudents = roster;
            ViewBag.AvailableTeachers = await _context.Teachers
                .Where(t => t.Status == "Active" || string.IsNullOrEmpty(t.Status))
                .OrderBy(t => t.LastName)
                .ThenBy(t => t.FirstName)
                .ToListAsync();
            ViewBag.AllStudents = await _context.Students
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
            var result = await _classManagement.CreateStudentInClassAsync(
                classId,
                new NewStudentInput(null, firstName, lastName, null, username, password));
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

            var result = await _classManagement.BulkCreateStudentsInClassAsync(classId, rows);
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
            if (file == null || file.Length == 0) return RedirectToAction("ClassDetails", new { id = classId });
            using var reader = new StreamReader(file.OpenReadStream());
            var parsed = _classManagement.ParseBulkStudentsCsv(await reader.ReadToEndAsync());
            var import = await _classManagement.ValidateBulkStudentsAsync(classId, parsed.Rows);
            if (import.Errors.Count > 0) return File(BulkErrorCsv(import.Errors), "text/csv; charset=utf-8", $"CAMS-Student-Import-Errors-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
            var result = await _classManagement.BulkCreateStudentsInClassAsync(classId, import.Rows);
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
            var result = await _classManagement.EnrollExistingStudentAsync(classId, studentId, moveStudent);
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
        public async Task<IActionResult> RemoveStudent(int classId, int studentId)
        {
            if (!CheckAccess()) return Denied();
            var result = await _classManagement.RemoveStudentAsync(classId, studentId);
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

        // ---------- Reports ----------
        public async Task<IActionResult> Reports(DateTime? from, DateTime? to, int? classId = null, string? station = null, int page = 1, int pageSize = 50)
        {
            if (!CheckAccess()) return Denied();

            var fromDate = (from ?? DateTime.Today.AddDays(-30)).Date;
            var toDate = (to ?? DateTime.Today).Date.AddDays(1);
            if (toDate <= fromDate) toDate = fromDate.AddDays(1);
            page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);

            var sessionQuery = _context.LabSessions
                .Include(s => s.Student).ThenInclude(s => s!.Class)
                .Include(s => s.Teacher)
                .Include(s => s.Computer)
                .Where(s => s.StartTime < toDate && (s.EndTime ?? DateTime.UtcNow) > fromDate);
            if (classId.HasValue) sessionQuery = sessionQuery.Where(s => s.Student != null && s.Student.ClassId == classId.Value);
            if (!string.IsNullOrWhiteSpace(station)) sessionQuery = sessionQuery.Where(s => (s.Computer != null && s.Computer.LaboratoryStation == station) || s.PCName == station);
            var totalSessions = await sessionQuery.CountAsync();
            var sessions = await sessionQuery.OrderByDescending(s => s.StartTime).ThenByDescending(s => s.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            var summarySessions = await sessionQuery.AsNoTracking().ToListAsync();

            var usage = await _context.UsageLogs
                .Where(u => u.Timestamp >= fromDate && u.Timestamp < toDate)
                .OrderByDescending(u => u.Timestamp)
                .Take(500)
                .ToListAsync();

            ViewBag.FromDate = fromDate.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate.ToString("yyyy-MM-dd");
            var now = DateTime.UtcNow;
            ViewBag.TotalSessions = totalSessions;
            ViewBag.TotalMinutes = summarySessions.Sum(s => SessionDuration(s, fromDate, toDate, now).TotalMinutes);
            ViewBag.Paging = new PagedResult<LabSession>(sessions, page, pageSize, totalSessions);
            ViewBag.ReportSummary = new ReportSummary(totalSessions,
                TimeSpan.FromMinutes(summarySessions.Sum(s => SessionDuration(s, fromDate, toDate, now).TotalMinutes)),
                summarySessions.GroupBy(s => s.Student?.Class?.ClassName ?? "Unassigned").ToDictionary(g => g.Key, g => g.Count()),
                summarySessions.GroupBy(s => s.Teacher?.Username ?? "Unknown").ToDictionary(g => g.Key, g => g.Count()),
                summarySessions.GroupBy(s => s.Computer?.LaboratoryStation ?? s.PCName ?? "Unknown").ToDictionary(g => g.Key, g => g.Count()));
            ViewBag.Classes = await _context.Classes.Where(c => !c.IsArchived).OrderBy(c => c.ClassName).ToListAsync();
            ViewBag.Stations = await _context.Computers.AsNoTracking().OrderBy(c => c.LaboratoryStation).ToListAsync();
            ViewBag.SelectedStation = station;
            ViewBag.TopApps = usage
                .GroupBy(u => u.AppName)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => new { App = g.Key, Count = g.Count() })
                .ToList();
            ViewBag.UsageLogs = usage;
            ViewBag.SessionsByTeacher = summarySessions
                .GroupBy(s => s.Teacher?.Username ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count());
            ViewBag.SessionsByStation = summarySessions
                .GroupBy(s => s.Computer?.LaboratoryStation ?? s.PCName ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count());

            return View(sessions);
        }

        private static TimeSpan SessionDuration(LabSession session, DateTime from, DateTime to, DateTime now)
        {
            var end = session.EndTime ?? (session.Status == "Paused" && session.PauseTime.HasValue ? session.PauseTime.Value : now);
            var start = session.StartTime > from ? session.StartTime : from;
            var clippedEnd = end < to ? end : to;
            return clippedEnd > start ? clippedEnd - start : TimeSpan.Zero;
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
        public async Task<IActionResult> ExportReportsCsv(DateTime? from = null, DateTime? to = null, int? classId = null, string? station = null)
        {
            if (!CheckAccess()) return Denied();
            var fromDate = (from ?? DateTime.Today.AddDays(-30)).Date;
            var toDate = (to ?? DateTime.Today).Date.AddDays(1);
            var query = _context.LabSessions
                .Include(s => s.Student)
                .ThenInclude(s => s!.Class)
                .Include(s => s.Teacher)
                .Include(s => s.Computer)
                .Where(s => s.StartTime < toDate && (s.EndTime ?? DateTime.UtcNow) > fromDate)
                .AsQueryable();
            if (classId.HasValue) query = query.Where(s => s.Student != null && s.Student.ClassId == classId.Value);
            if (!string.IsNullOrWhiteSpace(station)) query = query.Where(s => (s.Computer != null && s.Computer.LaboratoryStation == station) || s.PCName == station);
            var sessions = await query.OrderByDescending(s => s.StartTime).Take(2000).ToListAsync();

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Student,Class,Teacher,Station,Start Time,End Time,Duration (min),Attendance,Status");
            foreach (var s in sessions)
            {
                var duration = Math.Round(SessionDuration(s, fromDate, toDate, DateTime.UtcNow).TotalMinutes, 1);
                csv.AppendLine($"{Csv(s.Student?.FullName)},{Csv(s.Student?.Class?.ClassName ?? "Unassigned")},{Csv(s.Teacher?.Username ?? "System")},{Csv(s.Computer?.LaboratoryStation ?? s.PCName)},{s.StartTime:yyyy-MM-dd HH:mm},{s.EndTime?.ToString("yyyy-MM-dd HH:mm")},{duration},{(s.StartTime < toDate && (s.EndTime ?? DateTime.UtcNow) > fromDate ? "Present" : "Absent")},{s.Status}");
            }

            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()),
                "text/csv; charset=utf-8",
                $"CAMS-UsageReport-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> ExportAttendanceCsv(DateTime? from = null, DateTime? to = null, int? classId = null)
        {
            if (!CheckAccess()) return Denied();
            var fromDate = (from ?? DateTime.Today.AddDays(-30)).Date;
            var toDate = (to ?? DateTime.Today).Date.AddDays(1);
            var query = _context.LabSessions.AsNoTracking().Include(s => s.Student).ThenInclude(s => s!.Class)
                .Where(s => s.StartTime < toDate && (s.EndTime ?? DateTime.UtcNow) > fromDate);
            if (classId.HasValue) query = query.Where(s => s.Student != null && s.Student.ClassId == classId.Value);
            var rows = await query.OrderByDescending(s => s.StartTime).Take(5000).ToListAsync();
            var csv = new System.Text.StringBuilder("Student Number,Student Name,Class,Station,Date,Attendance\n");
            foreach (var s in rows) csv.AppendLine($"{Csv(s.Student?.StudentNumber)},{Csv(s.Student?.FullName)},{Csv(s.Student?.Class?.ClassName ?? "Unassigned")},{Csv(s.Computer?.LaboratoryStation ?? s.PCName)},{s.StartTime:yyyy-MM-dd},Present");
            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"CAMS-Attendance-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> ExportRemoteCommandsCsv(DateTime? from = null, DateTime? to = null, int? teacherId = null)
        {
            if (!CheckAccess()) return Denied();
            var fromDate = (from ?? DateTime.Today.AddDays(-30)).Date;
            var toDate = (to ?? DateTime.Today).Date.AddDays(1);
            var query = _context.RemoteCommandLogs.AsNoTracking().Where(l => l.Timestamp >= fromDate && l.Timestamp < toDate);
            if (teacherId.HasValue) query = query.Where(l => l.TeacherId == teacherId.Value);
            var rows = await query.OrderByDescending(l => l.Timestamp).Take(5000).ToListAsync();
            var csv = new System.Text.StringBuilder("Timestamp,Teacher ID,Command,Details,Session ID\n");
            foreach (var l in rows) csv.AppendLine($"{l.Timestamp:O},{l.TeacherId},{Csv(l.Command)},{Csv(l.Details)},{l.RemoteControlSessionId}");
            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"CAMS-RemoteCommands-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
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
    }
}
