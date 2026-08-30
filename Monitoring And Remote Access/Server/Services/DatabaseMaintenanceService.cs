using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Data;

namespace Server.Services;

public sealed class DatabaseMaintenanceService : IDatabaseMaintenanceService
{
    private static readonly SemaphoreSlim MaintenanceGate = new(1, 1);

    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DatabaseMaintenanceService> _logger;

    public DatabaseMaintenanceService(
        ApplicationDbContext db,
        IWebHostEnvironment environment,
        ILogger<DatabaseMaintenanceService> logger)
    {
        _db = db;
        _environment = environment;
        _logger = logger;
    }

    public async Task<DatabaseMaintenanceOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        await MaintenanceGate.WaitAsync(cancellationToken);
        try
        {
            var paths = GetPaths();
            SqliteMaintenanceFiles.EnsureBackupDirectory(paths);

            var backups = ListBackupsCore(paths);
            var integrity = SqliteMaintenanceFiles.ValidateDatabase(
                paths,
                paths.DatabasePath,
                requireCamsSchema: false);

            IReadOnlyList<string> appliedMigrations = Array.Empty<string>();
            IReadOnlyList<string> pendingMigrations = Array.Empty<string>();
            string? migrationError = null;

            try
            {
                appliedMigrations = (await _db.Database
                    .GetAppliedMigrationsAsync(cancellationToken))
                    .ToArray();
                pendingMigrations = (await _db.Database
                    .GetPendingMigrationsAsync(cancellationToken))
                    .ToArray();
            }
            catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Could not read the SQLite migration status.");
                migrationError = "Migration status could not be read.";
            }

            long? databaseSize = null;
            try
            {
                databaseSize = new FileInfo(paths.DatabasePath).Length;
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Could not read the SQLite database size.");
            }

            var health = new DatabaseHealthInfo(
                paths.DatabasePath,
                databaseSize,
                new DatabaseIntegrityResult(integrity.IsValid, integrity.Results),
                appliedMigrations,
                pendingMigrations,
                migrationError,
                backups.FirstOrDefault());

