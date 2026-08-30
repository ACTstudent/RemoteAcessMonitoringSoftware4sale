using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Server.Services;

public sealed record DatabaseRestoreStartupResult(
    bool PendingRestoreFound,
    bool Applied,
    string Message,
    string? BackupFileName = null,
    string? SafetyBackupFileName = null);

public static class DatabaseRestoreStartup
{
    public static DatabaseRestoreStartupResult ApplyPendingRestore(
        IConfiguration configuration,
        string contentRootPath,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        SqliteMaintenancePaths paths;
        try
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? $"Data Source={Path.Combine(contentRootPath, "CAMS.db")}";
            paths = SqliteMaintenanceFiles.ResolvePaths(connectionString, contentRootPath);
        }
        catch (Exception ex) when (
            ex is ArgumentException or IOException or UnauthorizedAccessException or
                InvalidOperationException or NotSupportedException)
        {
            log?.Invoke("Pending database restore check was skipped because the SQLite path is invalid.");
            return new DatabaseRestoreStartupResult(
                PendingRestoreFound: false,
                Applied: false,
                "The local SQLite path is invalid.");
        }

        var manifest = SqliteMaintenanceFiles.TryReadPendingManifest(paths, out var manifestError);
        if (manifest is null)
        {
            if (!File.Exists(paths.PendingManifestPath))
            {
                return new DatabaseRestoreStartupResult(
                    PendingRestoreFound: false,
                    Applied: false,
                    "No database restore is pending.");
            }

            log?.Invoke(manifestError ?? "The pending database restore marker is invalid.");
            return new DatabaseRestoreStartupResult(
                PendingRestoreFound: true,
                Applied: false,
                manifestError ?? "The pending database restore marker is invalid.");
        }

        var stagedPath = paths.GetStagedDatabasePath(manifest.Token);
        var replacementCompleted = false;
        var safetyBackupName = manifest.SafetyBackupFileName;

        try
        {
            if (!SqliteMaintenanceFiles.IsRegularFile(stagedPath))
            {
                if (RestoreWasAlreadyApplied(paths, manifest))
                {
                    DeletePendingManifest(paths);
                    TryWriteRestoreAudit(paths, manifest, manifest.SafetyBackupFileName, log);
                    log?.Invoke($"Completed recovery bookkeeping for restored backup {manifest.BackupFileName}.");
                    return new DatabaseRestoreStartupResult(
                        PendingRestoreFound: true,
                        Applied: true,
                        "The staged database restore had already been applied.",
                        manifest.BackupFileName,
                        manifest.SafetyBackupFileName);
                }

                return RestoreFailed(
                    manifest,
                    "The staged database file is missing; the live database was not changed.",
                    log);
            }

            var actualHash = SqliteMaintenanceFiles.ComputeSha256(stagedPath);
            if (!HashesMatch(manifest.StagedSha256, actualHash))
            {
                return RestoreFailed(
                    manifest,
                    "The staged database hash does not match its restore marker; the live database was not changed.",
                    log);
            }

            var stagedValidation = SqliteMaintenanceFiles.ValidateDatabase(
                paths,
                stagedPath,
                requireCamsSchema: true);
            if (!stagedValidation.IsValid)
            {
                return RestoreFailed(
                    manifest,
                    "The staged database failed its startup integrity check; the live database was not changed.",
                    log);
            }

            var stagedSafetyPath = SqliteMaintenanceFiles.ResolveKnownBackup(
                paths,
                manifest.SafetyBackupFileName);
            var stagedSafetyValidation = SqliteMaintenanceFiles.ValidateDatabase(
                paths,
                stagedSafetyPath,
                requireCamsSchema: true);
            if (!stagedSafetyValidation.IsValid)
            {
                return RestoreFailed(
                    manifest,
                    "The restore safety backup is unavailable or invalid; the live database was not changed.",
                    log);
            }

            if (File.Exists(paths.DatabasePath))
            {
                SqliteMaintenanceFiles.EnsureRegularDatabaseFile(paths.DatabasePath);
                safetyBackupName = TryCreateFreshSafetyBackup(paths, manifest.SafetyBackupFileName, log);

                if (!TryCheckpointWal(paths, out var checkpointError))
                {
                    return RestoreFailed(manifest, checkpointError, log, safetyBackupName);
                }
            }

            var finalHash = SqliteMaintenanceFiles.ComputeSha256(stagedPath);
            if (!HashesMatch(manifest.StagedSha256, finalHash))
            {
                return RestoreFailed(
                    manifest,
                    "The staged database changed during startup validation; the live database was not changed.",
                    log,
                    safetyBackupName);
            }

            // This hook runs before any application DbContext is opened. Clearing pools also
            // closes connections retained from earlier service-provider activity in this process.
            SqliteConnection.ClearAllPools();
            SqliteMaintenanceFiles.DeleteSidecarsForReplacement(paths.DatabasePath);

            if (File.Exists(paths.DatabasePath))
            {
                File.Replace(
                    stagedPath,
                    paths.DatabasePath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(stagedPath, paths.DatabasePath);
            }

            replacementCompleted = true;
            DeletePendingManifest(paths);
            TryWriteRestoreAudit(paths, manifest, safetyBackupName, log);

            log?.Invoke($"Applied staged database restore from {manifest.BackupFileName}.");
            return new DatabaseRestoreStartupResult(
                PendingRestoreFound: true,
                Applied: true,
                "The staged database restore was applied successfully.",
                manifest.BackupFileName,
                safetyBackupName);
        }
        catch (Exception ex) when (
            ex is SqliteException or IOException or UnauthorizedAccessException or
                InvalidOperationException or NotSupportedException or ArgumentException or
                CryptographicException)
        {
            if (replacementCompleted)
            {
                log?.Invoke(
                    "The database restore was applied, but post-restore bookkeeping could not be completed.");
                return new DatabaseRestoreStartupResult(
                    PendingRestoreFound: true,
                    Applied: true,
                    "The restore was applied; review the pending marker and audit log.",
                    manifest.BackupFileName,
                    safetyBackupName);
            }

            log?.Invoke(
                $"The staged database restore was not applied ({ex.GetType().Name}); the live database was not changed.");
            return new DatabaseRestoreStartupResult(
                PendingRestoreFound: true,
                Applied: false,
                "The staged database restore could not be applied; the live database was not changed.",
                manifest.BackupFileName,
                safetyBackupName);
        }
    }

