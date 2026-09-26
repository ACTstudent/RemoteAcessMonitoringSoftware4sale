using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Server.Data;
using Server.Models;
using Shared.Contracts;

namespace Server.Services;

public interface IWorkstationRegistrationService
{
    Task<Computer> GetOrCreateForStudentAsync(
        int studentId,
        string pcName,
        CancellationToken cancellationToken = default);
    Task<LabSession> EnsureStudentSessionAsync(
        int studentId,
        string pcName,
        string ipAddress,
        CancellationToken cancellationToken = default);
}

public sealed class WorkstationRegistrationService : IWorkstationRegistrationService
{
    public const string NoLabMessage = "No lab session is running. Wait for your teacher to start it.";

    private readonly ApplicationDbContext _db;
    private readonly SessionManagerService? _lab;

    public WorkstationRegistrationService(ApplicationDbContext db, SessionManagerService? lab = null)
    {
        _db = db;
        _lab = lab;
    }

    /// <summary>
    /// The rule a new session starts under: the one the teacher chose when
    /// starting the lab for everyone, while it is still active, otherwise the
    /// active default rule.
    /// </summary>
    private async Task<SessionRule?> RuleForNewSessionAsync(CancellationToken cancellationToken)
    {
        if (_lab?.LabRuleId is int labRuleId)
        {
            var labRule = await _db.SessionRules.FirstOrDefaultAsync(
                item => item.SessionRuleId == labRuleId && item.IsActive, cancellationToken);
            if (labRule is not null) return labRule;
        }
        return await _db.SessionRules.FirstOrDefaultAsync(item => item.IsActive && item.IsDefault, cancellationToken);
    }