            return new DatabaseMaintenanceOverview(
                health,
                backups,
                SqliteMaintenanceFiles.GetPendingRestoreInfo(paths));
        }
        finally
        {
            MaintenanceGate.Release();
        }
    }

    public async Task<IReadOnlyList<DatabaseBackupInfo>> ListBackupsAsync(
        CancellationToken cancellationToken = default)
    {
        await MaintenanceGate.WaitAsync(cancellationToken);
        try
        {
            var paths = GetPaths();
            SqliteMaintenanceFiles.EnsureBackupDirectory(paths);
            return ListBackupsCore(paths);
        }
        finally
        {
            MaintenanceGate.Release();
        }
    }

    public async Task<DatabaseBackupInfo> CreateBackupAsync(
        string? label,
        CancellationToken cancellationToken = default)
    {
        await MaintenanceGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paths = GetPaths();
            SqliteMaintenanceFiles.EnsureBackupDirectory(paths);

            var fileName = SqliteMaintenanceFiles.CreateBackupFileName(label);
            var destinationPath = Path.Combine(paths.BackupDirectory, fileName);

            try
            {
                SqliteMaintenanceFiles.CreateOnlineBackup(paths, destinationPath);
                cancellationToken.ThrowIfCancellationRequested();

                var validation = SqliteMaintenanceFiles.ValidateDatabase(
                    paths,
                    destinationPath,
                    requireCamsSchema: true);
                if (!validation.IsValid)
                {
                    throw new InvalidOperationException(
                        "The backup was created but failed integrity validation and was removed.");
                }

                var backup = SqliteMaintenanceFiles.GetBackupInfo(destinationPath);
                _logger.LogInformation("Created SQLite backup {BackupFileName}.", backup.FileName);
                return backup;
            }
            catch
            {
                SqliteMaintenanceFiles.DeleteDatabaseArtifacts(destinationPath);
                throw;
            }
        }
        finally
        {
            MaintenanceGate.Release();
        }
    }

    public async Task<BackupValidationResult> ValidateBackupAsync(
        string backupFileName,
        CancellationToken cancellationToken = default)
    {
        await MaintenanceGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paths = GetPaths();
            var backupPath = SqliteMaintenanceFiles.ResolveKnownBackup(paths, backupFileName);
            return SqliteMaintenanceFiles.ValidateDatabase(
                paths,
                backupPath,
                requireCamsSchema: true);
        }
        finally
        {
            MaintenanceGate.Release();
        }
    }

    public async Task<DatabaseRestoreStageResult> StageRestoreAsync(
        string backupFileName,
        DatabaseRestoreActor actor,
        CancellationToken cancellationToken = default)
    {
        await MaintenanceGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paths = GetPaths();
            SqliteMaintenanceFiles.EnsureBackupDirectory(paths);

            var backupPath = SqliteMaintenanceFiles.ResolveKnownBackup(paths, backupFileName);
            var backupValidation = SqliteMaintenanceFiles.ValidateDatabase(
                paths,
                backupPath,
                requireCamsSchema: true);
            if (!backupValidation.IsValid)
            {
                throw new InvalidOperationException(
                    "The selected backup failed integrity validation and was not staged.");
            }

            // Preserve the current state before committing a restart-time restore request.
            var safetyBackupName = SqliteMaintenanceFiles.CreateBackupFileName("pre-restore");
            var safetyBackupPath = Path.Combine(paths.BackupDirectory, safetyBackupName);
            try
            {
                SqliteMaintenanceFiles.CreateOnlineBackup(paths, safetyBackupPath);
                var safetyValidation = SqliteMaintenanceFiles.ValidateDatabase(
                    paths,
                    safetyBackupPath,
                    requireCamsSchema: true);
                if (!safetyValidation.IsValid)
                {
                    throw new InvalidOperationException(
                        "A safety backup of the current database could not be validated.");
                }
            }
            catch
            {
                SqliteMaintenanceFiles.DeleteDatabaseArtifacts(safetyBackupPath);
                throw;
            }

            var previousManifest = SqliteMaintenanceFiles.TryReadPendingManifest(paths, out _);
            var token = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            var stagedPath = paths.GetStagedDatabasePath(token);
            var manifestCommitted = false;

            try
            {
                await SqliteMaintenanceFiles.CopyFileDurablyAsync(
                    backupPath,
                    stagedPath,
                    cancellationToken);

                var stagedValidation = SqliteMaintenanceFiles.ValidateDatabase(
                    paths,
                    stagedPath,
                    requireCamsSchema: true);
                if (!stagedValidation.IsValid)
                {
                    throw new InvalidOperationException(
                        "The staged database failed integrity validation.");
                }

                var stagedHash = await SqliteMaintenanceFiles.ComputeSha256Async(
                    stagedPath,
                    cancellationToken);
                var stagedAt = DateTimeOffset.UtcNow;
                var manifest = new PendingRestoreManifest(
                    PendingRestoreManifest.CurrentVersion,
                    token,
                    backupFileName,
                    safetyBackupName,
                    stagedHash,
                    stagedAt,
                    actor.AdminId,
                    SqliteMaintenanceFiles.NormalizeIpAddress(actor.IpAddress));

                await SqliteMaintenanceFiles.CommitPendingManifestAsync(
                    paths,
                    manifest,
                    cancellationToken);
                manifestCommitted = true;

                SqliteMaintenanceFiles.TryDeletePreviousStagedDatabase(
                    paths,
                    previousManifest,
                    token,
                    _logger);

                _logger.LogWarning(
                    "Staged SQLite backup {BackupFileName} for restore on restart.",
                    backupFileName);

                return new DatabaseRestoreStageResult(
                    backupFileName,
                    safetyBackupName,
                    stagedAt,
                    RestartRequired: true);
            }
            catch
            {
                if (!manifestCommitted)
                {
                    SqliteMaintenanceFiles.DeleteDatabaseArtifacts(stagedPath);
                }
                throw;
            }
        }
        finally
        {
            MaintenanceGate.Release();
        }
    }

    private SqliteMaintenancePaths GetPaths()
    {
        if (!_db.Database.IsSqlite() ||
            _db.Database.GetDbConnection() is not SqliteConnection sqliteConnection)
        {
            throw new NotSupportedException(
                "Database maintenance is available only for the local Microsoft.Data.Sqlite database.");
        }

        return SqliteMaintenanceFiles.ResolvePaths(
            sqliteConnection.ConnectionString,
            _environment.ContentRootPath);
    }

    private static IReadOnlyList<DatabaseBackupInfo> ListBackupsCore(SqliteMaintenancePaths paths)
    {
        var backups = new List<DatabaseBackupInfo>();
        foreach (var path in Directory.EnumerateFiles(
                     paths.BackupDirectory,
                     "CAMS_*.db",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (SqliteMaintenanceFiles.IsRegularFile(path) &&
                    SqliteMaintenanceFiles.IsBackupFileName(Path.GetFileName(path)) &&
                    !SqliteMaintenanceFiles.PathsEqual(path, paths.DatabasePath))
                {
                    backups.Add(SqliteMaintenanceFiles.GetBackupInfo(path));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A single inaccessible file must not hide the rest of the backup inventory.
            }
        }

        return backups
            .OrderByDescending(backup => backup.CreatedAtUtc)
            .ThenByDescending(backup => backup.FileName, StringComparer.Ordinal)
            .ToArray();
    }
}

