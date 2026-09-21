using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Tests.Services;

// Who created an account or a class used to leave no trace in the row itself:
// only an audit line said it, and nothing tied the two together. These pin the
// columns that replaced that.
public class RecordCreatorTests
{
    private static ApplicationDbContext GetDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<Teacher> AddActiveTeacherAsync(ApplicationDbContext db)
    {
        var teacher = new Teacher
        {
            FirstName = "Active",
            LastName = "Teacher",
            Username = "active.teacher",
            PasswordHash = "hash",
            Status = "Active"
        };
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync();
        return teacher;
    }

    private static ClassInput ClassNamed(string name, int? teacherId) =>
        new(name, "A", "Computer", "Grade 6", "Monday", "2026-2027", teacherId);

    [Fact]
    public async Task ARecordStartsAsSystemUntilSomebodyCreatesIt()
    {
        Assert.Equal(ActorTypes.System, new Student().CreatedByType);
        Assert.Equal(ActorTypes.System, new Teacher().CreatedByType);
        Assert.Equal(ActorTypes.System, new Class().CreatedByType);
        Assert.Null(new Student().CreatedById);
    }

    [Fact]
    public async Task AClassRecordsTheAdministratorWhoCreatedIt()
    {
        using var db = GetDbContext();
        var teacher = await AddActiveTeacherAsync(db);

        var result = await new ClassManagementService(db).CreateClassAsync(
            ClassNamed("Grade 6 - Rose", teacher.TeacherId), actorTeacherId: null, isAdmin: true,
            actor: RecordActor.Admin(7));

        Assert.True(result.Success);
        var created = await db.Classes.SingleAsync();
        Assert.Equal(ActorTypes.Admin, created.CreatedByType);
        Assert.Equal(7, created.CreatedById);
    }

    [Fact]
    public async Task AClassRecordsTheTeacherWhoCreatedIt()
    {
        using var db = GetDbContext();
        var teacher = await AddActiveTeacherAsync(db);

        var result = await new ClassManagementService(db).CreateClassAsync(
            ClassNamed("Grade 6 - Lily", teacher.TeacherId), teacher.TeacherId, isAdmin: false,
            actor: RecordActor.Teacher(teacher.TeacherId));

        Assert.True(result.Success);
        var created = await db.Classes.SingleAsync();
        Assert.Equal(ActorTypes.Teacher, created.CreatedByType);
        Assert.Equal(teacher.TeacherId, created.CreatedById);
    }

    [Fact]
    public async Task AStudentCreatedInAClassRecordsTheActor()
    {
        using var db = GetDbContext();
        var teacher = await AddActiveTeacherAsync(db);
        var service = new ClassManagementService(db);
        Assert.True((await service.CreateClassAsync(
            ClassNamed("Grade 6 - Ivy", teacher.TeacherId), teacher.TeacherId, isAdmin: false,
            actor: RecordActor.Teacher(teacher.TeacherId))).Success);
        var cls = await db.Classes.SingleAsync();

        var result = await service.CreateStudentInClassAsync(
            cls.ClassId,
            new NewStudentInput(null, "Ana", "Reyes", null, "ana.reyes", "secret12"),
            RecordActor.Teacher(teacher.TeacherId),
            teacher.TeacherId);

        Assert.True(result.Success);
        var student = await db.Students.SingleAsync();
        Assert.Equal(ActorTypes.Teacher, student.CreatedByType);
        Assert.Equal(teacher.TeacherId, student.CreatedById);
    }

    [Fact]
    public async Task EveryStudentFromABulkCreateCarriesTheSameActor()
    {
        using var db = GetDbContext();

        var result = await new ClassManagementService(db).BulkCreateStudentsAsync(new[]
        {
            new NewStudentInput(null, "Ana", "Reyes", null, "ana.reyes", "secret12"),
            new NewStudentInput(null, "Ben", "Cruz", null, "ben.cruz", "secret23")
        }, RecordActor.Admin(3));

        Assert.True(result.Success);
        var students = await db.Students.ToListAsync();
        Assert.Equal(2, students.Count);
        Assert.All(students, student =>
        {
            Assert.Equal(ActorTypes.Admin, student.CreatedByType);
            Assert.Equal(3, student.CreatedById);
        });
    }

    // The id alone is ambiguous: administrator 3 and teacher 3 are different
    // people in different tables, which is why the type travels with it.
    [Fact]
    public void TheTypeIsWhatTellsTwoAccountsWithTheSameIdApart()
    {
        Assert.NotEqual(RecordActor.Admin(3), RecordActor.Teacher(3));
        Assert.Equal(ActorTypes.Admin, RecordActor.Admin(3).Type);
        Assert.Equal(ActorTypes.Teacher, RecordActor.Teacher(3).Type);
    }
}