    private static string TryCreateFreshSafetyBackup(
        SqliteMaintenancePaths paths,
        string existingSafetyBackupName,
        Action<string>? log)
    {
        var freshName = SqliteMaintenanceFiles.CreateBackupFileName("pre-restore");
        var freshPath = Path.Combine(paths.BackupDirectory, freshName);
        try
        {
            SqliteMaintenanceFiles.CreateOnlineBackup(paths, freshPath);
            var validation = SqliteMaintenanceFiles.ValidateDatabase(
                paths,
                freshPath,
                requireCamsSchema: true);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException("The fresh safety backup failed validation.");
            }

            return freshName;
        }
        catch (Exception ex) when (
            ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SqliteMaintenanceFiles.DeleteDatabaseArtifacts(freshPath);
            log?.Invoke(
                $"A fresh pre-restore backup could not be created ({ex.GetType().Name}); using the validated staged safety backup.");
            return existingSafetyBackupName;
        }
    }

    private static bool TryCheckpointWal(
        SqliteMaintenancePaths paths,
        out string error)
    {
        error = string.Empty;
        try
        {
            using var connection = SqliteMaintenanceFiles.CreateConnection(
                paths,
                paths.DatabasePath,
                SqliteOpenMode.ReadWrite);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.CommandTimeout = 30;
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                error =
                    "The live database returned no checkpoint status; it was not changed.";
                return false;
            }

            if (reader.GetInt64(0) != 0)
            {
                error =
                    "The live database is still busy and could not be checkpointed; it was not changed.";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            error =
                "The live database could not be checkpointed for replacement; it was not changed.";
            return false;
        }
    }

    private static bool RestoreWasAlreadyApplied(
        SqliteMaintenancePaths paths,
        PendingRestoreManifest manifest)
    {
        if (!SqliteMaintenanceFiles.IsRegularFile(paths.DatabasePath))
        {
            return false;
        }

        var currentHash = SqliteMaintenanceFiles.ComputeSha256(paths.DatabasePath);
        return HashesMatch(manifest.StagedSha256, currentHash);
    }

    private static bool HashesMatch(string expectedHex, string actualHex)
    {
        var expected = Convert.FromHexString(expectedHex);
        var actual = Convert.FromHexString(actualHex);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static void DeletePendingManifest(SqliteMaintenancePaths paths)
    {
        if (!File.Exists(paths.PendingManifestPath))
        {
            return;
        }

        if (!SqliteMaintenanceFiles.IsRegularFile(paths.PendingManifestPath))
        {
            throw new InvalidOperationException(
                "The pending restore marker is not a regular file.");
        }

        File.Delete(paths.PendingManifestPath);
    }

    private static void TryWriteRestoreAudit(
        SqliteMaintenancePaths paths,
        PendingRestoreManifest manifest,
        string safetyBackupName,
        Action<string>? log)
    {
        try
        {
            using var connection = SqliteMaintenanceFiles.CreateConnection(
                paths,
                paths.DatabasePath,
                SqliteOpenMode.ReadWrite);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO AuditLogs " +
                "(UserType, UserId, Action, Details, IpAddress, Timestamp) " +
                "VALUES ('Admin', $userId, $action, $details, $ipAddress, $timestamp);";
            command.Parameters.AddWithValue(
                "$userId",
                manifest.AdminId.HasValue ? manifest.AdminId.Value : DBNull.Value);
            command.Parameters.AddWithValue("$action", "DatabaseRestoreApplied");
            command.Parameters.AddWithValue(
                "$details",
                $"Restored {manifest.BackupFileName}; safety backup {safetyBackupName}.");
            command.Parameters.AddWithValue(
                "$ipAddress",
                manifest.IpAddress is null ? DBNull.Value : manifest.IpAddress);
            command.Parameters.AddWithValue("$timestamp", DateTime.UtcNow);
            command.ExecuteNonQuery();
        }
        catch (Exception ex) when (
            ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            log?.Invoke(
                $"The restore succeeded, but its audit result could not be recorded ({ex.GetType().Name}).");
        }
    }

    private static DatabaseRestoreStartupResult RestoreFailed(
        PendingRestoreManifest manifest,
        string message,
        Action<string>? log,
        string? safetyBackupName = null)
    {
        log?.Invoke(message);
        return new DatabaseRestoreStartupResult(
            PendingRestoreFound: true,
            Applied: false,
            message,
            manifest.BackupFileName,
            safetyBackupName ?? manifest.SafetyBackupFileName);
    }
}
