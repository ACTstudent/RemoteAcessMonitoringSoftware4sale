using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Controllers
{
    [Authorize(Roles = "Teacher")]
    [AutoValidateAntiforgeryToken]
    public class TeacherController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SessionManagerService _sessionManager;
        private readonly IClassManagementService _classManagement;

        public TeacherController(
            ApplicationDbContext context,
            SessionManagerService sessionManager,
            IClassManagementService? classManagement = null)
        {
            _context = context;
            _sessionManager = sessionManager;
            _classManagement = classManagement ?? new ClassManagementService(context);
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
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Timestamp = DateTime.Now
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
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            ViewBag.Students = teacherId.HasValue
                ? await _classManagement.GetStudentsForTeacherAsync(teacherId.Value)
                : Array.Empty<Student>();
            var sessions = await _context.LabSessions
                .Include(s => s.Student)
                .Include(s => s.Teacher)
                .Include(s => s.Computer)
                .Where(s => s.TeacherId == teacherId)
                .OrderByDescending(s => s.StartTime)
                .Take(100)
                .ToListAsync();
            return View(sessions);
        }

        [HttpPost]
        public async Task<IActionResult> StartSession(int studentId, int? computerId, int? sessionRuleId)
        {
            if (!CheckAccess()) return Denied();

            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            var student = teacherId.HasValue
                ? await _context.Students
                    .Include(s => s.Class)
                    .FirstOrDefaultAsync(s => s.Id == studentId &&
                                              _context.Classes.Any(c =>
                                                  c.TeacherId == teacherId.Value &&
                                                  !c.IsArchived &&
                                                  (c.ClassId == s.ClassId ||
                                                   _context.ClassStudents.Any(cs => cs.ClassId == c.ClassId && cs.StudentId == s.Id))))
                : null;
            if (student == null)
            {
                TempData["ErrorMessage"] = "You can only start sessions for students assigned to one of your active classes.";
                return RedirectToAction("Sessions");
            }

            var rule = sessionRuleId.HasValue
                ? await _context.SessionRules.FindAsync(sessionRuleId.Value)
                : await _context.SessionRules.FirstOrDefaultAsync(r => r.IsDefault);

            var session = new LabSession
            {
                StudentId = studentId,
                TeacherId = teacherId,
                ComputerId = computerId,
                SessionRuleId = rule?.SessionRuleId,
                PCName = computerId.HasValue
                    ? (await _context.Computers.FindAsync(computerId.Value))?.LaboratoryStation ?? ""
                    : student.Username,
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
            TempData["Message"] = "Lab Session started successfully!";
            return RedirectToAction("Sessions");
        }

        [HttpPost]
        public async Task<IActionResult> TogglePause(int id)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var session = await _context.LabSessions.FirstOrDefaultAsync(s => s.Id == id && s.TeacherId == teacherId.Value);
            if (session != null)
            {
                if (session.Status == "Running")
                {
                    session.Status = "Paused";
                    session.PauseTime = DateTime.Now;
                }
                else if (session.Status == "Paused")
                {
                    if (session.PauseTime.HasValue)
                    {
                        session.StartTime = session.StartTime.Add(DateTime.Now - session.PauseTime.Value);
                        session.PauseTime = null;
                    }
                    session.Status = "Running";
                }
                await _context.SaveChangesAsync();
                await AuditAsync("TogglePause", $"Session {id} -> {session.Status}");
                TempData["Message"] = $"Session status toggled to {session.Status}.";
            }
            return RedirectToAction("Sessions");
        }

        [HttpPost]
        public async Task<IActionResult> EndSession(int id)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var session = await _context.LabSessions.FirstOrDefaultAsync(s => s.Id == id && s.TeacherId == teacherId.Value);
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
                TempData["Message"] = "Lab Session ended successfully!";
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
            var studentIds = await _context.Students
                .Where(s => s.AdviserId == teacherId || (s.Class != null && s.Class.TeacherId == teacherId))
                .Select(s => s.Id)
                .ToListAsync();
            ViewBag.ApplicationUsage = await _context.UsageLogs
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

        // ---------- Student Management ----------
        public async Task<IActionResult> Students()
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var students = await _classManagement.GetStudentsForTeacherAsync(teacherId.Value);
            return View(students);
        }

        [HttpPost]
        public async Task<IActionResult> CreateStudent(Student student, int? classId = null)
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

        [HttpPost]
        public async Task<IActionResult> UpdateStudent(Student student, string? newPassword)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            var existing = teacherId.HasValue
                ? await _context.Students
                    .Include(s => s.Class)
                    .FirstOrDefaultAsync(s => s.Id == student.Id &&
                                              _context.Classes.Any(c =>
                                                  c.TeacherId == teacherId.Value &&
                                                  !c.IsArchived &&
                                                  (c.ClassId == s.ClassId ||
                                                   _context.ClassStudents.Any(cs => cs.ClassId == c.ClassId && cs.StudentId == s.Id))))
                : null;
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
                var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<object>();
                existing.PasswordHash = hasher.HashPassword(new object(), newPassword.Trim());
            }

            await _context.SaveChangesAsync();
            await AuditAsync("UpdateStudent", $"Updated student {student.StudentNumber}");
            TempData["Message"] = $"Student '{existing.FullName}' updated successfully!";
            return RedirectToAction("Students");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteStudent(int studentId)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            var existing = await _context.Students.FindAsync(studentId);
            var accessibleClassId = existing == null
                ? 0
                : await _context.Classes
                    .Where(c => c.TeacherId == teacherId.Value &&
                                !c.IsArchived &&
                                (c.ClassId == existing.ClassId ||
                                 _context.ClassStudents.Any(cs => cs.ClassId == c.ClassId && cs.StudentId == existing.Id)))
                    .Select(c => c.ClassId)
                    .FirstOrDefaultAsync();
            if (existing != null && accessibleClassId != 0)
            {
                var computer = await _context.Computers.FirstOrDefaultAsync(c => c.AssignedTo == studentId.ToString());
                if (computer != null)
                {
                    computer.AssignedTo = null;
                    if (computer.Status == "Assigned") computer.Status = "Available";
                }

                var result = await _classManagement.RemoveStudentAsync(accessibleClassId, studentId, teacherId.Value);
                if (!result.Success)
                {
                    TempData["ErrorMessage"] = result.Error;
                }
                else
                {
                    await AuditAsync("RemoveStudent", $"Removed student {studentId} from class {accessibleClassId}");
                    TempData["Message"] = $"Student '{result.Name}' removed from your roster. The account was preserved.";
                }
            }
            return RedirectToAction("Students");
        }

        // ---------- Computer Management ----------
        public async Task<IActionResult> Computers()
        {
            if (!CheckAccess()) return Denied();
            var computers = await _context.Computers.OrderBy(c => c.LaboratoryStation).ToListAsync();
            return View(computers);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateComputer(Computer computer)
        {
            if (!CheckAccess()) return Denied();
            var existing = await _context.Computers.FindAsync(computer.ComputerId);
            if (existing != null)
            {
                existing.LaboratoryStation = string.IsNullOrWhiteSpace(computer.LaboratoryStation) ? existing.LaboratoryStation : computer.LaboratoryStation.Trim();
                existing.Status = string.IsNullOrWhiteSpace(computer.Status) ? existing.Status : computer.Status.Trim();
                existing.AssignedTo = computer.AssignedTo;
                await _context.SaveChangesAsync();
                await AuditAsync("UpdateComputer", $"Updated computer {existing.LaboratoryStation}");
                TempData["Message"] = $"Workstation '{existing.LaboratoryStation}' status updated!";
            }
            return RedirectToAction("Computers");
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

            await AuditAsync("CreateClass", $"Created class '{result.Name}'");
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

            await AuditAsync("UpdateClass", $"Updated class '{result.Name}'");
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

            var result = await _classManagement.SetArchiveStateAsync(id, !existing.IsArchived, teacherId.Value);
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
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

            var result = await _classManagement.DeleteClassAsync(classId, teacherId.Value);
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
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();
            var cls = await _classManagement.GetClassAsync(id, teacherId.Value);
            if (cls == null) return RedirectToAction("Classes");

            await _classManagement.EnsureMembershipLinksAsync(id);
            var roster = await _classManagement.GetRosterAsync(id);
            cls.ClassStudents = roster.ToList();
            ViewBag.EnrolledStudents = roster;
            ViewBag.AllStudents = await _context.Students
                .Include(s => s.Class)
                .Where(s => s.ClassId == null || s.ClassId == id ||
                            (s.Class != null && s.Class.TeacherId == teacherId.Value && !s.Class.IsArchived))
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
        public async Task<IActionResult> EnrollStudent(int classId, int studentId, bool moveStudent = false)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

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
        public async Task<IActionResult> RemoveStudent(int classId, int studentId)
        {
            if (!CheckAccess()) return Denied();
            var teacherId = HttpContext.Session.GetInt32("TeacherId");
            if (!teacherId.HasValue) return Denied();

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
