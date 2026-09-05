using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Server.Data;
using Server.Models;

namespace Server.Services;

public sealed record ClassInput(
    string? ClassName,
    string? Section,
    string? Subject,
    string? GradeLevel,
    string? Schedule,
    string? AcademicYear,
    int? TeacherId);

public sealed record NewStudentInput(
    string? StudentNumber,
    string? FirstName,
    string? LastName,
    string? FullName,
    string? Username,
    string? Password);

public sealed record ClassOperationResult(
    bool Success,
    string? Error = null,
    string? Name = null,
    int Count = 0)
{
    public static ClassOperationResult Ok(string? name = null, int count = 0) =>
        new(true, Name: name, Count: count);

    public static ClassOperationResult Fail(string error) =>
        new(false, Error: error);
}

public sealed record BulkStudentRow(int RowNumber, NewStudentInput Input, string? Error = null)
{
    public bool IsValid => string.IsNullOrWhiteSpace(Error);
}

public sealed record BulkStudentPreview(IReadOnlyList<BulkStudentRow> Rows)
{
    public int ValidCount => Rows.Count(row => row.IsValid);
    public int ErrorCount => Rows.Count(row => !row.IsValid);
}

public sealed record BulkStudentImport(IReadOnlyList<NewStudentInput> Rows, IReadOnlyList<BulkStudentRow> Errors);

public interface IClassManagementService
{
    Task<IReadOnlyList<Class>> GetClassesAsync(int? teacherId = null);
    Task<Class?> GetClassAsync(int classId, int? teacherId = null);
    Task<IReadOnlyList<ClassStudent>> GetRosterAsync(int classId);
    Task<IReadOnlyList<Student>> GetStudentsForTeacherAsync(int teacherId);
    Task EnsureMembershipLinksAsync(int classId);
    Task<ClassOperationResult> CreateClassAsync(ClassInput input, int? actorTeacherId, bool isAdmin);
    Task<ClassOperationResult> UpdateClassAsync(int classId, ClassInput input, int? actorTeacherId, bool isAdmin);
    Task<ClassOperationResult> AssignTeacherAsync(int classId, int teacherId);
    Task<ClassOperationResult> SetArchiveStateAsync(int classId, bool archived, int? teacherId = null);
    Task<ClassOperationResult> DeleteClassAsync(int classId, int? teacherId = null);
    Task<ClassOperationResult> EnrollExistingStudentAsync(int classId, int studentId, bool moveStudent, int? teacherId = null);
    Task<ClassOperationResult> CreateStudentInClassAsync(int classId, NewStudentInput input, int? teacherId = null);
    Task<ClassOperationResult> BulkCreateStudentsAsync(IReadOnlyList<NewStudentInput> inputs);
    Task<ClassOperationResult> BulkCreateStudentsInClassAsync(int classId, IReadOnlyList<NewStudentInput> inputs, int? teacherId = null);
    Task<BulkStudentPreview> PreviewBulkStudentsAsync(int classId, IReadOnlyList<NewStudentInput> inputs, int? teacherId = null);
    Task<BulkStudentImport> ValidateBulkStudentsAsync(int classId, IReadOnlyList<BulkStudentRow> rows, int? teacherId = null);
    BulkStudentPreview ParseBulkStudentsCsv(string csv);
    Task<ClassOperationResult> RemoveStudentAsync(int classId, int studentId, int? teacherId = null);
}

public sealed class ClassManagementService : IClassManagementService
{
    private readonly ApplicationDbContext _context;
    private readonly PasswordHasher<object> _hasher = new();

    public ClassManagementService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Class>> GetClassesAsync(int? teacherId = null)
    {
        var query = _context.Classes
            .Include(c => c.Teacher)
            .Include(c => c.ClassStudents)
                .ThenInclude(cs => cs.Student)
            .AsNoTracking()
            .AsQueryable();

        if (teacherId.HasValue)
        {
            query = query.Where(c => c.TeacherId == teacherId.Value);
        }

        var classes = await query
            .OrderBy(c => c.IsArchived)
            .ThenBy(c => c.ClassName)
            .ToListAsync();

        foreach (var entity in classes)
        {
            await EnsureMembershipLinksAsync(entity.ClassId);
        }

        return await query
            .OrderBy(c => c.IsArchived)
            .ThenBy(c => c.ClassName)
            .ToListAsync();
    }

