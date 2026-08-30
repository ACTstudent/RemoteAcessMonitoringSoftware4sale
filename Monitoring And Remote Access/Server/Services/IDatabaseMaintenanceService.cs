namespace Server.Services;

public interface IDatabaseMaintenanceService
{
    Task<DatabaseMaintenanceOverview> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DatabaseBackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);

    Task<DatabaseBackupInfo> CreateBackupAsync(string? label, CancellationToken cancellationToken = default);

    Task<BackupValidationResult> ValidateBackupAsync(
        string backupFileName,
        CancellationToken cancellationToken = default);

    Task<DatabaseRestoreStageResult> StageRestoreAsync(
        string backupFileName,
        DatabaseRestoreActor actor,
        CancellationToken cancellationToken = default);
}

public sealed record DatabaseMaintenanceOverview(
    DatabaseHealthInfo Health,
    IReadOnlyList<DatabaseBackupInfo> Backups,
    PendingDatabaseRestoreInfo? PendingRestore,
    string? ErrorMessage = null);

public sealed record DatabaseHealthInfo(
    string DatabasePath,
    long? DatabaseSizeBytes,
    DatabaseIntegrityResult Integrity,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    string? MigrationStatusError,
    DatabaseBackupInfo? LatestBackup);

public sealed record DatabaseIntegrityResult(bool IsHealthy, IReadOnlyList<string> Results);

public sealed record DatabaseBackupInfo(
    string FileName,
    string? Label,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc);

public sealed record BackupValidationResult(bool IsValid, IReadOnlyList<string> Results);

public sealed record DatabaseRestoreActor(int? AdminId, string? IpAddress);

public sealed record DatabaseRestoreStageResult(
    string BackupFileName,
    string SafetyBackupFileName,
    DateTimeOffset StagedAtUtc,
    bool RestartRequired);

public sealed record PendingDatabaseRestoreInfo(
    string BackupFileName,
    string? SafetyBackupFileName,
    DateTimeOffset? StagedAtUtc,
    bool IsReady,
    string Status);
