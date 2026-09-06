using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;
using Shared.Contracts;

namespace Server.Controllers;

[Authorize(Roles = RoleNames.Admin)]
[AutoValidateAntiforgeryToken]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AdminDatabaseController : Controller
{
    private readonly IDatabaseMaintenanceService _maintenance;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AdminDatabaseController> _logger;

    public AdminDatabaseController(
        IDatabaseMaintenanceService maintenance,
        ApplicationDbContext db,
        ILogger<AdminDatabaseController> logger)
    {
        _maintenance = maintenance;
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!HttpContext.IsAdmin())
        {
            return Denied();
        }

        try
        {
            return View(await _maintenance.GetOverviewAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is SqliteException or IOException or UnauthorizedAccessException or
                InvalidOperationException or NotSupportedException or ArgumentException)
        {
            _logger.LogError(ex, "Could not load database maintenance status.");
            return View(new DatabaseMaintenanceOverview(
                new DatabaseHealthInfo(
                    "Unavailable",
                    null,
                    new DatabaseIntegrityResult(
                        false,
                        new[] { "The integrity check could not be completed." }),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    "Migration status could not be read.",
                    null),
                Array.Empty<DatabaseBackupInfo>(),
                null,
                "Database maintenance status is unavailable. Review the server log."));
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateBackup(
        string? label,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.IsAdmin())
        {
            return Denied();
        }

        if (label?.Length > 200)
        {
            TempData["ErrorMessage"] = "The backup label is too long.";
            return RedirectToAction(nameof(Index));
        }

        await AuditAsync(
            "DatabaseBackupRequested",
            "An administrator requested an online database backup.",
            cancellationToken);

        try
        {
            var backup = await _maintenance.CreateBackupAsync(label, cancellationToken);
            await AuditAsync(
                "DatabaseBackupCreated",
                $"Created {backup.FileName} ({backup.SizeBytes} bytes).",
                cancellationToken);
            TempData["Message"] = $"Backup '{backup.FileName}' was created and validated.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsMaintenanceFailure(ex))
        {
            _logger.LogError(ex, "Online SQLite backup creation failed.");
            await AuditAsync(
                "DatabaseBackupFailed",
                $"Online database backup failed ({ex.GetType().Name}).",
                CancellationToken.None);
            TempData["ErrorMessage"] =
                "The backup could not be created. The live database was not changed.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ValidateBackup(
        string backupFileName,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.IsAdmin())
        {
            return Denied();
        }

        try
        {
            var validation = await _maintenance.ValidateBackupAsync(
                backupFileName,
                cancellationToken);
            if (validation.IsValid)
            {
                TempData["Message"] = $"Backup '{backupFileName}' passed integrity validation.";
            }
            else
            {
                TempData["ErrorMessage"] =
                    $"Backup '{backupFileName}' failed integrity or CAMS schema validation.";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsMaintenanceFailure(ex))
        {
            _logger.LogWarning(ex, "Backup validation failed for an invalid selection.");
            TempData["ErrorMessage"] = "Select a backup from the CAMS backup list.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> StageRestore(
        string backupFileName,
        string? confirmation,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.IsAdmin())
        {
            return Denied();
        }

        if (!string.Equals(confirmation?.Trim(), "RESTORE", StringComparison.Ordinal))
        {
            TempData["ErrorMessage"] =
                "Type RESTORE exactly to stage a database restore.";
            return RedirectToAction(nameof(Index));
        }

        await AuditAsync(
            "DatabaseRestoreRequested",
            "An administrator requested a validated, restart-time database restore.",
            cancellationToken);

        try
        {
            var result = await _maintenance.StageRestoreAsync(
                backupFileName,
                new DatabaseRestoreActor(
                    HttpContext.Session.GetInt32("AdminId"),
                    HttpContext.Connection.RemoteIpAddress?.ToString()),
                cancellationToken);

            await AuditAsync(
                "DatabaseRestoreStaged",
                $"Staged {result.BackupFileName}; safety backup {result.SafetyBackupFileName}.",
                cancellationToken);
            TempData["Message"] =
                $"Backup '{result.BackupFileName}' is staged. Restart the CAMS server to apply it.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsMaintenanceFailure(ex))
        {
            _logger.LogError(ex, "SQLite restore staging failed.");
            await AuditAsync(
                "DatabaseRestoreFailed",
                $"Database restore staging failed ({ex.GetType().Name}); the live database was not changed.",
                CancellationToken.None);
            TempData["ErrorMessage"] =
                "The restore was not staged. The selected backup may be invalid; the live database was not changed.";
        }

        return RedirectToAction(nameof(Index));
    }

    private IActionResult Denied()
    {
        return RedirectToAction("Login", "Account");
    }

    private async Task AuditAsync(
        string action,
        string details,
        CancellationToken cancellationToken)
    {
        try
        {
            _db.AuditLogs.Add(new AuditLog
            {
                UserType = "Admin",
                UserId = HttpContext.Session.GetInt32("AdminId"),
                Action = action,
                Details = details.Length <= 1000 ? details : details[..1000],
                IpAddress = NormalizeIpAddress(HttpContext.Connection.RemoteIpAddress?.ToString()),
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (
            ex is DbUpdateException or SqliteException or InvalidOperationException or
                OperationCanceledException)
        {
            _db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Could not record database maintenance audit action {Action}.", action);
        }
    }

    private static string? NormalizeIpAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= 50 ? normalized : normalized[..50];
    }

    private static bool IsMaintenanceFailure(Exception ex)
    {
        return ex is SqliteException or IOException or UnauthorizedAccessException or
            InvalidOperationException or NotSupportedException or ArgumentException;
    }
}
