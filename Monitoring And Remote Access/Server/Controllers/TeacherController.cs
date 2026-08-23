using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Controllers
{
    [Authorize(Roles = "Teacher")]
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
            TempData["Message"] = "Lab Session started successfully!";
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
            var students = await _context.Students.OrderBy(s => s.FullName).ToListAsync();
            return View(students);
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

            var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<object>();
            student.PasswordHash = hasher.HashPassword(new object(), student.PasswordHash.Trim());
            student.Status = string.IsNullOrWhiteSpace(student.Status) ? "Active" : student.Status;

            if (string.IsNullOrWhiteSpace(student.StudentNumber))
            {
                student.StudentNumber = $"STU-{DateTime.Now:yyyy}-{new Random().Next(100, 999)}";
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
            await AuditAsync("CreateStudent", $"Created student {student.StudentNumber} - {student.FullName}");
            TempData["Message"] = $"Student '{student.FullName}' registered successfully!";
            return RedirectToAction("Students");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStudent(Student student, string? newPassword)
        {
            if (!CheckAccess()) return Denied();
            var existing = await _context.Students.FindAsync(student.Id);
            if (existing == null) return RedirectToAction("Students");

            existing.StudentNumber = string.IsNullOrWhiteSpace(student.StudentNumber) ? existing.StudentNumber : student.StudentNumber.Trim();
            existing.Username = string.IsNullOrWhiteSpace(student.Username) ? existing.Username : student.Username.Trim();

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
            var existing = await _context.Students.FindAsync(studentId);
            if (existing != null)
            {
                var computer = await _context.Computers.FirstOrDefaultAsync(c => c.AssignedTo == studentId.ToString());
                if (computer != null)
                {
                    computer.AssignedTo = null;
                    if (computer.Status == "Assigned") computer.Status = "Available";
                }

                var joinRecords = await _context.ClassStudents.Where(cs => cs.StudentId == studentId).ToListAsync();
                _context.ClassStudents.RemoveRange(joinRecords);

                _context.Students.Remove(existing);
                await _context.SaveChangesAsync();
                await AuditAsync("DeleteStudent", $"Deleted student {existing.StudentNumber}");
                TempData["Message"] = $"Student '{existing.FullName}' removed from roster.";
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
            var classes = await _context.Classes
                .Include(c => c.Teacher)
                .Include(c => c.Students)
                .Include(c => c.ClassStudents)
                    .ThenInclude(cs => cs.Student)
                .OrderBy(c => c.ClassName)
                .ToListAsync();

            ViewBag.TeacherList = await _context.Teachers
                .Where(t => t.Status == "Active" || string.IsNullOrEmpty(t.Status))
                .OrderBy(t => t.FirstName)
                .ToListAsync();

            return View(classes);
        }

        [HttpPost]
        public async Task<IActionResult> CreateClass(Class cls)
        {
            if (!CheckAccess()) return Denied();
            if (string.IsNullOrWhiteSpace(cls.ClassName))
            {
                TempData["ErrorMessage"] = "Section Name is required.";
                return RedirectToAction("Classes");
            }

            cls.CreatedAt = DateTime.Now;
            cls.Status = string.IsNullOrWhiteSpace(cls.Status) ? "Active" : cls.Status;
            cls.AcademicYear = string.IsNullOrWhiteSpace(cls.AcademicYear) ? "2026-2027" : cls.AcademicYear;

            if (!cls.TeacherId.HasValue)
            {
                cls.TeacherId = HttpContext.Session.GetInt32("TeacherId");
            }

            _context.Classes.Add(cls);
            await _context.SaveChangesAsync();
            await AuditAsync("CreateClass", $"Created class '{cls.ClassName}'");
            TempData["Message"] = $"Class '{cls.ClassName}' created successfully!";
            return RedirectToAction("Classes");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateClass(Class cls)
        {
            if (!CheckAccess()) return Denied();
            var existing = await _context.Classes.FindAsync(cls.ClassId);
            if (existing == null) return RedirectToAction("Classes");

            string oldName = existing.ClassName;
            existing.ClassName = string.IsNullOrWhiteSpace(cls.ClassName) ? existing.ClassName : cls.ClassName.Trim();
            existing.Section = string.IsNullOrWhiteSpace(cls.Section) ? existing.Section : cls.Section.Trim();
            existing.Subject = string.IsNullOrWhiteSpace(cls.Subject) ? existing.Subject : cls.Subject.Trim();
            existing.GradeLevel = string.IsNullOrWhiteSpace(cls.GradeLevel) ? existing.GradeLevel : cls.GradeLevel.Trim();
            existing.Schedule = string.IsNullOrWhiteSpace(cls.Schedule) ? existing.Schedule : cls.Schedule.Trim();
            existing.AcademicYear = string.IsNullOrWhiteSpace(cls.AcademicYear) ? existing.AcademicYear : cls.AcademicYear.Trim();
            existing.Status = string.IsNullOrWhiteSpace(cls.Status) ? existing.Status : cls.Status.Trim();
            existing.TeacherId = cls.TeacherId;

            var enrolledStudents = await _context.Students
                .Where(s => s.ClassId == existing.ClassId || (s.GradeSection != null && s.GradeSection == oldName))
                .ToListAsync();

            foreach (var st in enrolledStudents)
            {
                st.ClassId = existing.ClassId;
                st.GradeSection = existing.ClassName;
                st.AdviserId = existing.TeacherId;
            }

            await _context.SaveChangesAsync();
            await AuditAsync("UpdateClass", $"Updated class '{existing.ClassName}'");
            TempData["Message"] = $"Class '{existing.ClassName}' updated successfully!";
            return RedirectToAction("Classes");
        }

        [HttpPost]
        public async Task<IActionResult> AssignTeacher(int classId, int? teacherId)
        {
            if (!CheckAccess()) return Denied();
            var cls = await _context.Classes.FindAsync(classId);
            if (cls != null)
            {
                cls.TeacherId = teacherId;
                var enrolledStudents = await _context.Students
                    .Where(s => s.ClassId == cls.ClassId || (s.GradeSection != null && s.GradeSection == cls.ClassName))
                    .ToListAsync();

                foreach (var st in enrolledStudents)
                {
                    st.AdviserId = teacherId;
                    st.ClassId = cls.ClassId;
                }

                await _context.SaveChangesAsync();
                await AuditAsync("AssignTeacher", $"Assigned teacher {teacherId} to class '{cls.ClassName}'");
                TempData["Message"] = "Teacher assigned successfully to the class!";
            }
            return RedirectToAction("ClassDetails", new { id = classId });
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveClass(int id)
        {
            if (!CheckAccess()) return Denied();
            var existing = await _context.Classes.FindAsync(id);
            if (existing != null)
            {
                existing.IsArchived = !existing.IsArchived;
                existing.Status = existing.IsArchived ? "Archived" : "Active";
                await _context.SaveChangesAsync();
                await AuditAsync("ArchiveClass", $"Updated class status for '{existing.ClassName}' to {existing.Status}");
                TempData["Message"] = $"Class '{existing.ClassName}' status updated to {existing.Status}.";
            }
            return RedirectToAction("Classes");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteClass(int classId)
        {
            if (!CheckAccess()) return Denied();
            var targetClass = await _context.Classes.FindAsync(classId);
            if (targetClass != null)
            {
                var enrolledStudents = await _context.Students
                    .Where(s => s.ClassId == targetClass.ClassId || (s.GradeSection != null && s.GradeSection == targetClass.ClassName))
                    .ToListAsync();

                foreach (var st in enrolledStudents)
                {
                    st.ClassId = null;
                    st.GradeSection = string.Empty;
                    st.AdviserId = null;
                }

                var joinRecords = await _context.ClassStudents.Where(cs => cs.ClassId == targetClass.ClassId).ToListAsync();
                _context.ClassStudents.RemoveRange(joinRecords);

                _context.Classes.Remove(targetClass);
                await _context.SaveChangesAsync();
                await AuditAsync("DeleteClass", $"Deleted class '{targetClass.ClassName}'");
                TempData["Message"] = $"Class '{targetClass.ClassName}' deleted successfully!";
            }
            return RedirectToAction("Classes");
        }

        public async Task<IActionResult> ClassDetails(int id)
        {
            if (!CheckAccess()) return Denied();
            var cls = await _context.Classes
                .Include(c => c.Teacher)
                .Include(c => c.Students)
                .Include(c => c.ClassStudents)
                    .ThenInclude(cs => cs.Student)
                .FirstOrDefaultAsync(c => c.ClassId == id);
            if (cls == null) return RedirectToAction("Classes");

            var classStudentIds = cls.ClassStudents.Select(cs => cs.StudentId).ToList();
            var enrolledList = await _context.Students
                .Include(s => s.Adviser)
                .Where(s => s.ClassId == cls.ClassId || (s.GradeSection != null && s.GradeSection == cls.ClassName) || classStudentIds.Contains(s.Id))
                .ToListAsync();

            ViewBag.EnrolledStudents = enrolledList;
            ViewBag.AvailableTeachers = await _context.Teachers.Where(t => t.Status == "Active" || string.IsNullOrEmpty(t.Status)).ToListAsync();
            ViewBag.AllStudents = await _context.Students.OrderBy(s => s.FullName).ToListAsync();
            return View(cls);
        }

        [HttpPost]
        public async Task<IActionResult> AddStudentToClass(int classId, string firstName, string lastName, string? username, string? password)
        {
            if (!CheckAccess()) return Denied();
            var cls = await _context.Classes.FindAsync(classId);
            if (cls == null) return RedirectToAction("Classes");

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            {
                TempData["ErrorMessage"] = "First Name and Last Name are required.";
                return RedirectToAction("ClassDetails", new { id = classId });
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                TempData["ErrorMessage"] = "A password is required for a new student.";
                return RedirectToAction("ClassDetails", new { id = classId });
            }

            string uName = string.IsNullOrWhiteSpace(username)
                ? $"{firstName.ToLower().Replace(" ", "")}.{lastName.ToLower().Replace(" ", "")}"
                : username.Trim();
            string pwd = password.Trim();

            var student = new Student
            {
                StudentNumber = $"STU-{DateTime.Now:yyyy}-{new Random().Next(100, 999)}",
                FirstName = firstName.Trim(),
                LastName = lastName.Trim(),
                FullName = $"{firstName.Trim()} {lastName.Trim()}",
                Username = uName,
                PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<object>().HashPassword(new object(), pwd),
                Status = "Active",
                GradeSection = cls.ClassName,
                ClassId = cls.ClassId,
                AdviserId = cls.TeacherId
            };

            _context.Students.Add(student);
            await _context.SaveChangesAsync();

            _context.ClassStudents.Add(new ClassStudent { ClassId = cls.ClassId, StudentId = student.Id });
            await _context.SaveChangesAsync();

            await AuditAsync("AddStudentToClass", $"Added student {student.FullName} to {cls.ClassName}");
            TempData["Message"] = $"Student '{student.FullName}' added successfully!";
            return RedirectToAction("ClassDetails", new { id = classId });
        }

        [HttpPost]
        public async Task<IActionResult> BulkAddStudents(int classId, List<string> bulkFirstNames, List<string> bulkLastNames, List<string> bulkUserNames, List<string> bulkPasswords)
        {
            if (!CheckAccess()) return Denied();
            var cls = await _context.Classes.FindAsync(classId);
            if (cls == null) return RedirectToAction("Classes");

            if (bulkFirstNames == null || !bulkFirstNames.Any(f => !string.IsNullOrWhiteSpace(f)))
            {
                TempData["ErrorMessage"] = "Please fill in at least one student's First Name and Last Name.";
                return RedirectToAction("ClassDetails", new { id = classId });
            }

            for (int i = 0; i < bulkFirstNames.Count; i++)
            {
                var hasName = !string.IsNullOrWhiteSpace(bulkFirstNames[i]) ||
                              (i < bulkLastNames.Count && !string.IsNullOrWhiteSpace(bulkLastNames[i]));
                var hasPassword = i < bulkPasswords.Count && !string.IsNullOrWhiteSpace(bulkPasswords[i]);
                if (hasName && !hasPassword)
                {
                    TempData["ErrorMessage"] = "A password is required for every new student.";
                    return RedirectToAction("ClassDetails", new { id = classId });
                }
            }

            int addedCount = 0;
            for (int i = 0; i < bulkFirstNames.Count; i++)
            {
                string fName = bulkFirstNames[i]?.Trim() ?? "";
                string lName = (i < bulkLastNames.Count ? bulkLastNames[i]?.Trim() : "") ?? "";
                string uName = (i < bulkUserNames.Count ? bulkUserNames[i]?.Trim() : "") ?? "";
                string pwd = (i < bulkPasswords.Count ? bulkPasswords[i]?.Trim() : "") ?? "";

                if (string.IsNullOrWhiteSpace(fName) && string.IsNullOrWhiteSpace(lName)) continue;
                if (string.IsNullOrWhiteSpace(fName)) fName = "Student";
                if (string.IsNullOrWhiteSpace(lName)) lName = "Student";

                if (string.IsNullOrWhiteSpace(uName))
                {
                    uName = $"{fName.ToLower().Replace(" ", "")}.{lName.ToLower().Replace(" ", "")}{new Random().Next(100, 999)}";
                }
                var student = new Student
                {
                    StudentNumber = $"STU-{DateTime.Now:yyyy}-{new Random().Next(100, 999)}",
                    FirstName = fName,
                    LastName = lName,
                    FullName = $"{fName} {lName}",
                    Username = uName,
                    PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<object>().HashPassword(new object(), pwd),
                    Status = "Active",
                    GradeSection = cls.ClassName,
                    ClassId = cls.ClassId,
                    AdviserId = cls.TeacherId
                };

                _context.Students.Add(student);
                await _context.SaveChangesAsync();

                _context.ClassStudents.Add(new ClassStudent { ClassId = cls.ClassId, StudentId = student.Id });
                await _context.SaveChangesAsync();
                addedCount++;
            }

            await AuditAsync("BulkAddStudents", $"Bulk added {addedCount} students to {cls.ClassName}");
            TempData["Message"] = $"Successfully bulk added {addedCount} student(s) to {cls.ClassName}!";
            return RedirectToAction("ClassDetails", new { id = classId });
        }

        [HttpPost]
        public async Task<IActionResult> EnrollStudent(int classId, int studentId)
        {
            if (!CheckAccess()) return Denied();
            var cls = await _context.Classes.FindAsync(classId);
            var student = await _context.Students.FindAsync(studentId);
            if (cls != null && student != null)
            {
                student.ClassId = cls.ClassId;
                student.GradeSection = cls.ClassName;
                student.AdviserId = cls.TeacherId;

                var exists = await _context.ClassStudents.AnyAsync(cs => cs.ClassId == classId && cs.StudentId == studentId);
                if (!exists)
                {
                    _context.ClassStudents.Add(new ClassStudent { ClassId = classId, StudentId = studentId });
                }

                await _context.SaveChangesAsync();
                await AuditAsync("EnrollStudent", $"Enrolled student {studentId} in class {classId}");
                TempData["Message"] = $"Student '{student.FullName}' enrolled in {cls.ClassName}!";
            }
            return RedirectToAction("ClassDetails", new { id = classId });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveStudent(int classId, int studentId)
        {
            if (!CheckAccess()) return Denied();
            var student = await _context.Students.FindAsync(studentId);
            if (student != null)
            {
                student.ClassId = null;
                student.GradeSection = string.Empty;
                student.AdviserId = null;
            }

            var cs = await _context.ClassStudents.FirstOrDefaultAsync(cs => cs.ClassId == classId && cs.StudentId == studentId);
            if (cs != null)
            {
                _context.ClassStudents.Remove(cs);
            }

            await _context.SaveChangesAsync();
            await AuditAsync("RemoveStudent", $"Removed student {studentId} from class {classId}");
            TempData["Message"] = "Student removed from class.";
            return RedirectToAction("ClassDetails", new { id = classId });
        }
    }
}
