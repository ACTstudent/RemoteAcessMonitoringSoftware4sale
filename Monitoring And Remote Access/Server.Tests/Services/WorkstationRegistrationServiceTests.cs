using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Tests.Services;

public class WorkstationRegistrationServiceTests
{
    [Fact]
    public async Task GetOrCreate_NewStation_CreatesAndAuditsProfile()
    {
        await using var db = CreateContext();
        db.Students.Add(Student(1));
        await db.SaveChangesAsync();

        var computer = await new WorkstationRegistrationService(db)
            .GetOrCreateForStudentAsync(1, "LAB2-PC26");

        Assert.Equal("LAB2-PC26", computer.LaboratoryStation);
        Assert.Equal("1", computer.AssignedTo);
        Assert.Equal("In Use", computer.Status);
        Assert.Single(db.Computers);
        Assert.Contains(db.AuditLogs, log => log.Action == "WorkstationAutoCreated");
    }

    [Fact]
    public async Task GetOrCreate_UnassignedStation_ReusesProfile()
    {
        await using var db = CreateContext();
        db.Students.Add(Student(1));
        db.Computers.Add(new Computer { LaboratoryStation = "LAB2-PC26", Status = "Available" });
        await db.SaveChangesAsync();

        var computer = await new WorkstationRegistrationService(db)
            .GetOrCreateForStudentAsync(1, "lab2-pc26");

        Assert.Single(db.Computers);
        Assert.Equal("1", computer.AssignedTo);
        Assert.Contains(db.AuditLogs, log => log.Action == "WorkstationAutoAssigned");
    }

    [Fact]
    public async Task GetOrCreate_NewStation_ReleasesPreviousSafeAssignment()
    {
        await using var db = CreateContext();
        db.Students.Add(Student(1));
        db.Computers.Add(new Computer { LaboratoryStation = "LAB2-PC26", AssignedTo = "1", Status = "Assigned" });
        await db.SaveChangesAsync();

        var computer = await new WorkstationRegistrationService(db)
            .GetOrCreateForStudentAsync(1, "LAB2-PC27");

        var old = await db.Computers.SingleAsync(item => item.LaboratoryStation == "LAB2-PC26");
        Assert.Null(old.AssignedTo);
        Assert.Equal("Available", old.Status);
        Assert.Equal("1", computer.AssignedTo);
        Assert.Contains(db.AuditLogs, log => log.Action == "WorkstationAutoCreated");
        Assert.Contains(db.AuditLogs, log => log.Action == "WorkstationAutoMoved");
    }

    [Fact]
    public async Task GetOrCreate_OccupiedStation_RejectsLoginWithoutStealingIt()
    {
        await using var db = CreateContext();
        db.Students.AddRange(Student(1), Student(2));
        var computer = new Computer { LaboratoryStation = "LAB2-PC27", AssignedTo = "2", Status = "In Use" };
        db.Computers.Add(computer);
        db.LabSessions.Add(new LabSession { StudentId = 2, Computer = computer, PCName = "LAB2-PC27", IsActive = true, Status = "Running" });
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WorkstationRegistrationService(db).GetOrCreateForStudentAsync(1, "LAB2-PC27"));

        Assert.Contains("another student", error.Message);
        Assert.Equal("2", computer.AssignedTo);
        Assert.Contains(db.AuditLogs, log => log.Action == "WorkstationLoginRejected");
    }

    [Fact]
    public async Task GetOrCreate_StudentAlreadyActiveElsewhere_RejectsSecondPc()
    {
        await using var db = CreateContext();
        db.Students.Add(Student(1));
        var computer = new Computer { LaboratoryStation = "LAB2-PC26", AssignedTo = "1", Status = "In Use" };
        db.Computers.Add(computer);
        db.LabSessions.Add(new LabSession { StudentId = 1, Computer = computer, PCName = "LAB2-PC26", IsActive = true, Status = "Running" });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WorkstationRegistrationService(db).GetOrCreateForStudentAsync(1, "LAB2-PC27"));

        Assert.Single(db.Computers);
        Assert.Equal("1", computer.AssignedTo);
    }

    [Fact]
    public async Task EnsureSession_ConcurrentClaims_LeaveOneConsistentOwnerInSqlite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cams-workstation-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False").Options;
        try
        {
            await using (var setup = new ApplicationDbContext(options))
            {
                DatabaseInitializer.Initialize(setup);
                setup.Students.AddRange(Student(1), Student(2));
                await setup.SaveChangesAsync();
            }

            await using var firstDb = new ApplicationDbContext(options);
            await using var secondDb = new ApplicationDbContext(options);
            var attempts = await Task.WhenAll(
                TryEnsureSessionAsync(firstDb, 1),
                TryEnsureSessionAsync(secondDb, 2));

            Assert.Single(attempts, success => success);
            await using var verify = new ApplicationDbContext(options);
            var session = await verify.LabSessions.SingleAsync(item => item.IsActive);
            var computer = await verify.Computers.SingleAsync(item => item.LaboratoryStation == "LAB-RACE");
            Assert.Equal(session.StudentId.ToString(), computer.AssignedTo);
            Assert.Equal(computer.ComputerId, session.ComputerId);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ApplicationDbContext(options);
    }

    private static Student Student(int id) => new()
    {
        Id = id,
        StudentNumber = $"STU-{id}",
        Username = $"student-{id}",
        PasswordHash = "hash",
        Status = "Active"
    };

    private static async Task<bool> TryEnsureSessionAsync(ApplicationDbContext db, int studentId)
    {
        try
        {
            await new WorkstationRegistrationService(db)
                .EnsureStudentSessionAsync(studentId, "LAB-RACE", "127.0.0.1");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
