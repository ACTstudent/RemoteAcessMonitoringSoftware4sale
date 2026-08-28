using Microsoft.EntityFrameworkCore;
using Server.Data;

namespace Server.Services;

public static class DatabaseInitializer
{
    public static void EnsureCurrentSchema(ApplicationDbContext db)
    {
        if (!db.Database.IsSqlite())
        {
            return;
        }

        RemoveDuplicateMembershipLinks(db);
        EnsureTelemetryTables(db);

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
    }
}
