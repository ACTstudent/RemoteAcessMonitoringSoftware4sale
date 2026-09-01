using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Tests.Data;

public class DatabaseInitializerTests
{
    [Fact]
    public void Initialize_AppliesMigrationToFreshSqliteDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = CreateContext(connection);

        DatabaseInitializer.Initialize(db);

        Assert.True(db.Database.GetAppliedMigrations().Any());
        Assert.True(db.Database.CanConnect());
        Assert.Equal(3, db.Roles.Count());
    }

    [Fact]
    public void Initialize_BaselinesExistingEnsureCreatedSqliteDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = CreateContext(connection);
        db.Database.EnsureCreated();

        DatabaseInitializer.Initialize(db);

        Assert.Equal(db.Database.GetMigrations().Count(), db.Database.GetAppliedMigrations().Count());
        Assert.Equal(3, db.Roles.Count());
    }

    [Fact]
    public void Initialize_UpgradesVersion291DatabaseWithoutIndexCollision()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = CreateContext(connection);
        db.Database.GetService<IMigrator>().Migrate("20260831092112_EnforceSessionAndWorkstationIntegrity");

        DatabaseInitializer.Initialize(db);

        Assert.Equal(db.Database.GetMigrations().Count(), db.Database.GetAppliedMigrations().Count());
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_Classes_ClassName_AcademicYear';";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public void Initialize_RepairsLegacyIntegritySchemaBeforeBaseliningMigrations()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = CreateContext(connection);
        db.Database.EnsureCreated();

        db.Database.ExecuteSqlRaw("DROP INDEX IX_LabSessions_ComputerId;");
        db.Database.ExecuteSqlRaw("DROP INDEX IX_LabSessions_StudentId;");
        db.Database.ExecuteSqlRaw("DROP INDEX IX_Computers_AssignedTo;");
        db.Database.ExecuteSqlRaw("DROP INDEX IX_Computers_LaboratoryStation;");

        var student = new Student
        {
            StudentNumber = "LEGACY-1",
            FirstName = "Legacy",
            LastName = "Student",
            Username = "legacy-student",
            PasswordHash = "hash"
        };
        var firstComputer = new Computer { LaboratoryStation = "LAB-01", AssignedTo = "LEGACY-1", Status = "Assigned" };
        var secondComputer = new Computer { LaboratoryStation = "lab-01", AssignedTo = "LEGACY-1", Status = "Assigned" };
        db.AddRange(student, firstComputer, secondComputer);
        db.SaveChanges();
        db.AddRange(
            new LabSession { StudentId = student.Id, ComputerId = firstComputer.ComputerId, PCName = "LAB-01", IsActive = true },
            new LabSession { StudentId = student.Id, ComputerId = secondComputer.ComputerId, PCName = "LAB-02", IsActive = true });
        db.SaveChanges();

        db.ChangeTracker.Clear();
        db.Database.ExecuteSqlRaw("ALTER TABLE LabSessions DROP COLUMN AccumulatedPauseSeconds;");

        DatabaseInitializer.Initialize(db);

        Assert.Equal(db.Database.GetMigrations().Count(), db.Database.GetAppliedMigrations().Count());
        Assert.Equal(1, db.LabSessions.Count(session => session.IsActive));
        Assert.Equal(1, db.Computers.Count(computer => computer.AssignedTo == "LEGACY-1"));
        Assert.Equal(2, db.Computers.Select(computer => computer.LaboratoryStation.ToLower()).Distinct().Count());
        Assert.All(db.LabSessions, session => Assert.Equal(0, session.AccumulatedPauseSeconds));
    }

    [Fact]
    public void Initialize_RepairsAndValidatesLegacyClassColumns()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = CreateContext(connection);
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("DROP INDEX IX_Classes_ClassName_AcademicYear;");
        foreach (var statement in new[]
        {
            "ALTER TABLE Classes DROP COLUMN Section;",
            "ALTER TABLE Classes DROP COLUMN Subject;",
            "ALTER TABLE Classes DROP COLUMN GradeLevel;",
            "ALTER TABLE Classes DROP COLUMN Schedule;",
            "ALTER TABLE Classes DROP COLUMN AcademicYear;",
            "ALTER TABLE Classes DROP COLUMN Status;",
            "ALTER TABLE Classes DROP COLUMN IsArchived;",
            "ALTER TABLE Classes DROP COLUMN CreatedAt;"
        })
            db.Database.ExecuteSqlRaw(statement);

        DatabaseInitializer.Initialize(db);

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(Classes);";
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) columns.Add(reader.GetString(1));
        Assert.All(new[] { "ClassId", "ClassName", "Section", "Subject", "GradeLevel", "Schedule", "AcademicYear", "Status", "IsArchived", "CreatedAt", "TeacherId" },
            column => Assert.Contains(column, columns));
    }

    private static ApplicationDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        return new ApplicationDbContext(options);
    }
}