    public async Task<Class?> GetClassAsync(int classId, int? teacherId = null)
    {
        var query = _context.Classes
            .Include(c => c.Teacher)
            .Include(c => c.ClassStudents)
                .ThenInclude(cs => cs.Student)
            .AsQueryable();

        if (teacherId.HasValue)
        {
            query = query.Where(c => c.TeacherId == teacherId.Value);
        }

        return await query.FirstOrDefaultAsync(c => c.ClassId == classId);
    }

    public async Task<IReadOnlyList<ClassStudent>> GetRosterAsync(int classId)
    {
        await EnsureMembershipLinksAsync(classId);
        return await _context.ClassStudents
            .Include(cs => cs.Student)
                .ThenInclude(s => s!.Adviser)
            .Where(cs => cs.ClassId == classId)
            .OrderBy(cs => cs.Student!.LastName)
            .ThenBy(cs => cs.Student!.FirstName)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Student>> GetStudentsForTeacherAsync(int teacherId)
    {
        // Global access: every teacher can start sessions for every student, regardless of class assignment.
        return await _context.Students
            .Include(s => s.Class)
            .OrderBy(s => s.LastName)
            .ThenBy(s => s.FirstName)
            .ToListAsync();
    }

    public async Task EnsureMembershipLinksAsync(int classId)
    {
        var classEntity = await _context.Classes.FindAsync(classId);
        if (classEntity == null)
        {
            return;
        }

        var existingLinks = await _context.ClassStudents
            .Where(cs => cs.ClassId == classId)
            .OrderBy(cs => cs.ClassStudentId)
            .ToListAsync();
        var linked = new HashSet<int>();
        var duplicateLinks = new List<ClassStudent>();
        var changed = false;
        foreach (var link in existingLinks)
        {
            if (!linked.Add(link.StudentId))
            {
                duplicateLinks.Add(link);
            }
        }

        if (duplicateLinks.Count > 0)
        {
            _context.ClassStudents.RemoveRange(duplicateLinks);
            changed = true;
        }

        var directStudents = await _context.Students
            .Where(s => s.ClassId == classId)
            .ToListAsync();

        foreach (var student in directStudents)
        {
            if (linked.Add(student.Id))
            {
                _context.ClassStudents.Add(new ClassStudent
                {
                    ClassId = classId,
                    StudentId = student.Id,
                    EnrolledAt = DateTime.UtcNow
                });
                changed = true;
            }
        }

        var memberIds = linked.Concat(directStudents.Select(student => student.Id)).Distinct().ToList();
        if (memberIds.Count > 0 && !classEntity.IsArchived)
        {
            var members = await _context.Students
                .Where(student => memberIds.Contains(student.Id))
                .ToListAsync();
            foreach (var student in members)
            {
                if ((student.ClassId == null || student.ClassId == classId) &&
                    (student.ClassId != classId || student.GradeSection != classEntity.ClassName || student.AdviserId != classEntity.TeacherId))
                {
                    student.ClassId = classId;
                    student.GradeSection = classEntity.ClassName;
                    student.AdviserId = classEntity.TeacherId;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            await _context.SaveChangesAsync();
        }
    }

    public async Task<ClassOperationResult> CreateClassAsync(ClassInput input, int? actorTeacherId, bool isAdmin)
    {
        var validation = await ValidateClassInputAsync(input, actorTeacherId, isAdmin, null);
        if (!validation.Success)
        {
            return validation;
        }

        var teacherId = isAdmin ? input.TeacherId : actorTeacherId;
        var className = NormalizeRequired(input.ClassName);
        var academicYear = NormalizeOptional(input.AcademicYear) ?? "2026-2027";

        if (await _context.Classes.AnyAsync(c =>
                c.ClassName.ToLower() == className.ToLower() &&
                c.AcademicYear.ToLower() == academicYear.ToLower() &&
                !c.IsArchived))
        {
            return ClassOperationResult.Fail("An active class with the same name and academic year already exists.");
        }

        var entity = new Class
        {
            ClassName = className,
            Section = NormalizeOptional(input.Section) ?? string.Empty,
            Subject = NormalizeOptional(input.Subject) ?? string.Empty,
            GradeLevel = NormalizeOptional(input.GradeLevel) ?? string.Empty,
            Schedule = NormalizeOptional(input.Schedule) ?? string.Empty,
            AcademicYear = academicYear,
            Status = "Active",
            IsArchived = false,
            CreatedAt = DateTime.UtcNow,
            TeacherId = teacherId
        };

        try
        {
            _context.Classes.Add(entity);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CAMS] Class create failed: {ex.Message}");
            return ClassOperationResult.Fail("The class could not be created. Check the details and try again.");
        }
        Console.WriteLine($"[CAMS] Class created: {entity.ClassId} {entity.ClassName}");
        return ClassOperationResult.Ok(entity.ClassName);
    }

    public async Task<ClassOperationResult> UpdateClassAsync(int classId, ClassInput input, int? actorTeacherId, bool isAdmin)
    {
        var entity = await FindAccessibleClassAsync(classId, actorTeacherId);
        if (entity == null)
        {
            return ClassOperationResult.Fail("The class was not found or you do not have access to it.");
        }

        await EnsureMembershipLinksAsync(classId);

        var validation = await ValidateClassInputAsync(input, actorTeacherId, isAdmin, classId, entity.TeacherId);
        if (!validation.Success)
        {
            return validation;
        }

        var className = NormalizeRequired(input.ClassName);
        var academicYear = NormalizeOptional(input.AcademicYear) ?? entity.AcademicYear;
        if (await _context.Classes.AnyAsync(c =>
                c.ClassId != classId &&
                c.ClassName.ToLower() == className.ToLower() &&
                c.AcademicYear.ToLower() == academicYear.ToLower() &&
                !c.IsArchived))
        {
            return ClassOperationResult.Fail("An active class with the same name and academic year already exists.");
        }

        var teacherId = isAdmin ? input.TeacherId : entity.TeacherId;
        entity.ClassName = className;
        entity.Section = NormalizeOptional(input.Section) ?? string.Empty;
        entity.Subject = NormalizeOptional(input.Subject) ?? string.Empty;
        entity.GradeLevel = NormalizeOptional(input.GradeLevel) ?? string.Empty;
        entity.Schedule = NormalizeOptional(input.Schedule) ?? string.Empty;
        entity.AcademicYear = academicYear;
        entity.TeacherId = teacherId;
        entity.Status = entity.IsArchived ? "Archived" : "Active";

        var members = await GetMembersForMutationAsync(classId);
        foreach (var student in members)
        {
            if (student.ClassId == classId)
            {
                student.GradeSection = entity.ClassName;
                student.AdviserId = entity.IsArchived ? null : teacherId;
            }
        }

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CAMS] Class update failed for {classId}: {ex.Message}");
            return ClassOperationResult.Fail("The class could not be updated. Check the details and try again.");
        }
        Console.WriteLine($"[CAMS] Class updated: {entity.ClassId} {entity.ClassName}");
        return ClassOperationResult.Ok(entity.ClassName);
    }

