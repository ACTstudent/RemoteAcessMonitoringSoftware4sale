using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Tests.Services;

public class ClassManagementServiceTests
{
    private static ApplicationDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CreateClass_RequiresActiveTeacher()
    {
        using var db = GetDbContext();
        var inactiveTeacher = new Teacher
        {
            FirstName = "Inactive",
            LastName = "Teacher",
            Username = "inactive",
            PasswordHash = "hash",
            Status = "Inactive"
        };
        db.Teachers.Add(inactiveTeacher);
        await db.SaveChangesAsync();
        var service = new ClassManagementService(db);

        var result = await service.CreateClassAsync(
            new ClassInput("Grade 6 - Rose", "Rose", "Computer", "Grade 6", "Monday", "2026-2027", inactiveTeacher.TeacherId),
            null,
            isAdmin: true);

        Assert.False(result.Success);
        Assert.Contains("active teacher", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.Classes.ToListAsync());
    }

    [Fact]
    public async Task AdminCanCreateClassWithoutTeacher()
    {
        using var db = GetDbContext();
        var result = await new ClassManagementService(db).CreateClassAsync(
            new ClassInput("Grade 6 Test", "A", "Computer", "Grade 6", "Monday", null, null),
            actorTeacherId: null,
            isAdmin: true);

        var classroom = await db.Classes.SingleAsync();
        Assert.True(result.Success);
        Assert.Null(classroom.TeacherId);
        Assert.Equal("2026-2027", classroom.AcademicYear);
    }

    [Fact]
    public async Task TeacherCannotMutateAnotherTeachersClass()
    {
        using var db = GetDbContext();
        var firstTeacher = new Teacher { FirstName = "First", LastName = "Teacher", Username = "first", PasswordHash = "hash", Status = "Active" };
        var secondTeacher = new Teacher { FirstName = "Second", LastName = "Teacher", Username = "second", PasswordHash = "hash", Status = "Active" };
        db.Teachers.AddRange(firstTeacher, secondTeacher);
        await db.SaveChangesAsync();
        var cls = new Class { ClassName = "Grade 7 - A", TeacherId = secondTeacher.TeacherId };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        var service = new ClassManagementService(db);

        var update = await service.UpdateClassAsync(
            cls.ClassId,
            new ClassInput("Changed", "A", "Computer", "Grade 7", "Tuesday", "2026-2027", firstTeacher.TeacherId),
            firstTeacher.TeacherId,
            isAdmin: false);
        var delete = await service.DeleteClassAsync(cls.ClassId, firstTeacher.TeacherId);

        Assert.False(update.Success);
        Assert.False(delete.Success);
        Assert.Equal("Grade 7 - A", (await db.Classes.FindAsync(cls.ClassId))?.ClassName);
    }

    [Fact]
    public async Task MovingStudentRequiresConfirmationAndLeavesOneActiveMembership()
    {
        using var db = GetDbContext();
        var teacher = new Teacher { FirstName = "Maria", LastName = "Santos", Username = "msantos", PasswordHash = "hash", Status = "Active" };
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync();
        var firstClass = new Class { ClassName = "Grade 8 - A", TeacherId = teacher.TeacherId };
        var secondClass = new Class { ClassName = "Grade 8 - B", TeacherId = teacher.TeacherId };
        var student = new Student { StudentNumber = "STU-001", FirstName = "Juan", LastName = "Luna", Username = "jluna", PasswordHash = "hash", ClassId = firstClass.ClassId };
        db.Classes.AddRange(firstClass, secondClass);
        await db.SaveChangesAsync();
        student.ClassId = firstClass.ClassId;
        db.Students.Add(student);
        db.ClassStudents.Add(new ClassStudent { ClassId = firstClass.ClassId, StudentId = student.Id });
        await db.SaveChangesAsync();
        var service = new ClassManagementService(db);

        var withoutConfirmation = await service.EnrollExistingStudentAsync(secondClass.ClassId, student.Id, moveStudent: false, teacher.TeacherId);
        var withConfirmation = await service.EnrollExistingStudentAsync(secondClass.ClassId, student.Id, moveStudent: true, teacher.TeacherId);

        Assert.False(withoutConfirmation.Success);
        Assert.True(withConfirmation.Success);
        Assert.Equal(secondClass.ClassId, (await db.Students.FindAsync(student.Id))?.ClassId);
        Assert.Equal(secondClass.ClassId, Assert.Single(await db.ClassStudents.Where(cs => cs.StudentId == student.Id).ToListAsync()).ClassId);
    }

    [Fact]
    public async Task BulkCreateIsAtomicWhenOneRowIsInvalid()
    {
        using var db = GetDbContext();
        var teacher = new Teacher { FirstName = "Maria", LastName = "Santos", Username = "msantos", PasswordHash = "hash", Status = "Active" };
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync();
        var cls = new Class { ClassName = "Grade 9 - A", TeacherId = teacher.TeacherId };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        var service = new ClassManagementService(db);

        var result = await service.BulkCreateStudentsInClassAsync(cls.ClassId, new[]
        {
            new NewStudentInput(null, "Valid", "Student", "", "valid.student", "good123"),
            new NewStudentInput(null, "Missing", "", "", "missing.last", "good123")
        }, teacher.TeacherId);

        Assert.False(result.Success);
        Assert.Empty(await db.Students.ToListAsync());
        Assert.Empty(await db.ClassStudents.ToListAsync());
    }

