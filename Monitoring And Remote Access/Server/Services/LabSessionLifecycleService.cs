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
            if (!duration.HasValue || duration.Value <= 0 || GetElapsedSeconds(session, now) < duration.Value * 60) continue;
            End(session, now); ended++;
        }
        if (ended == 0) return 0;
        var closedRemoteSessions = await CloseRemoteSessionsAsync(sessions.Where(s => !s.IsActive), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await NotifyRemoteSessionsClosedAsync(closedRemoteSessions, cancellationToken);
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
        var closedRemoteSessions = await CloseRemoteSessionsAsync(new[] { session }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await NotifyRemoteSessionsClosedAsync(closedRemoteSessions, cancellationToken);
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
        var elapsed = GetElapsedSeconds(session, DateTime.UtcNow);
        return new GlobalSessionMessage(session.Status, elapsed, session.StartTime.ToUniversalTime());
    }

    public static int GetElapsedSeconds(Server.Models.LabSession session, DateTime now)
    {
        var effectiveNow = session.Status == "Paused" && session.PauseTime.HasValue
            ? session.PauseTime.Value.ToUniversalTime()
            : now.ToUniversalTime();
        return Math.Max(0, (int)(effectiveNow - session.StartTime.ToUniversalTime()).TotalSeconds - session.AccumulatedPauseSeconds);
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
            var assignedComputer = await _db.Computers
                .FirstOrDefaultAsync(c => c.AssignedTo == studentId.ToString(), cancellationToken);
            if (assignedComputer is null || !string.Equals(assignedComputer.LaboratoryStation, pcName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This workstation is not assigned to the student.");
            if (!string.IsNullOrWhiteSpace(existing.PCName) &&
                !string.Equals(existing.PCName, pcName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The student already has an active session on another workstation.");
            existing.PCName = pcName;
            existing.IPAddress = ipAddress;
            existing.ComputerId ??= assignedComputer.ComputerId;
            assignedComputer.Status = "In Use";
            await _db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var student = await _db.Students.Include(s => s.Class)
            .FirstAsync(s => s.Id == studentId, cancellationToken);
        var rule = await _db.SessionRules.FirstOrDefaultAsync(r => r.IsActive && r.IsDefault, cancellationToken);
        var computer = await _db.Computers.FirstOrDefaultAsync(c => c.AssignedTo == studentId.ToString(), cancellationToken);
        if (computer is null || !string.Equals(computer.LaboratoryStation, pcName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This workstation is not assigned to the student.");
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
        if (sessions.Count > 0)
        {
            var closedRemoteSessions = await CloseRemoteSessionsAsync(sessions, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyRemoteSessionsClosedAsync(closedRemoteSessions, cancellationToken);
        }
        return sessions.Count;
    }

    public async Task<int> EndStudentSessionsAndNotifyAsync(
        int studentId,
        string? pcName = null,
        CancellationToken cancellationToken = default)
    {
        var ended = await EndStudentSessionsAsync(studentId, pcName, cancellationToken);
        if (ended > 0)
            await _hub.Clients.User(studentId.ToString()).SendAsync(HubEventNames.SessionEnded, cancellationToken);
        return ended;
    }

    public async Task<int> EndTeacherSessionsAsync(int teacherId, CancellationToken cancellationToken = default)
    {
        var sessions = await _db.LabSessions.Include(s => s.Computer)
            .Where(s => s.TeacherId == teacherId && s.IsActive && s.Status != "Ended")
            .ToListAsync(cancellationToken);
        foreach (var session in sessions) End(session, DateTime.UtcNow);
        if (sessions.Count > 0)
        {
            var closedRemoteSessions = await CloseRemoteSessionsAsync(sessions, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyRemoteSessionsClosedAsync(closedRemoteSessions, cancellationToken);
            await _hub.Clients.Users(sessions.Select(s => s.StudentId.ToString()).Distinct().ToList())
                .SendAsync(HubEventNames.SessionEnded, cancellationToken);
        }
        return sessions.Count;
    }

    public async Task<int> PauseAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await _db.LabSessions.Include(s => s.SessionRule)
            .Where(s => s.IsActive && s.Status == "Running" &&
                (s.SessionRule == null || s.SessionRule.AllowPause))
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var session in sessions)
        {
            session.Status = "Paused";
            session.PauseTime = now;
        }
        if (sessions.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyStatesAsync(sessions, cancellationToken);
        }
        return sessions.Count;
    }

    public async Task<int> ResumeAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await _db.LabSessions
            .Where(s => s.IsActive && s.Status == "Paused")
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var session in sessions)
        {
            if (session.PauseTime.HasValue)
                session.AccumulatedPauseSeconds += Math.Max(0, (int)(now - session.PauseTime.Value).TotalSeconds);
            session.PauseTime = null;
            session.Status = "Running";
        }
        if (sessions.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyStatesAsync(sessions, cancellationToken);
        }
        return sessions.Count;
    }

    public async Task<int> EndAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await _db.LabSessions.Include(s => s.Computer)
            .Where(s => s.IsActive && s.Status != "Ended")
            .ToListAsync(cancellationToken);
        foreach (var session in sessions) End(session, DateTime.UtcNow);
        if (sessions.Count > 0)
        {
            var closedRemoteSessions = await CloseRemoteSessionsAsync(sessions, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyRemoteSessionsClosedAsync(closedRemoteSessions, cancellationToken);
            await _hub.Clients.Users(sessions.Select(s => s.StudentId.ToString()).Distinct().ToList())
                .SendAsync(HubEventNames.SessionEnded, cancellationToken);
        }
        return sessions.Count;
    }

    private async Task<List<Server.Models.RemoteControlSession>> CloseRemoteSessionsAsync(
        IEnumerable<Server.Models.LabSession> sessions,
        CancellationToken cancellationToken)
    {
        var sessionList = sessions.ToList();
        var studentIds = sessionList.Select(session => session.StudentId).Distinct().ToList();
        var studentNumbers = await _db.Students.AsNoTracking()
            .Where(student => studentIds.Contains(student.Id))
            .Select(student => student.StudentNumber)
            .ToListAsync(cancellationToken);
        var pcNames = sessionList.Select(s => s.PCName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToList();
        if (studentNumbers.Count == 0 && pcNames.Count == 0) return new List<Server.Models.RemoteControlSession>();
        var remoteSessions = await _db.RemoteControlSessions
            .Where(remote => remote.IsActive &&
                (studentNumbers.Contains(remote.StudentId) || pcNames.Contains(remote.PcName)))
            .ToListAsync(cancellationToken);
        foreach (var remote in remoteSessions)
        {
            remote.IsActive = false;
            remote.EndedAt = DateTime.UtcNow;
        }
        return remoteSessions;
    }

    private async Task NotifyRemoteSessionsClosedAsync(
        IEnumerable<Server.Models.RemoteControlSession> sessions,
        CancellationToken cancellationToken)
    {
        foreach (var remote in sessions.DistinctBy(session => session.ConnectionId))
            await _hub.Clients.Client(remote.ConnectionId).SendAsync(HubEventNames.RemoteControlState,
                new RemoteControlStateMessage(remote.StudentId, false, DateTime.UtcNow), cancellationToken);
    }

    private static void End(Server.Models.LabSession session, DateTime endedAt)
    {
        session.IsActive = false; session.Status = "Ended"; session.EndTime ??= endedAt;
        if (session.Computer is not null)
            session.Computer.Status = string.IsNullOrWhiteSpace(session.Computer.AssignedTo) ? "Available" : "Assigned";
    }
}