internal sealed record SqliteMaintenancePaths(
    string DatabasePath,
    string BackupDirectory,
    string PendingManifestPath,
    string ConnectionString)
{
    public string GetStagedDatabasePath(string token)
    {
        var databaseDirectory = Path.GetDirectoryName(DatabasePath)
            ?? throw new InvalidOperationException("The database directory could not be determined.");
        return Path.Combine(
            databaseDirectory,
            $"{Path.GetFileName(DatabasePath)}.restore-{token}.pending");
    }
}

internal sealed record PendingRestoreManifest(
    int Version,
    string Token,
    string BackupFileName,
    string SafetyBackupFileName,
    string StagedSha256,
    DateTimeOffset StagedAtUtc,
    int? AdminId,
    string? IpAddress)
{
    public const int CurrentVersion = 1;
}

internal static class SqliteMaintenanceFiles
{
    private const int MaximumBackupLabelLength = 40;
    private const int MaximumIntegrityResults = 100;
    private static readonly Regex BackupFileNamePattern = new(
        "^CAMS_(?<timestamp>\\d{8}T\\d{9}Z)(?:_(?<label>[A-Za-z0-9][A-Za-z0-9_-]{0,39}))?_(?<nonce>[a-f0-9]{8})\\.db$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RestoreTokenPattern = new(
        "^[a-f0-9]{32}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new(
        "^[A-F0-9]{64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static SqliteMaintenancePaths ResolvePaths(
        string connectionString,
        string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The SQLite connection string is not configured.");
        }

        SqliteConnectionStringBuilder builder;
        try
        {
            builder = new SqliteConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException ex)
        {
            throw new NotSupportedException(
                "Database maintenance requires a Microsoft.Data.Sqlite connection string.",
                ex);
        }

