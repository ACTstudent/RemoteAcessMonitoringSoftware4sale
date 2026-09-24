using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;
using Shared.Contracts;

namespace Server.Tests.Services;

// A student removed on Student Profiles is archived: kept for their history,
// but out of use. These pin the "out of use" half.
public class StudentArchiveTests
{
    private static ApplicationDbContext GetDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task ArchivedStudent_CannotBeEnrolledInAClass()
    {
        using var db = GetDbContext();
        var cls = new Class { ClassName = "Grade 6", TeacherId = 1 };
        var student = new Student { StudentNumber = "S-ARC", Username = "archived", PasswordHash = "hash", Status = RecordStatus.Archived };
        db.AddRange(cls, student);
        await db.SaveChangesAsync();

        var result = await new ClassManagementService(db).EnrollExistingStudentAsync(cls.ClassId, student.Id, moveStudent: false);

        Assert.False(result.Success);
        Assert.Empty(await db.ClassStudents.ToListAsync());
    }

    [Fact]
    public async Task ArchivedStudent_CannotSignIn()
    {
        using var db = GetDbContext();
        var student = new Student
        {
            StudentNumber = "S-ARC2",
            Username = "archived2",
            PasswordHash = new PasswordHasher<object>().HashPassword(new object(), "Passw0rd!"),
            Status = RecordStatus.Archived
        };
        db.Add(student);
        await db.SaveChangesAsync();

        var result = await new AuthenticationService(db).LoginAsync("archived2", "Passw0rd!", string.Empty, "127.0.0.1");

        Assert.NotEqual(AccountRole.Student, result.Role);
    }

    [Fact]
    public async Task ArchiveStudent_LeavesEveryActiveClassAndKeepsTheAccount()
    {
        using var db = GetDbContext();
        var first = new Class { ClassName = "Grade 6 A", TeacherId = 1 };
        var second = new Class { ClassName = "Grade 6 B", TeacherId = 2 };
        var lastYear = new Class { ClassName = "Grade 5 A", TeacherId = 1, IsArchived = true };
        db.AddRange(first, second, lastYear);
        await db.SaveChangesAsync();
        var student = new Student { StudentNumber = "S-TWO", Username = "twoclasses", PasswordHash = "hash", Status = "Active", ClassId = first.ClassId, AdviserId = 1 };
        db.Add(student);
        await db.SaveChangesAsync();
        db.ClassStudents.AddRange(
            new ClassStudent { ClassId = first.ClassId, StudentId = student.Id },
            new ClassStudent { ClassId = second.ClassId, StudentId = student.Id },
            new ClassStudent { ClassId = lastYear.ClassId, StudentId = student.Id });
        await db.SaveChangesAsync();

        var result = await new ClassManagementService(db).ArchiveStudentAsync(student.Id);

        Assert.True(result.Success);
        // A place in an archived class is history, and reports refer to it.
        var remaining = Assert.Single(await db.ClassStudents.ToListAsync());
        Assert.Equal(lastYear.ClassId, remaining.ClassId);
        var kept = await db.Students.SingleAsync();
        Assert.Equal(RecordStatus.Archived, kept.Status);
        Assert.Null(kept.ClassId);
        Assert.Null(kept.AdviserId);
    }
}
