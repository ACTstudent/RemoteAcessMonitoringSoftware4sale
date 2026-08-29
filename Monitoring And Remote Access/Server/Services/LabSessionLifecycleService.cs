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
            .Where(s => s.IsActive && (s.Status == "Running" || s.Status == "Paused"))
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
        await _hub.Clients.Group(HubEventNames.StudentsGroup).SendAsync(HubEventNames.SessionEnded, cancellationToken);
        return ended;
    }

    public async Task EndAsync(Server.Models.LabSession session, CancellationToken cancellationToken = default)
    {
        if (!session.IsActive && session.Status == "Ended") return;
        End(session, DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static void End(Server.Models.LabSession session, DateTime endedAt)
    {
        session.IsActive = false; session.Status = "Ended"; session.EndTime ??= endedAt;
        if (session.Computer is not null) { session.Computer.Status = "Available"; session.Computer.AssignedTo = null; }
    }
}