    public async Task<ClassOperationResult> AssignTeacherAsync(int classId, int teacherId)
    {
        var entity = await _context.Classes.FindAsync(classId);
        if (entity == null)
        {
            return ClassOperationResult.Fail("The class was not found.");
        }

        await EnsureMembershipLinksAsync(classId);

        var teacher = await GetActiveTeacherAsync(teacherId);
        if (teacher == null)
        {
            return ClassOperationResult.Fail("Select an active teacher before assigning the class.");
        }

        entity.TeacherId = teacher.TeacherId;
        entity.Status = entity.IsArchived ? "Archived" : "Active";
        var members = await GetMembersForMutationAsync(classId);
        foreach (var student in members)
        {
            if (student.ClassId == classId)
            {
                student.GradeSection = entity.ClassName;
                student.AdviserId = entity.IsArchived ? null : teacher.TeacherId;
            }
        }

        await _context.SaveChangesAsync();
        return ClassOperationResult.Ok(entity.ClassName);
    }

    public async Task<ClassOperationResult> SetArchiveStateAsync(int classId, bool archived, int? teacherId = null)
    {
        var entity = await FindAccessibleClassAsync(classId, teacherId);
        if (entity == null)
        {
            return ClassOperationResult.Fail("The class was not found or you do not have access to it.");
        }

        if (!archived && await GetActiveTeacherAsync(entity.TeacherId) == null)
        {
            return ClassOperationResult.Fail("Assign an active teacher before restoring this class.");
        }

        entity.IsArchived = archived;
        entity.Status = archived ? "Archived" : "Active";
        var members = await GetMembersForMutationAsync(classId);
        foreach (var student in members.Where(student => student.ClassId == classId))
            student.AdviserId = archived ? null : entity.TeacherId;
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CAMS] Class archive change failed for {classId}: {ex.Message}");
            return ClassOperationResult.Fail("The class archive status could not be changed. Please try again.");
        }
        return ClassOperationResult.Ok(entity.ClassName);
    }

    public async Task<ClassOperationResult> DeleteClassAsync(int classId, int? teacherId = null)
    {
        var entity = await FindAccessibleClassAsync(classId, teacherId);
        if (entity == null)
        {
            return ClassOperationResult.Fail("The class was not found or you do not have access to it.");
        }

        await EnsureMembershipLinksAsync(classId);

        var members = await GetMembersForMutationAsync(classId);
        foreach (var student in members)
        {
            if (student.ClassId == classId)
            {
                student.ClassId = null;
                student.GradeSection = string.Empty;
                student.AdviserId = null;
            }
        }

        var links = await _context.ClassStudents
            .Where(cs => cs.ClassId == classId)
            .ToListAsync();
        _context.ClassStudents.RemoveRange(links);
        _context.Classes.Remove(entity);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CAMS] Class delete failed for {classId}: {ex.Message}");
            return ClassOperationResult.Fail("The class could not be deleted. Student accounts were not removed.");
        }
        Console.WriteLine($"[CAMS] Class deleted: {classId} {entity.ClassName}");
        return ClassOperationResult.Ok(entity.ClassName);
    }

    public async Task<ClassOperationResult> EnrollExistingStudentAsync(int classId, int studentId, bool moveStudent, int? teacherId = null)
    {
        var entity = await FindAccessibleClassAsync(classId, teacherId);
        if (entity == null)
        {
            return ClassOperationResult.Fail("The class was not found or you do not have access to it.");
        }

        if (entity.IsArchived || entity.TeacherId == null)
        {
            return ClassOperationResult.Fail("Only an active class with an assigned teacher can receive students.");
        }

        var student = await _context.Students.FindAsync(studentId);
        if (student == null)
        {
            return ClassOperationResult.Fail("The student was not found.");
        }

        var existingMemberships = await _context.ClassStudents
            .Where(cs => cs.StudentId == studentId)
            .ToListAsync();
        var otherMemberships = existingMemberships
            .Where(cs => cs.ClassId != classId)
            .ToList();
        if (student.ClassId.HasValue && student.ClassId.Value != classId &&
            otherMemberships.All(cs => cs.ClassId != student.ClassId.Value))
        {
            otherMemberships.Add(new ClassStudent { ClassId = student.ClassId.Value, StudentId = studentId });
        }

        if (teacherId.HasValue && otherMemberships.Count > 0)
        {
            var otherClassIds = otherMemberships
                .Select(membership => membership.ClassId)
                .Distinct()
                .ToList();
            var ownedClassCount = await _context.Classes.CountAsync(c =>
                otherClassIds.Contains(c.ClassId) &&
                c.TeacherId == teacherId.Value &&
                !c.IsArchived);
            if (ownedClassCount != otherClassIds.Count)
            {
                return ClassOperationResult.Fail("You can only move students from one of your own active classes.");
            }
        }

        if (otherMemberships.Count > 0 && !moveStudent)
        {
            return ClassOperationResult.Fail("This student already belongs to another class. Confirm the move before continuing.");
        }

        if (moveStudent && otherMemberships.Count > 0)
        {
            _context.ClassStudents.RemoveRange(existingMemberships.Where(cs => cs.ClassId != classId));
        }

        student.ClassId = classId;
        student.GradeSection = entity.ClassName;
        student.AdviserId = entity.TeacherId;

        var existingLinks = await _context.ClassStudents
            .Where(cs => cs.ClassId == classId && cs.StudentId == studentId)
            .ToListAsync();
        if (existingLinks.Count == 0)
        {
            _context.ClassStudents.Add(new ClassStudent
            {
                ClassId = classId,
                StudentId = studentId,
                EnrolledAt = DateTime.UtcNow
            });
        }
        else if (existingLinks.Count > 1)
        {
            _context.ClassStudents.RemoveRange(existingLinks.Skip(1));
        }

        await _context.SaveChangesAsync();
        return ClassOperationResult.Ok(student.FullName);
    }

    public Task<ClassOperationResult> CreateStudentInClassAsync(int classId, NewStudentInput input, int? teacherId = null)
    {
        return InTransactionAsync(async () =>
        {
            var entity = await FindAccessibleClassAsync(classId, teacherId);
            if (entity == null)
            {
                return ClassOperationResult.Fail("The class was not found or you do not have access to it.");
            }

            if (entity.IsArchived || entity.TeacherId == null)
            {
                return ClassOperationResult.Fail("Only an active class with an assigned teacher can receive students.");
            }

            var validation = await ValidateStudentInputAsync(input);
            if (!validation.Success)
            {
                return validation;
            }

            var student = await BuildStudentAsync(input, entity);
            _context.Students.Add(student);
            await _context.SaveChangesAsync();
            _context.ClassStudents.Add(new ClassStudent
            {
                ClassId = classId,
                StudentId = student.Id,
                EnrolledAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            return ClassOperationResult.Ok(student.FullName);
        });
    }

    public Task<ClassOperationResult> BulkCreateStudentsInClassAsync(int classId, IReadOnlyList<NewStudentInput> inputs, int? teacherId = null)
    {
        return InTransactionAsync(async () =>
        {
            var entity = await FindAccessibleClassAsync(classId, teacherId);
            if (entity == null)
            {
                return ClassOperationResult.Fail("The class was not found or you do not have access to it.");
            }

            if (entity.IsArchived || entity.TeacherId == null)
            {
                return ClassOperationResult.Fail("Only an active class with an assigned teacher can receive students.");
            }

            var rows = inputs
                .Where(input => input != null &&
                                (!string.IsNullOrWhiteSpace(input.FirstName) ||
                                 !string.IsNullOrWhiteSpace(input.LastName) ||
                                 !string.IsNullOrWhiteSpace(input.FullName) ||
                                 !string.IsNullOrWhiteSpace(input.Username) ||
                                 !string.IsNullOrWhiteSpace(input.Password)))
                .ToList();
            if (rows.Count == 0)
            {
                return ClassOperationResult.Fail("Add at least one student before saving the bulk roster.");
            }

            var students = new List<Student>();
            var usernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var studentNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var validation = await ValidateStudentInputAsync(row);
                if (!validation.Success)
                {
                    return validation;
                }

                var requestedUsername = NormalizeOptional(row.Username);
                if (requestedUsername != null && usernames.Contains(requestedUsername))
                {
                    return ClassOperationResult.Fail($"The username '{requestedUsername}' is repeated in the bulk roster.");
                }

                var requestedStudentNumber = NormalizeOptional(row.StudentNumber);
                if (requestedStudentNumber != null && studentNumbers.Contains(requestedStudentNumber))
                {
                    return ClassOperationResult.Fail($"The student number '{requestedStudentNumber}' is repeated in the bulk roster.");
                }

                var student = await BuildStudentAsync(row, entity, usernames, studentNumbers);
                students.Add(student);
                usernames.Add(student.Username);
                studentNumbers.Add(student.StudentNumber);
            }

            _context.Students.AddRange(students);
            await _context.SaveChangesAsync();
            _context.ClassStudents.AddRange(students.Select(student => new ClassStudent
            {
                ClassId = classId,
                StudentId = student.Id,
                EnrolledAt = DateTime.UtcNow
            }));
            await _context.SaveChangesAsync();
            return ClassOperationResult.Ok(entity.ClassName, students.Count);
        });
    }

    public Task<ClassOperationResult> BulkCreateStudentsAsync(IReadOnlyList<NewStudentInput> inputs)
    {
        return InTransactionAsync(async () =>
        {
            var rows = inputs
                .Where(input => input != null &&
                                (!string.IsNullOrWhiteSpace(input.FirstName) ||
                                 !string.IsNullOrWhiteSpace(input.LastName) ||
                                 !string.IsNullOrWhiteSpace(input.FullName) ||
                                 !string.IsNullOrWhiteSpace(input.Username) ||
                                 !string.IsNullOrWhiteSpace(input.Password)))
                .ToList();
            if (rows.Count == 0)
            {
                return ClassOperationResult.Fail("Add at least one student profile before saving.");
            }

            var students = new List<Student>();
            var usernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var studentNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var validation = await ValidateStudentInputAsync(row);
                if (!validation.Success)
                {
                    return validation;
                }

                var requestedUsername = NormalizeOptional(row.Username);
                if (requestedUsername != null && usernames.Contains(requestedUsername))
                {
                    return ClassOperationResult.Fail($"The username '{requestedUsername}' is repeated in the bulk profiles.");
                }

                var requestedStudentNumber = NormalizeOptional(row.StudentNumber);
                if (requestedStudentNumber != null && studentNumbers.Contains(requestedStudentNumber))
                {
                    return ClassOperationResult.Fail($"The student number '{requestedStudentNumber}' is repeated in the bulk profiles.");
                }

                var student = await BuildStudentAsync(row, null, usernames, studentNumbers);
                students.Add(student);
                usernames.Add(student.Username);
                studentNumbers.Add(student.StudentNumber);
            }

            _context.Students.AddRange(students);
            await _context.SaveChangesAsync();
            return ClassOperationResult.Ok(count: students.Count);
        });
    }

    public BulkStudentPreview ParseBulkStudentsCsv(string csv)
    {
        var rows = new List<BulkStudentRow>();
        var lines = csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
        {
            return new BulkStudentPreview(rows);
        }

        var start = 0;
        var header = ParseCsvLine(lines[0].TrimStart('\uFEFF'));
        var knownHeaders = new[] { "studentnumber", "first", "firstname", "last", "lastname", "fullname", "username", "password" };
        if (header.Any(value => knownHeaders.Contains(value.Trim().Replace(" ", string.Empty).ToLowerInvariant())))
        {
            start = 1;
        }

        for (var i = start; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var values = ParseCsvLine(lines[i]);
            var input = new NewStudentInput(
                Value(values, 0), Value(values, 1), Value(values, 2), Value(values, 3), Value(values, 4), Value(values, 5));
            rows.Add(new BulkStudentRow(i + 1, input));
        }
        return new BulkStudentPreview(rows);
    }

    public async Task<BulkStudentPreview> PreviewBulkStudentsAsync(int classId, IReadOnlyList<NewStudentInput> inputs, int? teacherId = null)
    {
        var previewRows = new List<BulkStudentRow>();
        var usernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cls = await FindAccessibleClassAsync(classId, teacherId);
        if (cls == null)
        {
            return new BulkStudentPreview(new[] { new BulkStudentRow(1, new NewStudentInput(null, null, null, null, null, null), "The class was not found or you do not have access to it.") });
        }

        for (var i = 0; i < inputs.Count; i++)
        {
            var row = inputs[i];
            var validation = await ValidateStudentInputAsync(row);
            var error = validation.Success ? null : validation.Error;
            var requestedUsername = NormalizeOptional(row.Username);
            var requestedNumber = NormalizeOptional(row.StudentNumber);
            if (error == null && requestedUsername != null && !usernames.Add(requestedUsername)) error = $"The username '{requestedUsername}' is repeated in the file.";
            if (error == null && requestedNumber != null && !numbers.Add(requestedNumber)) error = $"The student number '{requestedNumber}' is repeated in the file.";
            previewRows.Add(new BulkStudentRow(i + 1, row, error));
        }
        return new BulkStudentPreview(previewRows);
    }

    public async Task<BulkStudentImport> ValidateBulkStudentsAsync(int classId, IReadOnlyList<BulkStudentRow> rows, int? teacherId = null)
    {
        var preview = await PreviewBulkStudentsAsync(classId, rows.Select(row => row.Input).ToList(), teacherId);
        var merged = preview.Rows.Select((row, index) => row with { RowNumber = rows[index].RowNumber }).ToList();
        return new BulkStudentImport(merged.Where(row => row.IsValid).Select(row => row.Input).ToList(), merged.Where(row => !row.IsValid).ToList());
    }

    private static string? Value(IReadOnlyList<string> values, int index) => index < values.Count ? NormalizeOptional(values[index]) : null;

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; continue; }
            if (c == '"') { quoted = !quoted; continue; }
            if (c == ',' && !quoted) { result.Add(value.ToString()); value.Clear(); continue; }
            value.Append(c);
        }
        result.Add(value.ToString());
        return result;
    }

    public async Task<ClassOperationResult> RemoveStudentAsync(int classId, int studentId, int? teacherId = null)
    {
        var entity = await FindAccessibleClassAsync(classId, teacherId);
        if (entity == null)
        {
            return ClassOperationResult.Fail("The class was not found or you do not have access to it.");
        }

        await EnsureMembershipLinksAsync(classId);

        var student = await _context.Students.FindAsync(studentId);
        if (student == null)
        {
            return ClassOperationResult.Fail("The student was not found.");
        }

        var links = await _context.ClassStudents
            .Where(cs => cs.StudentId == studentId &&
                         (cs.ClassId == classId || student.ClassId == classId))
            .ToListAsync();
        _context.ClassStudents.RemoveRange(links);

        if (student.ClassId == classId)
        {
            student.ClassId = null;
            student.GradeSection = string.Empty;
            student.AdviserId = null;
        }

        await _context.SaveChangesAsync();
        return ClassOperationResult.Ok(student.FullName);
    }

    private async Task<ClassOperationResult> ValidateClassInputAsync(
        ClassInput input,
        int? actorTeacherId,
        bool isAdmin,
        int? currentClassId,
        int? currentTeacherId = null)
    {
        if (string.IsNullOrWhiteSpace(input.ClassName))
        {
            return ClassOperationResult.Fail("Class name is required.");
        }

        var teacherId = isAdmin ? input.TeacherId : actorTeacherId ?? currentTeacherId;
        if (teacherId.HasValue && await GetActiveTeacherAsync(teacherId.Value) == null)
        {
            return ClassOperationResult.Fail("The selected teacher is inactive. Choose an active teacher or leave it unassigned.");
        }

        return ClassOperationResult.Ok();
    }

    private async Task<Class?> FindAccessibleClassAsync(int classId, int? teacherId)
    {
        var query = _context.Classes.AsQueryable();
        if (teacherId.HasValue)
        {
            query = query.Where(c => c.TeacherId == teacherId.Value);
        }

        return await query.FirstOrDefaultAsync(c => c.ClassId == classId);
    }

    private async Task<Teacher?> GetActiveTeacherAsync(int? teacherId)
    {
        if (!teacherId.HasValue)
        {
            return null;
        }

        return await _context.Teachers.FirstOrDefaultAsync(t =>
            t.TeacherId == teacherId.Value &&
            (t.Status == "Active" || string.IsNullOrEmpty(t.Status)));
    }

    private async Task<List<Student>> GetMembersForMutationAsync(int classId)
    {
        var linkedIds = await _context.ClassStudents
            .Where(cs => cs.ClassId == classId)
            .Select(cs => cs.StudentId)
            .ToListAsync();
        var directIds = await _context.Students
            .Where(s => s.ClassId == classId)
            .Select(s => s.Id)
            .ToListAsync();
        var ids = linkedIds.Concat(directIds).Distinct().ToList();
        return ids.Count == 0
            ? new List<Student>()
            : await _context.Students.Where(s => ids.Contains(s.Id)).ToListAsync();
    }

    private async Task<ClassOperationResult> ValidateStudentInputAsync(NewStudentInput input)
    {
        var name = NormalizeName(input);
        if (string.IsNullOrWhiteSpace(name.First) || string.IsNullOrWhiteSpace(name.Last))
        {
            return ClassOperationResult.Fail("Each student needs a first name and last name.");
        }
        // Matches the eight character minimum enforced when an account changes its
        // own password; creating accounts weaker than they can later be changed to
        // left every new student below the policy.

        if (string.IsNullOrWhiteSpace(input.Password) || input.Password.Trim().Length < 8)
        {
            return ClassOperationResult.Fail("Each student password must be at least 8 characters.");
        }

        if (!string.IsNullOrWhiteSpace(input.Username) &&
            await LoginIdentifierInUseAsync(input.Username))
        {
            return ClassOperationResult.Fail($"The username '{input.Username.Trim()}' is already in use.");
        }

        if (!string.IsNullOrWhiteSpace(input.StudentNumber) &&
            await LoginIdentifierInUseAsync(input.StudentNumber))
        {
            return ClassOperationResult.Fail($"The student number '{input.StudentNumber.Trim()}' is already in use.");
        }

        return ClassOperationResult.Ok();
    }

    private async Task<bool> LoginIdentifierInUseAsync(string identifier)
    {
        var normalized = identifier.Trim().ToLower();
        return await _context.Admins.AnyAsync(account => account.Username.ToLower() == normalized) ||
               await _context.Teachers.AnyAsync(account => account.Username.ToLower() == normalized) ||
               await _context.Students.AnyAsync(account =>
                   account.Username.ToLower() == normalized || account.StudentNumber.ToLower() == normalized);
    }

    private async Task<Student> BuildStudentAsync(
        NewStudentInput input,
        Class? entity,
        HashSet<string>? reservedUsernames = null,
        HashSet<string>? reservedStudentNumbers = null)
    {
        var name = NormalizeName(input);
        var username = await CreateUniqueUsernameAsync(input.Username, name.First, name.Last, reservedUsernames);
        var studentNumber = await CreateUniqueStudentNumberAsync(input.StudentNumber, reservedStudentNumbers);

        return new Student
        {
            StudentNumber = studentNumber,
            FirstName = name.First,
            LastName = name.Last,
            FullName = $"{name.First} {name.Last}".Trim(),
            Username = username,
            PasswordHash = _hasher.HashPassword(new object(), input.Password!.Trim()),
            Status = "Active",
            GradeSection = entity?.ClassName ?? string.Empty,
            ClassId = entity?.ClassId,
            AdviserId = entity?.TeacherId
        };
    }

    private async Task<string> CreateUniqueUsernameAsync(
        string? requested,
        string firstName,
        string lastName,
        HashSet<string>? reserved)
    {
        var baseName = NormalizeOptional(requested) ??
                       $"{Slug(firstName)}.{Slug(lastName)}";
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "student";
        }

        var candidate = baseName;
        var suffix = 2;
        while ((reserved?.Contains(candidate) ?? false) ||
               await LoginIdentifierInUseAsync(candidate))
        {
            candidate = $"{baseName}{suffix++}";
        }

        return candidate;
    }

    private async Task<string> CreateUniqueStudentNumberAsync(
        string? requested,
        HashSet<string>? reserved)
    {
        var candidate = NormalizeOptional(requested);
        if (!string.IsNullOrWhiteSpace(candidate) &&
            !(reserved?.Contains(candidate) ?? false) &&
            !await LoginIdentifierInUseAsync(candidate))
        {
            return candidate;
        }

        do
        {
            candidate = $"STU-{DateTime.Now:yyyy}-{RandomNumberGenerator.GetInt32(100000, 999999)}";
        }
        while ((reserved?.Contains(candidate) ?? false) ||
               await LoginIdentifierInUseAsync(candidate));

        return candidate;
    }

    private async Task<ClassOperationResult> InTransactionAsync(Func<Task<ClassOperationResult>> operation)
    {
        IDbContextTransaction? transaction = null;
        if (_context.Database.IsRelational())
        {
            transaction = await _context.Database.BeginTransactionAsync();
        }

        try
        {
            var result = await operation();
            if (transaction != null)
            {
                if (result.Success)
                {
                    await transaction.CommitAsync();
                }
                else
                {
                    await transaction.RollbackAsync();
                }
            }

            return result;
        }
        catch (DbUpdateException)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync();
            }

            return ClassOperationResult.Fail("The roster could not be saved. Check for duplicate student credentials and try again.");
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static (string First, string Last) NormalizeName(NewStudentInput input)
    {
        var first = NormalizeOptional(input.FirstName);
        var last = NormalizeOptional(input.LastName);
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
        {
            var fullName = NormalizeOptional(input.FullName);
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                var parts = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                first ??= parts.ElementAtOrDefault(0);
                last ??= parts.ElementAtOrDefault(1);
            }
        }

        return (first ?? string.Empty, last ?? string.Empty);
    }

    private static string Slug(string value)
    {
        var slug = new string(value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
        return string.IsNullOrWhiteSpace(slug) ? "student" : slug;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeRequired(string? value) =>
        NormalizeOptional(value) ?? string.Empty;
}