    public async Task<Computer> GetOrCreateForStudentAsync(
        int studentId,
        string pcName,
        CancellationToken cancellationToken = default)
    {
        pcName = pcName.Trim();
        if (pcName.Length is 0 or > 50)
            throw new InvalidOperationException("A valid workstation name is required.");

        IDbContextTransaction? transaction = null;
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction is null)
            transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var student = await _db.Students.FindAsync([studentId], cancellationToken)
                ?? throw new InvalidOperationException("The student account was not found.");
            var studentKey = studentId.ToString();
            var activeStudentSession = await _db.LabSessions.AsNoTracking()
                .FirstOrDefaultAsync(session => session.StudentId == studentId && session.IsActive && session.Status != LabSessionStatus.Ended, cancellationToken);
            if (activeStudentSession is not null &&
                !string.Equals(activeStudentSession.PCName, pcName, StringComparison.OrdinalIgnoreCase))
            {
                await RejectAsync(studentId, pcName, "Student already has an active session on another workstation.", cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                throw new InvalidOperationException("The student already has an active session on another workstation.");
            }

            var normalizedName = pcName.ToLower();
            var computer = await _db.Computers
                .FirstOrDefaultAsync(item => item.LaboratoryStation.ToLower() == normalizedName, cancellationToken);
            if (computer is not null &&
                (string.Equals(computer.Status, "Archived", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(computer.Status, "Maintenance", StringComparison.OrdinalIgnoreCase)))
            {
                await RejectAsync(studentId, pcName, $"Workstation status is {computer.Status}.", cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                throw new InvalidOperationException($"This workstation is currently {computer.Status.ToLowerInvariant()}.");
            }

            if (computer is not null && !string.IsNullOrWhiteSpace(computer.AssignedTo) && computer.AssignedTo != studentKey)
            {
                var occupied = await _db.LabSessions.AsNoTracking().AnyAsync(session =>
                    session.IsActive && session.Status != LabSessionStatus.Ended &&
                    (session.ComputerId == computer.ComputerId || session.PCName.ToLower() == normalizedName), cancellationToken);
                if (occupied)
                {
                    await RejectAsync(studentId, pcName, "Workstation has another active student session.", cancellationToken);
                    if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                    throw new InvalidOperationException("Workstation is currently being used by another student.");
                }
            }

            var previousComputers = await _db.Computers
                .Where(item => item.AssignedTo == studentKey && (computer == null || item.ComputerId != computer.ComputerId))
                .ToListAsync(cancellationToken);
            foreach (var previous in previousComputers)
            {
                previous.AssignedTo = null;
                previous.Status = WorkstationStatus.Available;
            }

            var created = computer is null;
            var previousAssignment = computer?.AssignedTo;
            if (computer is null)
            {
                computer = new Computer { LaboratoryStation = pcName };
                _db.Computers.Add(computer);
            }

            computer.AssignedTo = studentKey;
            computer.Status = WorkstationStatus.InUse;
            if (created)
                AddAudit(studentId, "WorkstationAutoCreated", $"Workstation {pcName} was automatically created for student {student.StudentNumber}.");
            if (previousComputers.Count > 0 || (!string.IsNullOrWhiteSpace(previousAssignment) && previousAssignment != studentKey))
                AddAudit(studentId, "WorkstationAutoMoved", $"Student {student.StudentNumber} was automatically moved to workstation {pcName}.");
            else if (!created && previousAssignment is null)
                AddAudit(studentId, "WorkstationAutoAssigned", $"Workstation {pcName} was automatically assigned to student {student.StudentNumber}.");

            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return computer;
        }
        catch (DbUpdateException ex)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("The workstation changed while login was being processed. Please try again.", ex);
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    public async Task<LabSession> EnsureStudentSessionAsync(
        int studentId,
        string pcName,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? transaction = null;
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction is null)
            transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // No lab, no new session. A student already in a session keeps it -
            // that is the reconnect path below - but nobody starts one until the
            // teacher opens the lab. Checked before the workstation is claimed,
            // so a refused sign-in leaves no trace on the computer record.
            if (_lab is { IsLabOpen: false } &&
                !await _db.LabSessions.AnyAsync(session => session.StudentId == studentId && session.IsActive && session.Status != LabSessionStatus.Ended, cancellationToken))
                throw new InvalidOperationException(NoLabMessage);

            var computer = await GetOrCreateForStudentAsync(studentId, pcName, cancellationToken);
            var existing = await _db.LabSessions.Include(session => session.Computer)
                .FirstOrDefaultAsync(session => session.StudentId == studentId && session.IsActive && session.Status != LabSessionStatus.Ended, cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.PCName, pcName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The student already has an active session on another workstation.");
                existing.PCName = pcName;
                existing.IPAddress = ipAddress;
                existing.ComputerId = computer.ComputerId;
                existing.Computer = computer;
                computer.Status = WorkstationStatus.InUse;
                await _db.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return existing;
            }

            var student = await _db.Students.Include(item => item.Class)
                .FirstAsync(item => item.Id == studentId, cancellationToken);
            var rule = await RuleForNewSessionAsync(cancellationToken);
            var session = new LabSession
            {
                StudentId = studentId,
                // A newcomer with no adviser or class belongs to the teacher running
                // the lab, so that teacher's restriction rules reach them too.
                TeacherId = student.AdviserId ?? student.Class?.TeacherId ?? _lab?.LabTeacherId,
                ComputerId = computer.ComputerId,
                Computer = computer,
                SessionRuleId = rule?.SessionRuleId,
                PCName = pcName,
                IPAddress = ipAddress,
                StartTime = DateTime.UtcNow,
                Status = LabSessionStatus.Running,
                IsActive = true,
                MaxDurationMinutes = rule?.MaxDurationMinutes
            };
            _db.LabSessions.Add(session);
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return session;
        }
        catch (DbUpdateException ex)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            await RecordRejectedAfterRollbackAsync(studentId, pcName, "The workstation was claimed by another login.", cancellationToken);
            throw new InvalidOperationException("The workstation was claimed by another login. Please try again.", ex);
        }
        catch (InvalidOperationException ex)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            await RecordRejectedAfterRollbackAsync(studentId, pcName, ex.Message, cancellationToken);
            throw;
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    private async Task RecordRejectedAfterRollbackAsync(
        int studentId,
        string pcName,
        string reason,
        CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        AddAudit(studentId, "WorkstationLoginRejected", $"Workstation {pcName}: {reason}");
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RejectAsync(int studentId, string pcName, string reason, CancellationToken cancellationToken)
    {
        AddAudit(studentId, "WorkstationLoginRejected", $"Workstation {pcName}: {reason}");
        await _db.SaveChangesAsync(cancellationToken);
    }

    private void AddAudit(int studentId, string action, string details)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserType = "Student",
            UserId = studentId,
            Action = action,
            Details = details,
            Timestamp = DateTime.UtcNow
        });
    }
}
