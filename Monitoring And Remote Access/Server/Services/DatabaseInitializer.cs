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
        EnsureAccountSecurityColumns(db);
        EnsureMonitoringAlertColumns(db);
        EnsureRemoteCommandColumns(db);
        EnsureBrowserMonitoringTable(db);
        EnsureCategoryTables(db);
        EnsureTeacherRestrictionScope(db);
        EnsureSessionAndWorkstationIntegrity(db);
        EnsureAnalyticsIndexes(db);

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

        ValidateLegacySchema(db);
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

    private static void EnsureAccountSecurityColumns(ApplicationDbContext db)
    {
        EnsureColumn(db, "Admins", "FailedLoginAttempts", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "Admins", "IsActive", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(db, "Admins", "LockoutEndUtc", "TEXT NULL");
        EnsureColumn(db, "Teachers", "FailedLoginAttempts", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "Teachers", "LockoutEndUtc", "TEXT NULL");
        EnsureColumn(db, "Students", "FailedLoginAttempts", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "Students", "LockoutEndUtc", "TEXT NULL");
    }

    private static void EnsureRemoteCommandColumns(ApplicationDbContext db)
    {
        EnsureColumn(db, "RemoteCommandLogs", "PcName", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(db, "RemoteCommandLogs", "StudentId", "TEXT NOT NULL DEFAULT ''");
        TryCreateIndex(db, "CREATE INDEX IF NOT EXISTS IX_RemoteCommandLogs_TeacherId_StudentId_Timestamp ON RemoteCommandLogs (TeacherId, StudentId, Timestamp);");
        TryCreateIndex(db, "CREATE INDEX IF NOT EXISTS IX_MonitoringAlerts_StudentId_DedupeKey_CreatedAt ON MonitoringAlerts (StudentId, DedupeKey, CreatedAt);");
    }

    private static void EnsureTeacherRestrictionScope(ApplicationDbContext db)
    {
        EnsureColumn(db, "RestrictionRules", "TeacherId", "INTEGER NULL");
        TryCreateIndex(db, "CREATE INDEX IF NOT EXISTS IX_RestrictionRules_TeacherId ON RestrictionRules (TeacherId);");
    }

    private static void EnsureSessionAndWorkstationIntegrity(ApplicationDbContext db)
    {
        EnsureColumn(db, "LabSessions", "AccumulatedPauseSeconds", "INTEGER NOT NULL DEFAULT 0");

        db.Database.ExecuteSqlRaw("""
            UPDATE LabSessions
            SET IsActive = 0,
                Status = 'Ended',
                EndTime = COALESCE(EndTime, CURRENT_TIMESTAMP)
            WHERE IsActive = 1
              AND Id NOT IN (
                  SELECT MAX(Id) FROM LabSessions WHERE IsActive = 1 GROUP BY StudentId
              );

            UPDATE LabSessions
            SET IsActive = 0,
                Status = 'Ended',
                EndTime = COALESCE(EndTime, CURRENT_TIMESTAMP)
            WHERE IsActive = 1
              AND ComputerId IS NOT NULL
              AND Id NOT IN (
                  SELECT MAX(Id) FROM LabSessions
                  WHERE IsActive = 1 AND ComputerId IS NOT NULL
                  GROUP BY ComputerId
              );

            UPDATE Computers
            SET AssignedTo = NULL,
                Status = CASE WHEN Status = 'Assigned' THEN 'Available' ELSE Status END
            WHERE AssignedTo IS NOT NULL
              AND ComputerId NOT IN (
                  SELECT MIN(ComputerId) FROM Computers
                  WHERE AssignedTo IS NOT NULL
                  GROUP BY AssignedTo
              );

            UPDATE Computers AS duplicate
            SET LaboratoryStation = duplicate.LaboratoryStation || '-' || duplicate.ComputerId
            WHERE EXISTS (
                SELECT 1 FROM Computers AS original
                WHERE original.ComputerId < duplicate.ComputerId
                  AND LOWER(original.LaboratoryStation) = LOWER(duplicate.LaboratoryStation)
            );
            """);

        db.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS IX_LabSessions_ComputerId;");
        db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_LabSessions_ComputerId ON LabSessions (ComputerId) WHERE IsActive = 1 AND ComputerId IS NOT NULL;");
        db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_LabSessions_StudentId ON LabSessions (StudentId) WHERE IsActive = 1;");
        db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_Computers_AssignedTo ON Computers (AssignedTo) WHERE AssignedTo IS NOT NULL;");
        db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_Computers_LaboratoryStation ON Computers (LaboratoryStation COLLATE NOCASE);");
    }

    private static void EnsureAnalyticsIndexes(ApplicationDbContext db)
    {
        TryCreateIndex(db, "CREATE INDEX IF NOT EXISTS IX_LabSessions_StudentId_StartTime ON LabSessions (StudentId, StartTime);");
        TryCreateIndex(db, "CREATE INDEX IF NOT EXISTS IX_IdleIntervals_StudentId_StartedAt ON IdleIntervals (StudentId, StartedAt);");
        TryCreateIndex(db, "CREATE INDEX IF NOT EXISTS IX_ActivityEvents_StudentId_Timestamp ON ActivityEvents (StudentId, Timestamp);");
    }

    private static void ValidateLegacySchema(ApplicationDbContext db)
    {
        var requiredColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Admins"] = ["FailedLoginAttempts", "IsActive", "LockoutEndUtc"],
            ["Teachers"] = ["FailedLoginAttempts", "LockoutEndUtc"],
            ["Students"] = ["FailedLoginAttempts", "LockoutEndUtc"],
            ["LabSessions"] = ["AccumulatedPauseSeconds"],
            ["RemoteCommandLogs"] = ["PcName", "StudentId"],
            ["MonitoringAlerts"] = ["DedupeKey", "GroupKey", "OccurrenceCount"],
            ["RestrictionRules"] = ["TeacherId"]
        };

        foreach (var (table, columns) in requiredColumns)
        {
            var actual = ReadSchemaNames(db, $"PRAGMA table_info(\"{table}\");", 1);
            var missing = columns.Where(column => !actual.Contains(column)).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"Legacy CAMS database upgrade is incomplete. {table} is missing: {string.Join(", ", missing)}.");
        }

        var requiredIndexes = new[]
        {
            "IX_LabSessions_ComputerId",
            "IX_LabSessions_StudentId",
            "IX_Computers_AssignedTo",
            "IX_Computers_LaboratoryStation"
        };
        var indexes = ReadSchemaNames(db, "SELECT name FROM sqlite_master WHERE type = 'index';", 0);
        var missingIndexes = requiredIndexes.Where(index => !indexes.Contains(index)).ToArray();
        if (missingIndexes.Length > 0)
            throw new InvalidOperationException($"Legacy CAMS database upgrade is incomplete. Missing indexes: {string.Join(", ", missingIndexes)}.");
    }

    private static HashSet<string> ReadSchemaNames(ApplicationDbContext db, string sql, int ordinal)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        if (command.Connection!.State != ConnectionState.Open)
            command.Connection.Open();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(ordinal));
        return names;
    }

    private static void EnsureMonitoringAlertColumns(ApplicationDbContext db)
    {
        // Legacy databases were created without the alert-lifecycle columns, but they are
        // baselined as fully migrated. Materialize the missing columns and backfill them so
        // the alert lifecycle and analytics queries match the current model.
        EnsureColumn(db, "MonitoringAlerts", "DedupeKey", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(db, "MonitoringAlerts", "AcknowledgedAt", "TEXT NULL");
        EnsureColumn(db, "MonitoringAlerts", "AcknowledgedByTeacherId", "INTEGER NULL");
        EnsureColumn(db, "MonitoringAlerts", "DismissalReason", "TEXT NULL");
        EnsureColumn(db, "MonitoringAlerts", "DismissedAt", "TEXT NULL");
        EnsureColumn(db, "MonitoringAlerts", "DismissedByTeacherId", "INTEGER NULL");
        EnsureColumn(db, "MonitoringAlerts", "FirstSeenAt", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'");
        EnsureColumn(db, "MonitoringAlerts", "GroupKey", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(db, "MonitoringAlerts", "LastSeenAt", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'");
        EnsureColumn(db, "MonitoringAlerts", "OccurrenceCount", "INTEGER NOT NULL DEFAULT 1");

        TryExecute(db,
            "UPDATE MonitoringAlerts SET FirstSeenAt = CreatedAt, LastSeenAt = CreatedAt, OccurrenceCount = CASE WHEN OccurrenceCount < 1 THEN 1 ELSE OccurrenceCount END, GroupKey = lower(trim(StudentId)) || '|' || lower(trim(PcName)) || '|' || lower(trim(CASE WHEN DedupeKey = '' THEN Title ELSE DedupeKey END));");

        TryCreateIndex(db, "CREATE INDEX IF NOT EXISTS IX_MonitoringAlerts_StudentId_GroupKey_LastSeenAt ON MonitoringAlerts (StudentId, GroupKey, LastSeenAt);");
    }

    private static void EnsureBrowserMonitoringTable(ApplicationDbContext db)
    {
        TryCreateIndex(db, "CREATE TABLE IF NOT EXISTS BrowserMonitoringRecords (BrowserMonitoringRecordId INTEGER NOT NULL CONSTRAINT PK_BrowserMonitoringRecords PRIMARY KEY AUTOINCREMENT, ConnectionId TEXT NOT NULL, StudentId TEXT NOT NULL, PcName TEXT NOT NULL, Browser TEXT NOT NULL, Mode INTEGER NOT NULL, Detail TEXT NULL, Timestamp TEXT NOT NULL);");
        TryCreateIndex(db, "CREATE INDEX IF NOT EXISTS IX_BrowserMonitoringRecords_PcName_Timestamp ON BrowserMonitoringRecords (PcName, Timestamp);");
        TryCreateIndex(db, "CREATE INDEX IF NOT EXISTS IX_BrowserMonitoringRecords_StudentId_Timestamp ON BrowserMonitoringRecords (StudentId, Timestamp);");
    }

    private static void EnsureColumn(ApplicationDbContext db, string table, string column, string definition)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        if (command.Connection!.State != ConnectionState.Open)
            command.Connection.Open();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(1));

        if (columns.Contains(column))
            return;

        TryExecute(db, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }

    private static void TryExecute(ApplicationDbContext db, string statement)
    {
        try
        {
            db.Database.ExecuteSqlRaw(statement);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CAMS] Legacy schema statement warning: {ex.Message}");
        }
    }
}