        var dataSource = builder.DataSource?.Trim();
        if (string.IsNullOrWhiteSpace(dataSource) ||
            string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase) ||
            builder.Mode == SqliteOpenMode.Memory ||
            dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "Database maintenance requires a local file-based SQLite database.");
        }

        var contentRoot = Path.GetFullPath(contentRootPath);
        var databasePath = Path.IsPathFullyQualified(dataSource)
            ? Path.GetFullPath(dataSource)
            : Path.GetFullPath(Path.Combine(contentRoot, dataSource));

        if (IsNetworkPath(databasePath) || IsNetworkPath(contentRoot))
        {
            throw new NotSupportedException(
                "Database maintenance does not allow network or UNC database paths.");
        }

        var backupDirectory = Path.GetFullPath(Path.Combine(contentRoot, "CAMS Backups"));
        builder.DataSource = databasePath;
        builder.Pooling = false;
        builder.Cache = SqliteCacheMode.Private;

        return new SqliteMaintenancePaths(
            databasePath,
            backupDirectory,
            databasePath + ".restore-pending.json",
            builder.ConnectionString);
    }

    internal static void EnsureBackupDirectory(SqliteMaintenancePaths paths)
    {
        Directory.CreateDirectory(paths.BackupDirectory);
        var attributes = File.GetAttributes(paths.BackupDirectory);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "The controlled CAMS Backups directory cannot be a symbolic link or reparse point.");
        }
    }

    internal static string CreateBackupFileName(string? label)
    {
        var safeLabel = SanitizeLabel(label);
        var labelPart = safeLabel.Length == 0 ? string.Empty : $"_{safeLabel}";
        var nonce = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
        var timestamp = DateTimeOffset.UtcNow.ToString(
            "yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture);
        return $"CAMS_{timestamp}{labelPart}_{nonce}.db";
    }

    internal static bool IsBackupFileName(string? fileName)
    {
        if (fileName is null)
        {
            return false;
        }

        var match = BackupFileNamePattern.Match(fileName);
        return match.Success && DateTimeOffset.TryParseExact(
            match.Groups["timestamp"].Value,
            "yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out _);
    }

    internal static DatabaseBackupInfo GetBackupInfo(string path)
    {
        var fileName = Path.GetFileName(path);
        var match = BackupFileNamePattern.Match(fileName);
        if (!match.Success)
        {
            throw new InvalidOperationException("The file is not a CAMS-managed backup.");
        }

        if (!DateTimeOffset.TryParseExact(
                match.Groups["timestamp"].Value,
                "yyyyMMdd'T'HHmmssfff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var createdAt))
        {
            throw new InvalidOperationException("The backup timestamp is invalid.");
        }

        var label = match.Groups["label"].Success
            ? match.Groups["label"].Value
            : null;
        return new DatabaseBackupInfo(fileName, label, new FileInfo(path).Length, createdAt);
    }

    internal static string ResolveKnownBackup(
        SqliteMaintenancePaths paths,
        string backupFileName)
    {
        if (string.IsNullOrWhiteSpace(backupFileName) ||
            backupFileName.Length > 100 ||
            !string.Equals(
                Path.GetFileName(backupFileName),
                backupFileName,
                StringComparison.Ordinal) ||
            !IsBackupFileName(backupFileName))
        {
            throw new ArgumentException(
                "Select a backup from the CAMS backup list.",
                nameof(backupFileName));
        }

        EnsureBackupDirectory(paths);
        var candidate = Path.GetFullPath(Path.Combine(paths.BackupDirectory, backupFileName));
        var root = paths.BackupDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidate.StartsWith(root, comparison) || !IsRegularFile(candidate))
        {
            throw new ArgumentException(
                "Select a backup from the CAMS backup list.",
                nameof(backupFileName));
        }

        if (PathsEqual(candidate, paths.DatabasePath))
        {
            throw new ArgumentException(
                "Select a backup from the CAMS backup list.",
                nameof(backupFileName));
        }

        return candidate;
    }

    internal static bool IsRegularFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var attributes = File.GetAttributes(path);
        return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
    }

    internal static void CreateOnlineBackup(SqliteMaintenancePaths paths, string destinationPath)
    {
        EnsureRegularDatabaseFile(paths.DatabasePath);

        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("The backup directory could not be determined.");
        Directory.CreateDirectory(destinationDirectory);

        using (var reservation = new FileStream(
                   destinationPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   bufferSize: 1,
                   FileOptions.WriteThrough))
        {
            reservation.Flush(flushToDisk: true);
        }

        try
        {
            using var source = CreateConnection(paths, paths.DatabasePath, SqliteOpenMode.ReadOnly);
            using var destination = CreateConnection(
                paths,
                destinationPath,
                SqliteOpenMode.ReadWriteCreate);
            source.Open();
            destination.Open();

            using (var command = destination.CreateCommand())
            {
                command.CommandText = "PRAGMA synchronous = FULL;";
                command.ExecuteNonQuery();
            }

            // sqlite3_backup provides a transactionally consistent snapshot while the live DB stays online.
            source.BackupDatabase(destination);
        }
        catch
        {
            DeleteDatabaseArtifacts(destinationPath);
            throw;
        }
    }

    internal static BackupValidationResult ValidateDatabase(
        SqliteMaintenancePaths paths,
        string databasePath,
        bool requireCamsSchema)
    {
        try
        {
            EnsureRegularDatabaseFile(databasePath);
            if (new FileInfo(databasePath).Length == 0)
            {
                return new BackupValidationResult(
                    false,
                    new[] { "The database file is empty." });
            }

            using var connection = CreateConnection(paths, databasePath, SqliteOpenMode.ReadOnly);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            command.CommandTimeout = 30;

            var results = new List<string>();
            var resultsTruncated = false;
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (results.Count == MaximumIntegrityResults)
                    {
                        resultsTruncated = true;
                        break;
                    }

                    var result = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    results.Add(result.Length <= 500 ? result : result[..500]);
                }
            }

            if (resultsTruncated)
            {
                results.Add("Additional integrity errors were omitted.");
            }

            var integrityIsValid = results.Count == 1 &&
                string.Equals(results[0], "ok", StringComparison.OrdinalIgnoreCase);
            if (!integrityIsValid || !requireCamsSchema)
            {
                return new BackupValidationResult(integrityIsValid, results);
            }

            command.CommandText =
                "SELECT name FROM sqlite_schema " +
                "WHERE type = 'table' AND name IN ('Admins', 'AuditLogs');";
            var requiredTables = new HashSet<string>(StringComparer.Ordinal);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    requiredTables.Add(reader.GetString(0));
                }
            }

            if (!requiredTables.SetEquals(new[] { "Admins", "AuditLogs" }))
            {
                return new BackupValidationResult(
                    false,
                    new[] { "Required CAMS database tables are missing." });
            }

            if (!HasRequiredColumns(
                    connection,
                    "Admins",
                    "Id",
                    "Username",
                    "PasswordHash") ||
                !HasRequiredColumns(
                    connection,
                    "AuditLogs",
                    "AuditLogId",
                    "UserType",
                    "UserId",
                    "Action",
                    "Details",
                    "IpAddress",
                    "Timestamp"))
            {
                return new BackupValidationResult(
                    false,
                    new[] { "Required CAMS database columns are missing." });
            }

            return new BackupValidationResult(true, results);
        }
        catch (Exception ex) when (
            ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new BackupValidationResult(
                false,
                new[] { "SQLite could not complete the integrity check." });
        }
    }

    internal static async Task CopyFileDurablyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);

        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        destination.Flush(flushToDisk: true);
    }

    internal static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    internal static string ComputeSha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static async Task CommitPendingManifestAsync(
        SqliteMaintenancePaths paths,
        PendingRestoreManifest manifest,
        CancellationToken cancellationToken)
    {
        var temporaryManifestPath = paths.PendingManifestPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryManifestPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(paths.PendingManifestPath) &&
                !IsRegularFile(paths.PendingManifestPath))
            {
                throw new InvalidOperationException(
                    "The pending restore marker is not a regular file.");
            }

            if (File.Exists(paths.PendingManifestPath))
            {
                File.Replace(
                    temporaryManifestPath,
                    paths.PendingManifestPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryManifestPath, paths.PendingManifestPath);
            }
        }
        finally
        {
            TryDeleteFile(temporaryManifestPath);
        }
    }

    internal static PendingRestoreManifest? TryReadPendingManifest(
        SqliteMaintenancePaths paths,
        out string? error)
    {
        error = null;
        if (!File.Exists(paths.PendingManifestPath))
        {
            return null;
        }

        try
        {
            if (!IsRegularFile(paths.PendingManifestPath))
            {
                error = "The pending restore marker is not a regular file.";
                return null;
            }

            if (new FileInfo(paths.PendingManifestPath).Length > 16 * 1024)
            {
                error = "The pending restore marker is too large.";
                return null;
            }

            using var stream = new FileStream(
                paths.PendingManifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            var manifest = JsonSerializer.Deserialize<PendingRestoreManifest>(stream);
            if (!IsValidManifest(manifest))
            {
                error = "The pending restore marker is invalid.";
                return null;
            }

            return manifest;
        }
        catch (Exception ex) when (
            ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = "The pending restore marker could not be read.";
            return null;
        }
    }

    internal static PendingDatabaseRestoreInfo? GetPendingRestoreInfo(
        SqliteMaintenancePaths paths)
    {
        var manifest = TryReadPendingManifest(paths, out var error);
        if (manifest is null)
        {
            return File.Exists(paths.PendingManifestPath)
                ? new PendingDatabaseRestoreInfo(
                    "Unknown",
                    null,
                    null,
                    false,
                    error ?? "The pending restore marker is invalid.")
                : null;
        }

        var stagedPath = paths.GetStagedDatabasePath(manifest.Token);
        var ready = IsRegularFile(stagedPath);
        return new PendingDatabaseRestoreInfo(
            manifest.BackupFileName,
            manifest.SafetyBackupFileName,
            manifest.StagedAtUtc,
            ready,
            ready
                ? "Restore is staged and will be revalidated and applied on the next server start."
                : "The staged database file is missing or unsafe; the live database was not changed.");
    }

    internal static void TryDeletePreviousStagedDatabase(
        SqliteMaintenancePaths paths,
        PendingRestoreManifest? previousManifest,
        string currentToken,
        ILogger logger)
    {
        if (previousManifest is null ||
            string.Equals(previousManifest.Token, currentToken, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var previousPath = paths.GetStagedDatabasePath(previousManifest.Token);
            if (IsRegularFile(previousPath))
            {
                DeleteDatabaseArtifacts(previousPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not remove an obsolete staged restore file.");
        }
    }

    internal static bool IsValidManifest(PendingRestoreManifest? manifest)
    {
        return manifest is not null &&
            manifest.Version == PendingRestoreManifest.CurrentVersion &&
            RestoreTokenPattern.IsMatch(manifest.Token ?? string.Empty) &&
            IsBackupFileName(manifest.BackupFileName) &&
            IsBackupFileName(manifest.SafetyBackupFileName) &&
            Sha256Pattern.IsMatch(manifest.StagedSha256 ?? string.Empty) &&
            manifest.AdminId is null or > 0 &&
            (manifest.IpAddress is null || manifest.IpAddress.Length <= 50);
    }

    internal static string? NormalizeIpAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= 50 ? normalized : normalized[..50];
    }

    internal static void DeleteDatabaseArtifacts(string databasePath)
    {
        TryDeleteFile(databasePath + "-wal");
        TryDeleteFile(databasePath + "-shm");
        TryDeleteFile(databasePath + "-journal");
        TryDeleteFile(databasePath);
    }

    internal static void DeleteSidecarsForReplacement(string databasePath)
    {
        DeleteRegularFileIfPresent(databasePath + "-wal");
        DeleteRegularFileIfPresent(databasePath + "-shm");
        DeleteRegularFileIfPresent(databasePath + "-journal");
    }

    internal static SqliteConnection CreateConnection(
        SqliteMaintenancePaths paths,
        string databasePath,
        SqliteOpenMode mode)
    {
        var builder = new SqliteConnectionStringBuilder(paths.ConnectionString)
        {
            DataSource = databasePath,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 30
        };
        return new SqliteConnection(builder.ConnectionString);
    }

    internal static void EnsureRegularDatabaseFile(string path)
    {
        if (!IsRegularFile(path))
        {
            throw new InvalidOperationException(
                "The SQLite database file is missing or is not a regular local file.");
        }
    }

    internal static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }

    private static string SanitizeLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var result = new StringBuilder(MaximumBackupLabelLength);
        var separatorPending = false;
        foreach (var character in value.Trim())
        {
            if (result.Length >= MaximumBackupLabelLength)
            {
                break;
            }

            var isAsciiLetterOrDigit =
                character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
            if (isAsciiLetterOrDigit)
            {
                if (separatorPending &&
                    result.Length > 0 &&
                    result.Length < MaximumBackupLabelLength - 1)
                {
                    result.Append('-');
                }

                if (result.Length < MaximumBackupLabelLength)
                {
                    result.Append(character);
                }
                separatorPending = false;
            }
            else
            {
                separatorPending = result.Length > 0;
            }
        }

        return result.ToString().TrimEnd('-');
    }

    private static bool HasRequiredColumns(
        SqliteConnection connection,
        string tableName,
        params string[] requiredColumns)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info($tableName);";
        command.Parameters.AddWithValue("$tableName", tableName);

        var actualColumns = new HashSet<string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            actualColumns.Add(reader.GetString(0));
        }

        return requiredColumns.All(actualColumns.Contains);
    }

    private static bool IsNetworkPath(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return true;
        }

        var root = Path.GetPathRoot(path);
        return !string.IsNullOrWhiteSpace(root) &&
            new DriveInfo(root).DriveType == DriveType.Network;
    }

    private static void DeleteRegularFileIfPresent(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        if (!IsRegularFile(path))
        {
            throw new InvalidOperationException(
                "A SQLite sidecar path is not a regular file.");
        }

        File.Delete(path);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path) && IsRegularFile(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup must not hide the original maintenance failure.
        }
    }
}