    [Fact]
    public async Task BulkCreateStudents_CreatesUnassignedProfiles()
    {
        using var db = GetDbContext();
        var service = new ClassManagementService(db);

        var result = await service.BulkCreateStudentsAsync(new[]
        {
            new NewStudentInput(null, "Ana", "Reyes", null, "ana.reyes", "secret1"),
            new NewStudentInput(null, "Ben", "Cruz", null, null, "secret2")
        });

        var students = await db.Students.OrderBy(student => student.FirstName).ToListAsync();
        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        Assert.All(students, student =>
        {
            Assert.Null(student.ClassId);
            Assert.Null(student.AdviserId);
            Assert.Empty(student.GradeSection);
        });
        Assert.Empty(await db.ClassStudents.ToListAsync());
    }

    [Fact]
    public async Task BulkCreateStudents_IsAtomicWhenOneProfileIsInvalid()
    {
        using var db = GetDbContext();
        var service = new ClassManagementService(db);

        var result = await service.BulkCreateStudentsAsync(new[]
        {
            new NewStudentInput(null, "Valid", "Student", null, "valid.student", "secret1"),
            new NewStudentInput(null, "Missing", "", null, "invalid.student", "secret2")
        });

        Assert.False(result.Success);
        Assert.Empty(await db.Students.ToListAsync());
    }

    [Fact]
    public void CsvParserSupportsQuotedValuesAndHeader()
    {
        using var db = GetDbContext();
        var service = new ClassManagementService(db);

        var preview = service.ParseBulkStudentsCsv("Student Number,First Name,Last Name,Full Name,Username,Password\nSTU-1,Jane,\"Van Doe\",,jane,secret1");

        var row = Assert.Single(preview.Rows);
        Assert.Equal("Van Doe", row.Input.LastName);
        Assert.Equal("jane", row.Input.Username);
    }

    [Fact]
    public async Task BulkPreviewReportsAllInvalidRowsWithoutCreatingAccounts()
    {
        using var db = GetDbContext();
        var teacher = new Teacher { FirstName = "Maria", LastName = "Santos", Username = "preview", PasswordHash = "hash", Status = "Active" };
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync();
        var cls = new Class { ClassName = "Preview", TeacherId = teacher.TeacherId };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        var service = new ClassManagementService(db);

        var preview = await service.PreviewBulkStudentsAsync(cls.ClassId, new[] {
            new NewStudentInput(null, "Valid", "Student", null, "same", "secret1"),
            new NewStudentInput(null, "", "", null, "same", "short")
        }, teacher.TeacherId);

        Assert.Equal(2, preview.Rows.Count);
        Assert.Equal(1, preview.ErrorCount);
        Assert.Empty(await db.Students.ToListAsync());
    }

    [Fact]
    public async Task DeleteClassPreservesStudentAccountAndClearsAssignment()
    {
        using var db = GetDbContext();
        var teacher = new Teacher { FirstName = "Maria", LastName = "Santos", Username = "msantos", PasswordHash = "hash", Status = "Active" };
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync();
        var cls = new Class { ClassName = "Grade 10 - A", TeacherId = teacher.TeacherId };
        db.Classes.Add(cls);
        await db.SaveChangesAsync();
        var student = new Student
        {
            StudentNumber = "STU-010",
            FirstName = "Maria",
            LastName = "Clara",
            Username = "mclara",
            PasswordHash = "hash",
            ClassId = cls.ClassId,
            AdviserId = teacher.TeacherId,
            GradeSection = cls.ClassName
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        db.ClassStudents.Add(new ClassStudent { ClassId = cls.ClassId, StudentId = student.Id });
        await db.SaveChangesAsync();
        var service = new ClassManagementService(db);

        var result = await service.DeleteClassAsync(cls.ClassId);

        var preserved = await db.Students.FindAsync(student.Id);
        Assert.True(result.Success);
        Assert.NotNull(preserved);
        Assert.Null(preserved!.ClassId);
        Assert.Null(preserved.AdviserId);
        Assert.Empty(preserved.GradeSection);
        Assert.Empty(await db.ClassStudents.ToListAsync());
        Assert.Null(await db.Classes.FindAsync(cls.ClassId));
    }

    [Fact]
    public async Task UpdateAndArchiveSynchronizeStudentTeacherVisibility()
    {
        using var db = GetDbContext();
        var first = new Teacher { Username = "first-teacher", PasswordHash = "hash", Status = "Active" };
        var second = new Teacher { Username = "second-teacher", PasswordHash = "hash", Status = "Active" };
        db.Teachers.AddRange(first, second);
        await db.SaveChangesAsync();
        var classroom = new Class { ClassName = "Old Name", TeacherId = first.TeacherId };
        db.Classes.Add(classroom);
        await db.SaveChangesAsync();
        var student = new Student { StudentNumber = "SYNC-1", Username = "sync-1", PasswordHash = "hash", ClassId = classroom.ClassId, AdviserId = first.TeacherId, GradeSection = classroom.ClassName };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var service = new ClassManagementService(db);

        var update = await service.UpdateClassAsync(classroom.ClassId,
            new ClassInput("New Name", "A", "Computer", "Grade 6", "Monday", "2026-2027", second.TeacherId), null, true);
        Assert.True(update.Success);
        Assert.Equal("New Name", student.GradeSection);
        Assert.Equal(second.TeacherId, student.AdviserId);

        Assert.True((await service.SetArchiveStateAsync(classroom.ClassId, true)).Success);
        Assert.Null(student.AdviserId);
        Assert.True((await service.UpdateClassAsync(classroom.ClassId,
            new ClassInput("Archived Name", "A", "Computer", "Grade 6", "Tuesday", "2026-2027", first.TeacherId), null, true)).Success);
        Assert.Null(student.AdviserId);
        Assert.True((await service.AssignTeacherAsync(classroom.ClassId, second.TeacherId)).Success);
        Assert.Null(student.AdviserId);
        Assert.True((await service.SetArchiveStateAsync(classroom.ClassId, false)).Success);
        Assert.Equal(second.TeacherId, student.AdviserId);
    }
}
