using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Data;
using Server.Models;
using Server.Services;

namespace Server.Tests.Services;

public sealed class DatabaseMaintenanceServiceTests
{
    [Fact]
    public async Task CreateBackup_SanitizesLabelAndReturnsHealthyManagedBackup()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(root, "CAMS.db");
            await using var db = await CreateDatabaseAsync(databasePath);
            var service = CreateService(db, root);

            var backup = await service.CreateBackupAsync(" ../Term 1\\Final ??? ");
            var backupPath = Path.Combine(root, "CAMS Backups", backup.FileName);

            Assert.True(File.Exists(backupPath));
            Assert.Equal("Term-1-Final", backup.Label);
            Assert.Matches(
                "^CAMS_\\d{8}T\\d{9}Z_Term-1-Final_[a-f0-9]{8}\\.db$",
                backup.FileName);
            Assert.DoesNotContain("..", backup.FileName, StringComparison.Ordinal);
            Assert.DoesNotContain(Path.DirectorySeparatorChar, backup.FileName);

            var validation = await service.ValidateBackupAsync(backup.FileName);
            Assert.True(validation.IsValid);
            Assert.Equal(new[] { "ok" }, validation.Results);

            var overview = await service.GetOverviewAsync();
            Assert.True(overview.Health.Integrity.IsHealthy);
            Assert.Equal(Path.GetFullPath(databasePath), overview.Health.DatabasePath);
            Assert.Equal(backup.FileName, overview.Health.LatestBackup?.FileName);
            Assert.Single(overview.Backups);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateBackup_RejectsTraversalAndDetectsCorruption()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(root, "CAMS.db");
            await using var db = await CreateDatabaseAsync(databasePath);
            var service = CreateService(db, root);

            await Assert.ThrowsAsync<ArgumentException>(
                () => service.ValidateBackupAsync("..\\CAMS_20260830T120000000Z_bad_12345678.db"));

            var backup = await service.CreateBackupAsync("corrupt-me");
            var backupPath = Path.Combine(root, "CAMS Backups", backup.FileName);
            await File.WriteAllBytesAsync(backupPath, "not a sqlite database"u8.ToArray());

            var validation = await service.ValidateBackupAsync(backup.FileName);
            Assert.False(validation.IsValid);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.StageRestoreAsync(
                    backup.FileName,
                    new DatabaseRestoreActor(1, "127.0.0.1")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StagedRestore_IsRevalidatedAndAtomicallyAppliedAtStartup()
    {
        var root = CreateTemporaryDirectory();
        var databasePath = Path.Combine(root, "CAMS.db");
        string selectedBackup;

        try
        {
            await using (var db = await CreateDatabaseAsync(databasePath))
            {
                db.AuditLogs.Add(CreateAudit("PresentInSelectedBackup"));
                await db.SaveChangesAsync();

                var service = CreateService(db, root);
                selectedBackup = (await service.CreateBackupAsync("restore-point")).FileName;

                db.AuditLogs.Add(CreateAudit("AddedAfterSelectedBackup"));
                await db.SaveChangesAsync();

                var staged = await service.StageRestoreAsync(
                    selectedBackup,
                    new DatabaseRestoreActor(42, "127.0.0.1"));
                Assert.True(staged.RestartRequired);
                Assert.True(File.Exists(databasePath + ".restore-pending.json"));

                var overview = await service.GetOverviewAsync();
                Assert.True(overview.PendingRestore?.IsReady);
                Assert.Equal(selectedBackup, overview.PendingRestore?.BackupFileName);
            }

            SqliteConnection.ClearAllPools();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        $"Data Source={databasePath};Pooling=False"
                })
                .Build();

            var startupResult = DatabaseRestoreStartup.ApplyPendingRestore(
                configuration,
                root);

            Assert.True(startupResult.PendingRestoreFound);
            Assert.True(startupResult.Applied, startupResult.Message);
            Assert.Equal(selectedBackup, startupResult.BackupFileName);
            Assert.False(File.Exists(databasePath + ".restore-pending.json"));

            await using var restoredDb = CreateContext(databasePath);
            var actions = await restoredDb.AuditLogs
                .OrderBy(audit => audit.AuditLogId)
                .Select(audit => audit.Action)
                .ToListAsync();
            Assert.Contains("PresentInSelectedBackup", actions);
            Assert.DoesNotContain("AddedAfterSelectedBackup", actions);
            Assert.Contains("DatabaseRestoreApplied", actions);

            var restoreAudit = await restoredDb.AuditLogs
                .SingleAsync(audit => audit.Action == "DatabaseRestoreApplied");
            Assert.Equal(42, restoreAudit.UserId);
            Assert.Equal("127.0.0.1", restoreAudit.IpAddress);

            var safetyBackups = Directory.GetFiles(
                Path.Combine(root, "CAMS Backups"),
                "CAMS_*_pre-restore_*.db",
                SearchOption.TopDirectoryOnly);
            Assert.NotEmpty(safetyBackups);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StartupRestore_WithTamperedStage_LeavesLiveDatabaseUnchanged()
    {
        var root = CreateTemporaryDirectory();
        var databasePath = Path.Combine(root, "CAMS.db");

        try
        {
            await using (var db = await CreateDatabaseAsync(databasePath))
            {
                db.AuditLogs.Add(CreateAudit("LiveDatabaseMarker"));
                await db.SaveChangesAsync();

                var service = CreateService(db, root);
                var backup = await service.CreateBackupAsync("tamper-test");
                await service.StageRestoreAsync(
                    backup.FileName,
                    new DatabaseRestoreActor(1, "127.0.0.1"));
            }

            var stagedPath = Assert.Single(Directory.GetFiles(
                root,
                "CAMS.db.restore-*.pending",
                SearchOption.TopDirectoryOnly));
            await File.WriteAllBytesAsync(stagedPath, "tampered"u8.ToArray());

            SqliteConnection.ClearAllPools();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        $"Data Source={databasePath};Pooling=False"
                })
                .Build();

            var startupResult = DatabaseRestoreStartup.ApplyPendingRestore(
                configuration,
                root);

            Assert.True(startupResult.PendingRestoreFound);
            Assert.False(startupResult.Applied);
            Assert.True(File.Exists(databasePath + ".restore-pending.json"));

            await using var liveDb = CreateContext(databasePath);
            Assert.True(await liveDb.AuditLogs.AnyAsync(
                audit => audit.Action == "LiveDatabaseMarker"));
            Assert.False(await liveDb.AuditLogs.AnyAsync(
                audit => audit.Action == "DatabaseRestoreApplied"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cams-db-maintenance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<ApplicationDbContext> CreateDatabaseAsync(string databasePath)
    {
        var db = CreateContext(databasePath);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static ApplicationDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static DatabaseMaintenanceService CreateService(
        ApplicationDbContext db,
        string contentRoot)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.ContentRootPath).Returns(contentRoot);
        return new DatabaseMaintenanceService(
            db,
            environment.Object,
            NullLogger<DatabaseMaintenanceService>.Instance);
    }

    private static AuditLog CreateAudit(string action)
    {
        return new AuditLog
        {
            UserType = "System",
            Action = action,
            Details = action,
            Timestamp = DateTime.UtcNow
        };
    }
}
