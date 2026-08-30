using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Hubs;
using Shared.Contracts;

namespace Server.Services;

public sealed class LabSessionLifecycleService
{
    private readonly ApplicationDbContext _db;
    private readonly IHubContext<RemoteMonitoringHub> _hub;

    public LabSessionLifecycleService(ApplicationDbContext db, IHubContext<RemoteMonitoringHub> hub)
    { _db = db; _hub = hub; }

    public async Task<int> EndExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var sessions = await _db.LabSessions.Include(s => s.SessionRule).Include(s => s.Computer)
            .Where(s => s.IsActive && s.Status == "Running")
            .ToListAsync(cancellationToken);
        var ended = 0;
        foreach (var session in sessions)
        {
            var duration = session.MaxDurationMinutes ?? session.SessionRule?.MaxDurationMinutes;
            if (duration is not > 0 || now < session.StartTime.ToUniversalTime().AddMinutes(duration.Value)) continue;
            End(session, now); ended++;
        }
        if (ended == 0) return 0;
        await _db.SaveChangesAsync(cancellationToken);
        await _hub.Clients.Users(sessions.Where(s => !s.IsActive).Select(s => s.StudentId.ToString()).Distinct().ToList())
            .SendAsync(HubEventNames.SessionEnded, cancellationToken);
        return ended;
    }

    public async Task EndAsync(Server.Models.LabSession session, CancellationToken cancellationToken = default)
    {
        if (!session.IsActive && session.Status == "Ended") return;
        if (session.ComputerId.HasValue && session.Computer is null)
            await _db.Entry(session).Reference(s => s.Computer).LoadAsync(cancellationToken);
        End(session, DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        await _hub.Clients.User(session.StudentId.ToString()).SendAsync(HubEventNames.SessionEnded, cancellationToken);
    }

    public Task NotifyStateAsync(Server.Models.LabSession session, CancellationToken cancellationToken = default) =>
        _hub.Clients.User(session.StudentId.ToString())
            .SendAsync(HubEventNames.GlobalSessionState, CreateState(session), cancellationToken);

    public async Task NotifyStatesAsync(IEnumerable<Server.Models.LabSession> sessions, CancellationToken cancellationToken = default)
    {
        foreach (var session in sessions)
            await NotifyStateAsync(session, cancellationToken);
    }

    public static GlobalSessionMessage CreateState(Server.Models.LabSession session)
    {
        var effectiveNow = session.Status == "Paused" && session.PauseTime.HasValue
            ? session.PauseTime.Value.ToUniversalTime()
            : DateTime.UtcNow;
        var elapsed = Math.Max(0, (int)(effectiveNow - session.StartTime.ToUniversalTime()).TotalSeconds);
        return new GlobalSessionMessage(session.Status, elapsed, session.StartTime.ToUniversalTime());
    }

    public async Task<Server.Models.LabSession> EnsureStudentSessionAsync(
        int studentId,
        string pcName,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.LabSessions.Include(s => s.Computer)
            .FirstOrDefaultAsync(s => s.StudentId == studentId && s.IsActive && s.Status != "Ended", cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.PCName, pcName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The student already has an active session on another workstation.");
            existing.IPAddress = ipAddress;
            await _db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var student = await _db.Students.Include(s => s.Class)
            .FirstAsync(s => s.Id == studentId, cancellationToken);
        var rule = await _db.SessionRules.FirstOrDefaultAsync(r => r.IsActive && r.IsDefault, cancellationToken);
        var computer = await _db.Computers.FirstOrDefaultAsync(c => c.AssignedTo == studentId.ToString(), cancellationToken)
            ?? await _db.Computers.FirstOrDefaultAsync(c => c.LaboratoryStation == pcName, cancellationToken);
        var session = new Server.Models.LabSession
        {
            StudentId = studentId,
            TeacherId = student.AdviserId ?? student.Class?.TeacherId,
            ComputerId = computer?.ComputerId,
            SessionRuleId = rule?.SessionRuleId,
            PCName = pcName,
            IPAddress = ipAddress,
            StartTime = DateTime.UtcNow,
            Status = "Running",
            IsActive = true,
            MaxDurationMinutes = rule?.MaxDurationMinutes
        };
        _db.LabSessions.Add(session);
        if (computer is not null)
        {
            computer.Status = "In Use";
            computer.AssignedTo = studentId.ToString();
        }
        await _db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<int> EndStudentSessionsAsync(
        int studentId,
        string? pcName = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.LabSessions.Include(s => s.Computer)
            .Where(s => s.StudentId == studentId && s.IsActive && s.Status != "Ended");
        if (!string.IsNullOrWhiteSpace(pcName))
            query = query.Where(s => s.PCName == pcName);
        var sessions = await query.ToListAsync(cancellationToken);
        foreach (var session in sessions) End(session, DateTime.UtcNow);
        if (sessions.Count > 0) await _db.SaveChangesAsync(cancellationToken);
        return sessions.Count;
    }

    public async Task<int> EndTeacherSessionsAsync(int teacherId, CancellationToken cancellationToken = default)
    {
        var sessions = await _db.LabSessions.Include(s => s.Computer)
            .Where(s => s.TeacherId == teacherId && s.IsActive && s.Status != "Ended")
            .ToListAsync(cancellationToken);
        foreach (var session in sessions) End(session, DateTime.UtcNow);
        if (sessions.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            await _hub.Clients.Users(sessions.Select(s => s.StudentId.ToString()).Distinct().ToList())
                .SendAsync(HubEventNames.SessionEnded, cancellationToken);
        }
        return sessions.Count;
    }

    private static void End(Server.Models.LabSession session, DateTime endedAt)
    {
        session.IsActive = false; session.Status = "Ended"; session.EndTime ??= endedAt;
        if (session.Computer is not null) { session.Computer.Status = "Available"; session.Computer.AssignedTo = null; }
    }
}
