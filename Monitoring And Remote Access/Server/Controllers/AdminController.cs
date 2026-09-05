using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Server.Authorization;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Controllers
{
    [Authorize(Roles = "Admin,Teacher")]
    [ServiceFilter(typeof(AdminControllerAuthorizationFilter))]
    [AutoValidateAntiforgeryToken]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PasswordHasher<object> _hasher = new();
        private readonly IClassManagementService _classManagement;
        private readonly IAuthenticationService _authentication;
        private readonly LabSessionLifecycleService _sessionLifecycle;

        public AdminController(
            ApplicationDbContext context,
            LabSessionLifecycleService sessionLifecycle,
            IClassManagementService? classManagement = null,
            IAuthenticationService? authentication = null)
        {
            _context = context;
            _classManagement = classManagement ?? new ClassManagementService(context);
            _authentication = authentication ?? new AuthenticationService(context);
            _sessionLifecycle = sessionLifecycle;
        }

        private bool CheckAccess() => HttpContext.IsAdmin() || HttpContext.IsTeacher();

        private bool IsTeacherActor => User.IsInRole("Teacher") || HttpContext.IsTeacher();

        private int? ActorTeacherId
        {
            get
            {
                if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var teacherId)) return teacherId;
                return HttpContext.Session.GetInt32("TeacherId");
            }
        }

        private bool IsTeacherSelf(int teacherId) => IsTeacherActor && ActorTeacherId == teacherId;

        private Task<int> ActiveTeacherCountAsync() => _context.Teachers.CountAsync(teacher =>
            teacher.Status == "Active" || teacher.Status == null || teacher.Status == string.Empty);

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

        private async Task<bool> LoginIdentifierInUseAsync(string value, AccountRole? excludedRole = null, int? excludedId = null)
        {
            value = value.Trim().ToLower();
            return (excludedRole != AccountRole.Admin && await _context.Admins.AnyAsync(a => a.Username.ToLower() == value)) ||
                   (excludedRole == AccountRole.Admin && await _context.Admins.AnyAsync(a => a.Id != excludedId && a.Username.ToLower() == value)) ||
                   (excludedRole != AccountRole.Teacher && await _context.Teachers.AnyAsync(t => t.Username.ToLower() == value)) ||
                   (excludedRole == AccountRole.Teacher && await _context.Teachers.AnyAsync(t => t.TeacherId != excludedId && t.Username.ToLower() == value)) ||
                   (excludedRole != AccountRole.Student && await _context.Students.AnyAsync(s => s.Username.ToLower() == value || s.StudentNumber.ToLower() == value)) ||
                   (excludedRole == AccountRole.Student && await _context.Students.AnyAsync(s => s.Id != excludedId && (s.Username.ToLower() == value || s.StudentNumber.ToLower() == value)));
        }

        private IActionResult AccountManagementRedirect(AccountRole accountRole) => accountRole switch
        {
            AccountRole.Admin => RedirectToAction(nameof(Settings)),
            AccountRole.Teacher => RedirectToAction(nameof(Teachers)),
            AccountRole.Student => RedirectToAction(nameof(Students)),
            _ => BadRequest()
        };

        private async Task LoadAdminAccountsAsync()
        {
            ViewBag.Admins = await _context.Admins
                .AsNoTracking()
                .OrderBy(admin => admin.Username)
                .ToListAsync();
        }

        private async Task AuditAsync(string action, string details)
        {
            var teacherActor = IsTeacherActor;
            _context.AuditLogs.Add(new AuditLog
            {
                UserType = teacherActor ? "Teacher" : "Admin",
                UserId = teacherActor ? ActorTeacherId : HttpContext.Session.GetInt32("AdminId"),
                Action = action,
                Details = details,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                 Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        // ---------- Account settings and lockout management ----------
        public async Task<IActionResult> Settings()
        {
            if (!CheckAccess()) return Denied();
            await LoadAdminAccountsAsync();
            return View(new PasswordChangeInput());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword([Bind("CurrentPassword,NewPassword,ConfirmPassword")] PasswordChangeInput input)
        {
            if (!CheckAccess()) return Denied();
            var adminId = HttpContext.Session.GetInt32("AdminId");
            if (!adminId.HasValue) return Denied();

            if (!ModelState.IsValid)
            {
                await LoadAdminAccountsAsync();
                return View("Settings", input);
            }

            var changed = await _authentication.ChangeAdminPasswordAsync(
                adminId.Value,
                input.CurrentPassword,
                input.NewPassword,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
            if (!changed)
            {
                ModelState.AddModelError(nameof(input.CurrentPassword), "The current password is incorrect.");
                await LoadAdminAccountsAsync();
                return View("Settings", input);
            }

            TempData["Message"] = "Your password was changed successfully.";
            return RedirectToAction(nameof(Settings));
        }

        [HttpPost, ValidateAntiForgeryToken]
        [TeacherSharedAction]
        public async Task<IActionResult> UnlockAccount(AccountRole accountRole, int id)
        {
            if (!CheckAccess()) return Denied();
            if (IsTeacherActor && accountRole == AccountRole.Admin) return Forbid();
            if (accountRole == AccountRole.Teacher && IsTeacherSelf(id))
            {
                TempData["ErrorMessage"] = "You cannot unlock your own Teacher account from global Teacher management.";
                return RedirectToAction(nameof(Teachers));
            }

            string accountName;
            switch (accountRole)
            {
                case AccountRole.Admin:
                {
                    var account = await _context.Admins.FindAsync(id);
                    if (account == null) return NotFound();
                    account.FailedLoginAttempts = 0;
                    account.LockoutEndUtc = null;
                    accountName = account.Username;
                    break;
                }
                case AccountRole.Teacher:
                {
                    var account = await _context.Teachers.FindAsync(id);
                    if (account == null) return NotFound();
                    account.FailedLoginAttempts = 0;
                    account.LockoutEndUtc = null;
                    accountName = account.Username;
                    break;
                }
                case AccountRole.Student:
                {
                    var account = await _context.Students.FindAsync(id);
                    if (account == null) return NotFound();
                    account.FailedLoginAttempts = 0;
                    account.LockoutEndUtc = null;
                    accountName = account.Username;
                    break;
                }
                default:
                    return BadRequest();
            }

            await _context.SaveChangesAsync();
            await AuditAsync("UnlockAccount", $"Unlocked {accountRole} account {id} ({accountName})");
            TempData["Message"] = $"The {accountRole.ToString().ToLowerInvariant()} account was unlocked.";
            return AccountManagementRedirect(accountRole);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [TeacherSharedAction]
        public async Task<IActionResult> SetAccountActive(AccountRole accountRole, int id, bool isActive)
        {
            if (!CheckAccess()) return Denied();
            if (IsTeacherActor && accountRole == AccountRole.Admin) return Forbid();
            if (!isActive && accountRole == AccountRole.Teacher && IsTeacherSelf(id))
            {
                TempData["ErrorMessage"] = "You cannot deactivate your own Teacher account from global Teacher management.";
                return RedirectToAction(nameof(Teachers));
            }

            string accountName;
            switch (accountRole)
            {
                case AccountRole.Admin:
                {
                    var account = await _context.Admins.FindAsync(id);
                    if (account == null) return NotFound();
                    if (!isActive && account.IsActive && await _context.Admins.CountAsync(admin => admin.IsActive) <= 1)
                    {
                        TempData["ErrorMessage"] = "The last active administrator cannot be deactivated.";
                        return RedirectToAction(nameof(Settings));
                    }

                    account.IsActive = isActive;
                    accountName = account.Username;
                    break;
                }
                case AccountRole.Teacher:
                {
                    var account = await _context.Teachers.FindAsync(id);
                    if (account == null) return NotFound();
                    if (!isActive && await _context.Classes.AnyAsync(cls => cls.TeacherId == id && !cls.IsArchived))
                    {
                        TempData["ErrorMessage"] = "Reassign or archive this teacher's active classes before deactivating the account.";
                        return RedirectToAction(nameof(Teachers));
                    }

                    var accountIsActive = account.Status == "Active" || string.IsNullOrEmpty(account.Status);
                    if (IsTeacherActor && !isActive && accountIsActive && await ActiveTeacherCountAsync() <= 1)
                    {
                        TempData["ErrorMessage"] = "The last active Teacher cannot be deactivated.";
                        return RedirectToAction(nameof(Teachers));
                    }

                    account.Status = isActive ? "Active" : "Inactive";
                    accountName = account.Username;
                    break;
                }
                case AccountRole.Student:
                {
                    var account = await _context.Students.FindAsync(id);
                    if (account == null) return NotFound();
                    account.Status = isActive ? "Active" : "Inactive";
                    accountName = account.Username;
                    break;
                }
                default:
                    return BadRequest();
            }

            await _context.SaveChangesAsync();
            await AuditAsync(
                isActive ? "ActivateAccount" : "DeactivateAccount",
                $"Set {accountRole} account {id} ({accountName}) to {(isActive ? "active" : "inactive")}");
            TempData["Message"] = $"The {accountRole.ToString().ToLowerInvariant()} account is now {(isActive ? "active" : "inactive")}.";
            return AccountManagementRedirect(accountRole);
        }

        // ---------- Administrator account management ----------
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAdmin([Bind("Username,PasswordHash,FullName")] Admin admin)
        {
            if (!CheckAccess()) return Denied();

            if (string.IsNullOrWhiteSpace(admin.Username))
            {
                TempData["ErrorMessage"] = "Username is required.";
                return RedirectToAction(nameof(Settings));
            }
            if (string.IsNullOrWhiteSpace(admin.PasswordHash))
            {
                TempData["ErrorMessage"] = "A password is required for a new administrator.";
                return RedirectToAction(nameof(Settings));
            }

            admin.Username = admin.Username.Trim();
            if (await LoginIdentifierInUseAsync(admin.Username))
            {
                TempData["ErrorMessage"] = $"The username '{admin.Username}' is already in use.";
                return RedirectToAction(nameof(Settings));
            }

            admin.FullName = string.IsNullOrWhiteSpace(admin.FullName) ? "System Administrator" : admin.FullName.Trim();
            admin.PasswordHash = _hasher.HashPassword(new object(), admin.PasswordHash.Trim());
            admin.IsActive = true;
            admin.FailedLoginAttempts = 0;
            admin.LockoutEndUtc = null;

            _context.Admins.Add(admin);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateAdmin", $"Created administrator {admin.Username}");
            TempData["Message"] = $"Administrator '{admin.Username}' created successfully.";
            return RedirectToAction(nameof(Settings));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateAdmin([Bind("Id,Username,FullName")] Admin admin, string? newPassword)
        {
            if (!CheckAccess()) return Denied();
            var existing = await _context.Admins.FindAsync(admin.Id);
            if (existing == null) return RedirectToAction(nameof(Settings));

            var requestedUsername = string.IsNullOrWhiteSpace(admin.Username) ? existing.Username : admin.Username.Trim();
            if (await LoginIdentifierInUseAsync(requestedUsername, AccountRole.Admin, existing.Id))
            {
                TempData["ErrorMessage"] = $"The username '{requestedUsername}' is already in use.";
                return RedirectToAction(nameof(Settings));
            }

            existing.Username = requestedUsername;
            if (!string.IsNullOrWhiteSpace(admin.FullName))
            {
                existing.FullName = admin.FullName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                existing.PasswordHash = _hasher.HashPassword(new object(), newPassword.Trim());
            }

            await _context.SaveChangesAsync();
            await AuditAsync("UpdateAdmin", $"Updated administrator {existing.Username}");
            TempData["Message"] = $"Administrator '{existing.Username}' updated successfully.";
            return RedirectToAction(nameof(Settings));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAdmin(int id)
        {
            if (!CheckAccess()) return Denied();

            var currentAdminId = HttpContext.Session.GetInt32("AdminId");
            if (currentAdminId == id)
            {
                TempData["ErrorMessage"] = "You cannot delete the administrator account you are signed in as.";
                return RedirectToAction(nameof(Settings));
            }

            var admin = await _context.Admins.FindAsync(id);
            if (admin == null)
            {
                return RedirectToAction(nameof(Settings));
            }

            if (admin.IsActive && await _context.Admins.CountAsync(a => a.IsActive) <= 1)
            {
                TempData["ErrorMessage"] = "The last active administrator cannot be deleted.";
                return RedirectToAction(nameof(Settings));
            }

            _context.Admins.Remove(admin);
            await _context.SaveChangesAsync();
            await AuditAsync("DeleteAdmin", $"Deleted administrator {admin.Username}");
            TempData["Message"] = $"Administrator '{admin.Username}' deleted successfully.";
            return RedirectToAction(nameof(Settings));
        }

        // ---------- Dashboard ----------
        [TeacherSharedAction]
        public async Task<IActionResult> Index()
        {
            if (!CheckAccess()) return Denied();

            ViewBag.StudentCount = await _context.Students.CountAsync();
            ViewBag.TeacherCount = await _context.Teachers.CountAsync();
            ViewBag.ComputerCount = await _context.Computers.CountAsync();
            ViewBag.ActiveSessions = await _context.LabSessions.CountAsync(s => s.IsActive);
            ViewBag.RunningSessions = await _context.LabSessions.CountAsync(s => s.IsActive && s.Status == "Running");
            ViewBag.PausedSessions = await _context.LabSessions.CountAsync(s => s.IsActive && s.Status == "Paused");
            return View();
        }

        [HttpPost, ValidateAntiForgeryToken]
        [TeacherSharedAction]
        public async Task<IActionResult> PauseAllSessions()
        {
            if (!CheckAccess()) return Denied();
            var changed = await _sessionLifecycle.PauseAllSessionsAsync();
            await AuditAsync("GlobalPauseSession", $"Paused {changed} active lab session(s)");
            TempData["Message"] = $"Paused {changed} lab session(s).";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        [TeacherSharedAction]
        public async Task<IActionResult> ResumeAllSessions()
        {
            if (!CheckAccess()) return Denied();
            var changed = await _sessionLifecycle.ResumeAllSessionsAsync();
            await AuditAsync("GlobalResumeSession", $"Resumed {changed} paused lab session(s)");
            TempData["Message"] = $"Resumed {changed} lab session(s).";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        [TeacherSharedAction]
        public async Task<IActionResult> EndAllSessions()
        {
            if (!CheckAccess()) return Denied();
            var changed = await _sessionLifecycle.EndAllSessionsAsync();
            await AuditAsync("GlobalEndSession", $"Ended {changed} active lab session(s)");
            TempData["Message"] = $"Ended {changed} lab session(s).";
            return RedirectToAction(nameof(Index));
        }

        // ---------- Teacher accounts ----------
        [TeacherSharedAction]
        public async Task<IActionResult> Teachers()
        {
            if (!CheckAccess()) return Denied();
            return View(await _context.Teachers.OrderBy(t => t.LastName).ThenBy(t => t.FirstName).ToListAsync());
        }

        [HttpPost]
        [TeacherSharedAction]
        public async Task<IActionResult> CreateTeacher([Bind("FirstName,LastName,Email,Username,PasswordHash,ContactNumber,Status")] Teacher teacher)
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
            if (await LoginIdentifierInUseAsync(teacher.Username))
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
        [TeacherSharedAction]
        public async Task<IActionResult> UpdateTeacher([Bind("TeacherId,FirstName,LastName,Email,Username,ContactNumber,Status")] Teacher teacher, string? newPassword)
        {
            if (!CheckAccess()) return Denied();
            if (IsTeacherSelf(teacher.TeacherId))
            {
                TempData["ErrorMessage"] = "You cannot edit your own Teacher account from global Teacher management.";
                return RedirectToAction(nameof(Teachers));
            }
            var existing = await _context.Teachers.FindAsync(teacher.TeacherId);
            if (existing == null) return RedirectToAction("Teachers");

            var requestedUsername = string.IsNullOrWhiteSpace(teacher.Username) ? existing.Username : teacher.Username.Trim();
            if (await LoginIdentifierInUseAsync(requestedUsername, AccountRole.Teacher, existing.TeacherId))
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
            var existingIsActive = existing.Status == "Active" || string.IsNullOrEmpty(existing.Status);
            if (IsTeacherActor && string.Equals(requestedStatus, "Inactive", StringComparison.OrdinalIgnoreCase) &&
                existingIsActive && await ActiveTeacherCountAsync() <= 1)
            {
                TempData["ErrorMessage"] = "The last active Teacher cannot be deactivated.";
                return RedirectToAction(nameof(Teachers));
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
        [TeacherSharedAction]
        public async Task<IActionResult> DeleteTeacher(int id)
        {
            if (!CheckAccess()) return Denied();
            if (IsTeacherSelf(id))
            {
                TempData["ErrorMessage"] = "You cannot delete your own Teacher account from global Teacher management.";
                return RedirectToAction(nameof(Teachers));
            }
            var teacher = await _context.Teachers.FindAsync(id);
            if (teacher != null)
            {
                var classes = await _context.Classes.Where(c => c.TeacherId == id).ToListAsync();
                if (classes.Any(c => !c.IsArchived))
                {
                    TempData["ErrorMessage"] = "Reassign or archive this teacher's active classes before deleting the account.";
                    return RedirectToAction("Teachers");
                }

                var teacherIsActive = teacher.Status == "Active" || string.IsNullOrEmpty(teacher.Status);
                if (IsTeacherActor && teacherIsActive && await ActiveTeacherCountAsync() <= 1)
                {
                    TempData["ErrorMessage"] = "The last active Teacher cannot be deleted.";
                    return RedirectToAction(nameof(Teachers));
                }

                teacher.Status = "Inactive";
                await _context.SaveChangesAsync();
                await AuditAsync("DeactivateTeacher", $"Deactivated teacher {teacher.Username}; historical records retained");
                TempData["Message"] = $"Teacher '{teacher.Username}' deactivated. Historical records were retained.";
            }
            return RedirectToAction("Teachers");
        }

        // ---------- Student accounts ----------
        [TeacherSharedAction]
        public async Task<IActionResult> Students()
        {
            if (!CheckAccess()) return Denied();
            ViewBag.Computers = await _context.Computers.Where(c => c.Status != "Archived").ToListAsync();
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
        [TeacherSharedAction]
        public async Task<IActionResult> CreateStudent([Bind("StudentNumber,FirstName,LastName,FullName,Username,PasswordHash,Status,GradeSection,ClassId,AdviserId")] Student student)
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
            if (await LoginIdentifierInUseAsync(student.Username))
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
            if (await LoginIdentifierInUseAsync(student.StudentNumber))
            {
                TempData["ErrorMessage"] = $"The login identifier '{student.StudentNumber}' is already in use.";
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
        [TeacherSharedAction]
        public async Task<IActionResult> UpdateStudent([Bind("Id,StudentNumber,FirstName,LastName,FullName,Username,Status,GradeSection,ClassId,AdviserId")] Student student, string? newPassword)
        {
            if (!CheckAccess()) return Denied();
            var existing = await _context.Students.FindAsync(student.Id);
            if (existing == null) return RedirectToAction("Students");

            var requestedStudentNumber = string.IsNullOrWhiteSpace(student.StudentNumber) ? existing.StudentNumber : student.StudentNumber.Trim();
            var requestedUsername = string.IsNullOrWhiteSpace(student.Username) ? existing.Username : student.Username.Trim();
            if (await LoginIdentifierInUseAsync(requestedStudentNumber, AccountRole.Student, existing.Id) ||
                await LoginIdentifierInUseAsync(requestedUsername, AccountRole.Student, existing.Id))
            {
                TempData["ErrorMessage"] = "The student number or username is already in use.";
                return RedirectToAction("Students");
            }

            existing.StudentNumber = requestedStudentNumber;
            existing.Username = requestedUsername;
            existing.Status = string.IsNullOrWhiteSpace(student.Status) ? existing.Status : student.Status.Trim();
            existing.GradeSection = student.GradeSection?.Trim() ?? string.Empty;
            existing.ClassId = student.ClassId;
            existing.AdviserId = student.AdviserId;

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
                existing.PasswordHash = _hasher.HashPassword(new object(), newPassword.Trim());
            }

            await _context.SaveChangesAsync();
            await AuditAsync("UpdateStudent", $"Updated student {existing.Username}");
            TempData["Message"] = $"Student '{existing.FullName}' updated successfully!";
            return RedirectToAction("Students");
        }

        [HttpPost]
        [TeacherSharedAction]
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

                await _sessionLifecycle.EndStudentSessionsAndNotifyAsync(student.Id);
                student.Status = "Inactive";
                await _context.SaveChangesAsync();
                await AuditAsync("DeactivateStudent", $"Deactivated student {student.Username}; historical records retained");
                TempData["Message"] = $"Student '{student.Username}' deactivated. Historical records were retained.";
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
        [TeacherSharedAction]
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
        [TeacherSharedAction]
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
            if (IsTeacherActor)
            {
                rule.IsGlobal = true;
                rule.TeacherId = null;
            }
            rule.CreatedAt = DateTime.UtcNow;
            _context.RestrictionRules.Add(rule);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateRestriction", $"Added restriction on {rule.Target}");
            TempData["Message"] = $"Restriction rule on '{rule.Target}' saved!";
            return RedirectToAction("Restrictions");
        }

        [HttpPost]
        [TeacherSharedAction]
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
            rule.IsGlobal = IsTeacherActor || input.IsGlobal;
            if (IsTeacherActor) rule.TeacherId = null;
            rule.IsActive = input.IsActive;
            await _context.SaveChangesAsync();
            await AuditAsync("UpdateRestriction", $"Updated restriction {rule.RestrictionRuleId}");
            return RedirectToAction("Restrictions");
        }

        [HttpPost]
        [TeacherSharedAction]
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
        [TeacherSharedAction]
        public async Task<IActionResult> Blacklists()
        {
            if (!CheckAccess()) return Denied();
            return View(await _context.BlacklistItems.OrderByDescending(b => b.CreatedAt).ToListAsync());
        }

        [TeacherSharedAction]
        public async Task<IActionResult> Whitelists()
        {
            if (!CheckAccess()) return Denied();
            return View(await _context.RestrictionRules.Where(r => r.IsActive && r.Mode == "Allow").OrderByDescending(r => r.CreatedAt).ToListAsync());
        }

        [HttpPost]
        [TeacherSharedAction]
        public async Task<IActionResult> CreateWhitelist([Bind("RuleType,Target,Description,IsGlobal,IsActive")] RestrictionRule rule)
        {
            rule.Mode = "Allow";
            return await CreateRestriction(rule);
        }

        [HttpPost]
        [TeacherSharedAction]
        public async Task<IActionResult> UpdateWhitelist([Bind("RestrictionRuleId,RuleType,Target,Description,IsGlobal,IsActive")] RestrictionRule rule)
        {
            rule.Mode = "Allow";
            return await UpdateRestriction(rule);
        }

        [HttpPost]
        [TeacherSharedAction]
        public async Task<IActionResult> CreateBlacklist([Bind("TargetType,Value,Reason,IsActive")] BlacklistItem item)
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
            item.CreatedAt = DateTime.UtcNow;
            _context.BlacklistItems.Add(item);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateBlacklist", $"Blacklisted {item.TargetType}: {item.Value}");
            TempData["Message"] = $"Blacklist entry '{item.Value}' created!";
            return RedirectToAction(nameof(Blacklists));
        }

        [HttpPost]
        [TeacherSharedAction]
        public async Task<IActionResult> UpdateBlacklist([Bind("BlacklistItemId,TargetType,Value,Reason,IsActive")] BlacklistItem input)
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
        [TeacherSharedAction]
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
        [TeacherSharedAction]
        public async Task<IActionResult> CreateApplicationCategory(ApplicationCategory category)
            => await SaveApplicationCategory(category, null);

        [HttpPost]
        [TeacherSharedAction]
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
        [TeacherSharedAction]
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
        [TeacherSharedAction]
        public async Task<IActionResult> CreateWebsiteCategory(WebsiteCategory category)
            => await SaveWebsiteCategory(category, null);

        [HttpPost]
        [TeacherSharedAction]
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
        [TeacherSharedAction]
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
        [TeacherSharedAction]
        public async Task<IActionResult> SessionRules()
        {
            if (!CheckAccess()) return Denied();
            return View(await _context.SessionRules.OrderByDescending(s => s.CreatedAt).ToListAsync());
        }

        [HttpPost]
        [TeacherSharedAction]
        public async Task<IActionResult> CreateSessionRule([Bind("Name,MaxDurationMinutes,AllowPause,AllowRemoteControl,IsDefault,IsActive")] SessionRule rule)
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
        [TeacherSharedAction]
        public async Task<IActionResult> UpdateSessionRule([Bind("SessionRuleId,Name,MaxDurationMinutes,AllowPause,AllowRemoteControl,IsDefault,IsActive")] SessionRule input)
        {
            if (!CheckAccess()) return Denied();
            var rule = await _context.SessionRules.FindAsync(input.SessionRuleId);
            if (rule == null || string.IsNullOrWhiteSpace(input.Name) || input.MaxDurationMinutes < 1) return RedirectToAction(nameof(SessionRules));
            if (input.IsDefault)
                await _context.SessionRules.Where(r => r.SessionRuleId != rule.SessionRuleId && r.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(r => r.IsDefault, false));
            var closesRemoteControl = rule.AllowRemoteControl && (!input.AllowRemoteControl || !input.IsActive);
            rule.Name = input.Name.Trim(); rule.MaxDurationMinutes = input.MaxDurationMinutes; rule.AllowPause = input.AllowPause;
            rule.AllowRemoteControl = input.AllowRemoteControl; rule.IsDefault = input.IsDefault; rule.IsActive = input.IsActive;
            if (closesRemoteControl)
                await _sessionLifecycle.CloseRemoteSessionsForRuleAsync(rule.SessionRuleId);
            await _context.SaveChangesAsync();
            await AuditAsync("UpdateSessionRule", $"Updated session rule {rule.Name}");
            return RedirectToAction(nameof(SessionRules));
        }

        [HttpPost]
        [TeacherSharedAction]
        public async Task<IActionResult> DeleteSessionRule(int id)
        {
            if (!CheckAccess()) return Denied();
            var rule = await _context.SessionRules.FindAsync(id);
            if (rule != null)
            {
                var closesRemoteControl = rule.AllowRemoteControl && rule.IsActive;
                rule.IsActive = false;
                rule.IsDefault = false;
                if (closesRemoteControl)
                    await _sessionLifecycle.CloseRemoteSessionsForRuleAsync(rule.SessionRuleId);
                await _context.SaveChangesAsync();
                await AuditAsync("DeactivateSessionRule", $"Deactivated session rule {rule.Name}; historical sessions retained");
                TempData["Message"] = "Session rule deactivated. Historical sessions were retained.";
            }
            return RedirectToAction("SessionRules");
        }

        // ---------- LAN configuration ----------
        public IActionResult LanConfig()
        {
            if (!CheckAccess()) return Denied();
            ViewBag.DetectedEndpoint = $"{Request.Scheme}://{Request.Host}";
            ViewBag.RemoteMonitoringEndpoint = $"{Request.Scheme}://{Request.Host}/remoteMonitoringHub";
            return View();
        }

        // ---------- Computers ----------
        [TeacherSharedAction]
        public async Task<IActionResult> Computers()
        {
            if (!CheckAccess()) return Denied();
            return View(await _context.Computers.OrderBy(c => c.LaboratoryStation).ToListAsync());
        }

        [TeacherSharedAction]
        public async Task<IActionResult> ComputerHistory(int id)
        {
            if (!CheckAccess()) return Denied();
            var computer = await _context.Computers.AsNoTracking().FirstOrDefaultAsync(c => c.ComputerId == id);
            if (computer == null) return NotFound();
            ViewBag.Computer = computer;
            return View(await _context.ComputerStatusHistories.AsNoTracking().Where(h => h.ComputerId == id).OrderByDescending(h => h.ChangedAt).ToListAsync());
        }

        [HttpPost]
        [TeacherSharedAction]
        public async Task<IActionResult> CreateComputer(Computer computer)
        {
            if (!CheckAccess()) return Denied();
            if (string.IsNullOrWhiteSpace(computer.LaboratoryStation))
            {
                TempData["ErrorMessage"] = "Laboratory station name is required.";
                return RedirectToAction("Computers");
            }
            computer.LaboratoryStation = computer.LaboratoryStation.Trim();
            if (await _context.Computers.AnyAsync(c => c.LaboratoryStation.ToLower() == computer.LaboratoryStation.ToLower()))
            {
                TempData["ErrorMessage"] = "A workstation with that station name already exists.";
                return RedirectToAction(nameof(Computers));
            }
            computer.Status = string.IsNullOrWhiteSpace(computer.Status) ? "Available" : computer.Status;
            _context.Computers.Add(computer);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateComputer", $"Added {computer.LaboratoryStation}");
            TempData["Message"] = $"Workstation '{computer.LaboratoryStation}' added!";
            return RedirectToAction("Computers");
        }

        [HttpPost]
        [TeacherSharedAction]
        public async Task<IActionResult> UpdateComputer(Computer computer)
        {
            if (!CheckAccess()) return Denied();
            var existing = await _context.Computers.FindAsync(computer.ComputerId);
            if (existing != null)
            {
                var previousStatus = existing.Status;
                var station = string.IsNullOrWhiteSpace(computer.LaboratoryStation) ? existing.LaboratoryStation : computer.LaboratoryStation.Trim();
                if (await _context.Computers.AnyAsync(c => c.ComputerId != existing.ComputerId && c.LaboratoryStation.ToLower() == station.ToLower()))
                {
                    TempData["ErrorMessage"] = "A workstation with that station name already exists.";
                    return RedirectToAction(nameof(Computers));
                }
                existing.LaboratoryStation = station;
                existing.Status = string.IsNullOrWhiteSpace(computer.Status) ? existing.Status : computer.Status.Trim();
                existing.AssignedTo = computer.AssignedTo;
                if (!string.Equals(previousStatus, existing.Status, StringComparison.OrdinalIgnoreCase))
                    _context.ComputerStatusHistories.Add(new ComputerStatusHistory
                    {
                        ComputerId = existing.ComputerId,
                        Status = existing.Status,
                        ChangedByType = IsTeacherActor ? "Teacher" : "Admin",
                        ChangedById = IsTeacherActor ? ActorTeacherId : HttpContext.Session.GetInt32("AdminId")
                    });
                await _context.SaveChangesAsync();
                await AuditAsync("UpdateComputer", $"Updated computer {existing.LaboratoryStation}");
                TempData["Message"] = $"Workstation '{existing.LaboratoryStation}' updated successfully!";
            }
            return RedirectToAction("Computers");
        }

        [HttpPost]
        [TeacherSharedAction]
        public async Task<IActionResult> DeleteComputer(int id)
        {
            if (!CheckAccess()) return Denied();
            var computer = await _context.Computers.FindAsync(id);
            if (computer != null)
            {
                if (await _context.LabSessions.AnyAsync(s => s.ComputerId == id && s.IsActive))
                {
                    TempData["ErrorMessage"] = "End the active lab session before archiving this workstation.";
                    return RedirectToAction(nameof(Computers));
                }
                computer.Status = "Archived";
                computer.AssignedTo = null;
                await _context.SaveChangesAsync();
                await AuditAsync("ArchiveComputer", $"Archived {computer.LaboratoryStation}; historical records retained");
                TempData["Message"] = $"Workstation '{computer.LaboratoryStation}' archived. Historical records were retained.";
            }
            return RedirectToAction(nameof(Computers));
        }

        // ---------- Workstation-to-Student mapping ----------
        [HttpPost]
        [TeacherSharedAction]
        public async Task<IActionResult> AssignComputer(int studentId, int? computerId)
        {
            if (!CheckAccess()) return Denied();

            if (!await _context.Students.AnyAsync(student => student.Id == studentId)) return NotFound();
            Computer? selected = null;
            if (computerId.HasValue)
            {
                selected = await _context.Computers.FindAsync(computerId.Value);
                if (selected is null) return NotFound();
                if (selected.Status == "Archived")
                {
                    TempData["ErrorMessage"] = "Archived workstations cannot be assigned.";
                    return RedirectToAction(nameof(Students));
                }
                if (!string.IsNullOrWhiteSpace(selected.AssignedTo) && selected.AssignedTo != studentId.ToString())
                {
                    TempData["ErrorMessage"] = "That workstation is already assigned to another student.";
                    return RedirectToAction(nameof(Students));
                }
                if (await _context.LabSessions.AnyAsync(session => session.ComputerId == selected.ComputerId && session.IsActive && session.Status != "Ended"))
                {
                    TempData["ErrorMessage"] = "That workstation is currently in use.";
                    return RedirectToAction(nameof(Students));
                }
            }
            var previousAssignments = await _context.Computers
                .Where(c => c.AssignedTo == studentId.ToString() && (!computerId.HasValue || c.ComputerId != computerId.Value))
                .ToListAsync();
            foreach (var previous in previousAssignments)
            {
                previous.AssignedTo = null;
                if (previous.Status == "Assigned") previous.Status = "Available";
            }
            if (selected is not null)
            {
                selected.AssignedTo = studentId.ToString();
                selected.Status = "Assigned";
            }

            await _context.SaveChangesAsync();
            await AuditAsync("AssignComputer",
                computerId.HasValue ? $"Assigned student {studentId} to workstation {computerId}" : $"Unassigned student {studentId}");
            TempData["Message"] = "Workstation assignment updated successfully!";
            return RedirectToAction("Students");
        }

        [HttpPost]
        [TeacherSharedAction]
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
        [TeacherSharedAction]
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
        [TeacherSharedAction]
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

            await AuditAsync("ClassCreated", $"Created class '{result.Name}'");
            TempData["Message"] = $"Class '{result.Name}' created successfully!";
            return RedirectToAction("Classes");
        }

        [HttpPost, ValidateAntiForgeryToken]
        [TeacherSharedAction]
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

            await AuditAsync("ClassUpdated", $"Updated class '{result.Name}'");
            TempData["Message"] = $"Class '{result.Name}' updated successfully!";
            return RedirectToAction("Classes");
        }

        [HttpPost, ValidateAntiForgeryToken]
        [TeacherSharedAction]
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
        [TeacherSharedAction]
        public async Task<IActionResult> ArchiveClass(int id)
        {
            if (!CheckAccess()) return Denied();
            var existing = await _classManagement.GetClassAsync(id);
            if (existing == null)
            {
                TempData["ErrorMessage"] = "The class was not found.";
                return RedirectToAction("Classes");
            }

            var archived = !existing.IsArchived;
            var result = await _classManagement.SetArchiveStateAsync(id, archived);
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
        [TeacherSharedAction]
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
                await AuditAsync("ClassDeleted", $"Deleted class '{result.Name}'");
                TempData["Message"] = $"Class '{result.Name}' deleted successfully.";
            }
            return RedirectToAction("Classes");
        }

        [TeacherSharedAction]
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
        [TeacherSharedAction]
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
        [TeacherSharedAction]
        public async Task<IActionResult> BulkCreateStudents(List<string>? bulkFirstNames, List<string>? bulkLastNames, List<string>? bulkUserNames, List<string>? bulkPasswords)
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

            var result = await _classManagement.BulkCreateStudentsAsync(rows);
            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
                return RedirectToAction("Students");
            }

            await AuditAsync("BulkCreateStudents", $"Bulk created {result.Count} unassigned student profiles");
            TempData["Message"] = $"Successfully created {result.Count} student profile(s).";
            return RedirectToAction("Students");
        }

        [HttpPost, ValidateAntiForgeryToken]
        [TeacherSharedAction]
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
        [TeacherSharedAction]
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
        [TeacherSharedAction]
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
        [TeacherSharedAction]
        public async Task<IActionResult> EnrollStudents(int classId, List<int>? studentIds, bool moveStudent = false)
        {
            if (!CheckAccess()) return Denied();
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
                var result = await _classManagement.EnrollExistingStudentAsync(classId, studentId, moveStudent);
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
        [TeacherSharedAction]
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

        public async Task<IActionResult> SystemLogs()
        {
            if (!CheckAccess()) return Denied();
            return View(await _context.SystemLogs.AsNoTracking()
                .OrderByDescending(log => log.Timestamp)
                .Take(500)
                .ToListAsync());
        }

        public async Task<IActionResult> ExportSystemLogsCsv()
        {
            if (!CheckAccess()) return Denied();
            var logs = await _context.SystemLogs.AsNoTracking()
                .OrderByDescending(log => log.Timestamp)
                .ToListAsync();
            var csv = new System.Text.StringBuilder("Timestamp,Level,Message,Stack Trace\n");
            foreach (var log in logs)
                csv.AppendLine($"{log.Timestamp:O},{Csv(log.Level)},{Csv(log.Message)},{Csv(log.StackTrace)}");
            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()),
                "text/csv; charset=utf-8",
                $"CAMS-SystemLogs-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
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
