using Microsoft.EntityFrameworkCore;
using System.Data;
using Server.Data;

namespace Server.Services;

public static class DatabaseInitializer
{
    public static void Initialize(ApplicationDbContext db)
    {
        if (!db.Database.IsRelational())
        {
            return;
        }

        if (db.Database.IsSqlite() && IsLegacySqliteDatabase(db))
        {
            EnsureCurrentSchema(db);
            BaselineLegacySqliteDatabase(db);
        }

        db.Database.Migrate();
    }

    public static void EnsureCurrentSchema(ApplicationDbContext db)
    {
        if (!db.Database.IsSqlite())
        {
            return;
        }

        RemoveDuplicateMembershipLinks(db);
        EnsureTelemetryTables(db);
        EnsureCategoryTables(db);

        var hasDuplicateStudentNumbers = db.Students
            .AsNoTracking()
            .GroupBy(student => student.StudentNumber.ToLower())
            .Any(group => group.Count() > 1);
        var hasDuplicateUsernames = db.Students
            .AsNoTracking()
            .GroupBy(student => student.Username.ToLower())
            .Any(group => group.Count() > 1);

        TryCreateIndex(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_ClassStudents_ClassId_StudentId ON ClassStudents (ClassId, StudentId);");
        if (!hasDuplicateStudentNumbers)
        {
            TryCreateIndex(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_Students_StudentNumber ON Students (StudentNumber);");
        }
        if (!hasDuplicateUsernames)
        {
            TryCreateIndex(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_Students_Username ON Students (Username);");
        }
    }

    private static bool IsLegacySqliteDatabase(ApplicationDbContext db)
    {
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";

        if (command.Connection!.State != ConnectionState.Open)
        {
            command.Connection.Open();
        }

        var userTableCount = Convert.ToInt32(command.ExecuteScalar());
        if (userTableCount == 0)
        {
            return false;
        }

        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'";
        return Convert.ToInt32(command.ExecuteScalar()) == 0;
    }

    private static void BaselineLegacySqliteDatabase(ApplicationDbContext db)
    {
        var migrations = db.Database.GetMigrations().ToList();
        if (migrations.Count == 0)
        {
            throw new InvalidOperationException("No EF Core migration was found for the application database.");
        }

        db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY, ProductVersion TEXT NOT NULL);");
        var productVersion = typeof(DbContext).Assembly.GetName().Version?.ToString(3) ?? "8.0.0";
        foreach (var migration in migrations)
        {
            db.Database.ExecuteSqlInterpolated($"INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ({migration}, {productVersion});");
        }
    }

    private static void RemoveDuplicateMembershipLinks(ApplicationDbContext db)
    {
        var duplicates = db.ClassStudents
            .AsNoTracking()
            .OrderBy(link => link.ClassStudentId)
            .AsEnumerable()
            .GroupBy(link => new { link.ClassId, link.StudentId })
            .SelectMany(group => group.Skip(1))
            .ToList();

        if (duplicates.Count == 0)
        {
            return;
        }

        db.ClassStudents.RemoveRange(duplicates);
        db.SaveChanges();
    }

    private static void TryCreateIndex(ApplicationDbContext db, string statement)
    {
        try
        {
            db.Database.ExecuteSqlRaw(statement);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CAMS] Database index warning: {ex.Message}");
        }
    }

    private static void EnsureTelemetryTables(ApplicationDbContext db)
    {
        TryCreateIndex(db, "CREATE TABLE IF NOT EXISTS IdleIntervals (IdleIntervalId INTEGER NOT NULL CONSTRAINT PK_IdleIntervals PRIMARY KEY AUTOINCREMENT, ConnectionId TEXT NOT NULL, StudentId TEXT NOT NULL, PcName TEXT NOT NULL, StartedAt TEXT NOT NULL, EndedAt TEXT NULL);");
        TryCreateIndex(db, "CREATE TABLE IF NOT EXISTS ActivityEvents (ActivityEventId INTEGER NOT NULL CONSTRAINT PK_ActivityEvents PRIMARY KEY AUTOINCREMENT, ConnectionId TEXT NOT NULL, StudentId TEXT NOT NULL, PcName TEXT NOT NULL, EventType TEXT NOT NULL, ApplicationName TEXT NULL, Details TEXT NULL, Timestamp TEXT NOT NULL);");
        TryCreateIndex(db, "CREATE INDEX IF NOT EXISTS IX_IdleIntervals_ConnectionId_StartedAt ON IdleIntervals (ConnectionId, StartedAt);");
        TryCreateIndex(db, "CREATE INDEX IF NOT EXISTS IX_ActivityEvents_PcName_Timestamp ON ActivityEvents (PcName, Timestamp);");
        TryCreateIndex(db, "CREATE TABLE IF NOT EXISTS MonitoringAlerts (MonitoringAlertId INTEGER NOT NULL CONSTRAINT PK_MonitoringAlerts PRIMARY KEY AUTOINCREMENT, StudentId TEXT NOT NULL, PcName TEXT NOT NULL, Severity TEXT NOT NULL, Title TEXT NOT NULL, Message TEXT NOT NULL, IsAcknowledged INTEGER NOT NULL DEFAULT 0, CreatedAt TEXT NOT NULL);");
        TryCreateIndex(db, "CREATE INDEX IF NOT EXISTS IX_MonitoringAlerts_StudentId_CreatedAt ON MonitoringAlerts (StudentId, CreatedAt);");
        TryCreateIndex(db, "CREATE TABLE IF NOT EXISTS RemoteControlSessions (RemoteControlSessionId INTEGER NOT NULL CONSTRAINT PK_RemoteControlSessions PRIMARY KEY AUTOINCREMENT, TeacherId INTEGER NOT NULL, StudentId TEXT NOT NULL, PcName TEXT NOT NULL, ConnectionId TEXT NOT NULL, StartedAt TEXT NOT NULL, EndedAt TEXT NULL, IsActive INTEGER NOT NULL DEFAULT 1);");
        TryCreateIndex(db, "CREATE TABLE IF NOT EXISTS RemoteCommandLogs (RemoteCommandLogId INTEGER NOT NULL CONSTRAINT PK_RemoteCommandLogs PRIMARY KEY AUTOINCREMENT, RemoteControlSessionId INTEGER NULL, TeacherId INTEGER NOT NULL, Command TEXT NOT NULL, Details TEXT NOT NULL, Timestamp TEXT NOT NULL);");
        TryCreateIndex(db, "CREATE INDEX IF NOT EXISTS IX_RemoteControlSessions_TeacherId_IsActive ON RemoteControlSessions (TeacherId, IsActive);");
        TryCreateIndex(db, "CREATE TABLE IF NOT EXISTS WebsiteUsageLogs (WebsiteUsageLogId INTEGER NOT NULL CONSTRAINT PK_WebsiteUsageLogs PRIMARY KEY AUTOINCREMENT, StudentId INTEGER NULL, Domain TEXT NOT NULL, Browser TEXT NOT NULL, Timestamp TEXT NOT NULL);");
        TryCreateIndex(db, "CREATE INDEX IF NOT EXISTS IX_WebsiteUsageLogs_StudentId_Timestamp ON WebsiteUsageLogs (StudentId, Timestamp);");
        TryCreateIndex(db, "CREATE TABLE IF NOT EXISTS ComputerStatusHistories (ComputerStatusHistoryId INTEGER NOT NULL CONSTRAINT PK_ComputerStatusHistories PRIMARY KEY AUTOINCREMENT, ComputerId INTEGER NOT NULL, Status TEXT NOT NULL, ChangedAt TEXT NOT NULL, ChangedByType TEXT NOT NULL, ChangedById INTEGER NULL);");
        TryCreateIndex(db, "CREATE INDEX IF NOT EXISTS IX_ComputerStatusHistories_ComputerId_ChangedAt ON ComputerStatusHistories (ComputerId, ChangedAt);");
    }

    private static void EnsureCategoryTables(ApplicationDbContext db)
    {
        TryCreateIndex(db, "CREATE TABLE IF NOT EXISTS ApplicationCategories (ApplicationCategoryId INTEGER NOT NULL CONSTRAINT PK_ApplicationCategories PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Pattern TEXT NOT NULL, Description TEXT NOT NULL, Mode TEXT NOT NULL, IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL);");
        TryCreateIndex(db, "CREATE TABLE IF NOT EXISTS WebsiteCategories (WebsiteCategoryId INTEGER NOT NULL CONSTRAINT PK_WebsiteCategories PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, DomainPattern TEXT NOT NULL, Description TEXT NOT NULL, Mode TEXT NOT NULL, IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL);");
    }
}
