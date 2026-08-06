using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Services;
using Shared.Contracts;

namespace Server.Hubs
{
    public class RemoteMonitoringHub : Hub
    {
        private readonly IMonitoringService _monitoringService;
        private readonly SessionManagerService _sessionManager;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ConcurrentDictionary<string, string> _lastLoggedApp = new();
        private static readonly ConcurrentDictionary<string, string> _connectionRoles = new();

        public RemoteMonitoringHub(
            IMonitoringService monitoringService,
            SessionManagerService sessionManager,
            IServiceScopeFactory scopeFactory)
        {
            _monitoringService = monitoringService;
            _sessionManager = sessionManager;
            _scopeFactory = scopeFactory;
        }

        private bool IsTeacher => _connectionRoles.TryGetValue(Context.ConnectionId, out var r) && r == "Teacher";

        private void RequireTeacher()
        {
            if (!IsTeacher) throw new HubException("Only teachers can perform this action.");
        }

        // Student Client sends a live screen frame
        public async Task SendScreenFrame(ScreenFrameMessage frame)
        {
            await Clients.Group(HubEventNames.TeachersGroup)
                .SendAsync(HubEventNames.ReceiveScreenFrame, Context.ConnectionId, frame);
        }

        // Student Client registers upon login
        public async Task RegisterStudent(string studentId, string pcName)
        {
            _connectionRoles[Context.ConnectionId] = "Student";
            var student = _monitoringService.RegisterStudent(Context.ConnectionId, studentId, pcName);
            await Groups.AddToGroupAsync(Context.ConnectionId, HubEventNames.StudentsGroup);
            await Clients.Group(HubEventNames.TeachersGroup)
                .SendAsync(HubEventNames.StudentConnected, student);

            // Push the current global session state so late-joining workstations sync up
            await Clients.Client(Context.ConnectionId)
                .SendAsync(HubEventNames.GlobalSessionState, _sessionManager.Snapshot());
        }

        // Teacher dashboard joins monitoring group
        public async Task RegisterTeacher()
        {
            _connectionRoles[Context.ConnectionId] = "Teacher";
            await Groups.AddToGroupAsync(Context.ConnectionId, HubEventNames.TeachersGroup);
            await Clients.Client(Context.ConnectionId)
                .SendAsync(HubEventNames.GlobalSessionState, _sessionManager.Snapshot());
        }

        // Teacher transmits mouse/keyboard event to a specific student connection
        public async Task SendRemoteInput(string targetConnectionId, RemoteInputMessage input)
        {
            await Clients.Client(targetConnectionId)
                .SendAsync(HubEventNames.ExecuteRemoteInput, input);
        }

        // ---- Teacher control commands ----

        public async Task LockStudent(string targetConnectionId)
        {
            RequireTeacher();
            await Clients.Client(targetConnectionId)
                .SendAsync(HubEventNames.LockStudent);
        }

        public async Task UnlockStudent(string targetConnectionId)
        {
            RequireTeacher();
            await Clients.Client(targetConnectionId)
                .SendAsync(HubEventNames.UnlockStudent);
        }

        public async Task ForceLogout(string targetConnectionId)
        {
            RequireTeacher();
            await Clients.Client(targetConnectionId)
                .SendAsync(HubEventNames.ForceLogout);
            _monitoringService.UnregisterStudent(targetConnectionId);
            await Clients.Group(HubEventNames.TeachersGroup)
                .SendAsync(HubEventNames.StudentDisconnected, targetConnectionId);
        }

        // Remotely shut down a student workstation
        public async Task ShutdownStudent(string targetConnectionId)
        {
            RequireTeacher();
            await Clients.Client(targetConnectionId)
                .SendAsync(HubEventNames.ShutdownStudent);
        }

        // Send a warning popup to one student (or all if target is empty)
        public async Task SendWarningPopup(string targetConnectionId, NotificationMessage warning)
        {
            RequireTeacher();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.Notifications.Add(new Notification { Type = "Warning", Title = warning.Title ?? "", Message = warning.Message ?? "" });
                await context.SaveChangesAsync();
            }
            catch { }

            if (string.IsNullOrWhiteSpace(targetConnectionId))
            {
                await Clients.Group(HubEventNames.StudentsGroup)
                    .SendAsync(HubEventNames.SendWarningPopup, warning);
            }
            else
            {
                await Clients.Client(targetConnectionId)
                    .SendAsync(HubEventNames.SendWarningPopup, warning);
            }
        }

        // Broadcast the current session's frame to all students (screen broadcast)
        public async Task BroadcastScreen(string frameBase64)
        {
            RequireTeacher();
            await Clients.Group(HubEventNames.StudentsGroup)
                .SendAsync(HubEventNames.BroadcastScreen, new BroadcastMessage(frameBase64, DateTime.Now));
        }

        public async Task SendNotification(NotificationMessage notification)
        {
            RequireTeacher();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.Notifications.Add(new Notification { Type = notification.Type ?? "Info", Title = notification.Title ?? "", Message = notification.Message ?? "", CreatedAt = notification.Timestamp == default ? DateTime.Now : notification.Timestamp });
                await context.SaveChangesAsync();
            }
            catch { }

            await Clients.Group(HubEventNames.StudentsGroup)
                .SendAsync(HubEventNames.SendNotification, notification);
        }

        // ---- Global session management (teacher) ----

        public async Task GlobalStartSession()
        {
            RequireTeacher();
            var existing = _sessionManager.Snapshot();
            if (existing.Status == "Ended") _sessionManager.StartSession();
            else _sessionManager.StartSession();
            await Task.CompletedTask;
        }

        public async Task GlobalPauseSession()
        {
            RequireTeacher();
            _sessionManager.PauseSession();
            await Task.CompletedTask;
        }

        public async Task GlobalEndSession()
        {
            RequireTeacher();
            _sessionManager.EndSession();
            await Task.CompletedTask;
        }

        // ---- Restriction enforcement (student client) ----

        // Student client pulls the active restriction rules on login
        public async Task FetchRestrictions()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var rules = await context.RestrictionRules
                .Where(r => r.IsActive)
                .OrderBy(r => r.RuleType)
                .Select(r => new RestrictionRuleMessage(r.RestrictionRuleId, r.RuleType, r.Target, r.Mode))
                .ToListAsync();

            await Clients.Client(Context.ConnectionId)
                .SendAsync(HubEventNames.RestrictionsReceived, rules);
        }

        // Student client reports a blocked-application / blocked-site attempt
        public async Task ReportInfraction(InfractionMessage infraction)
        {
            // Persist to the audit trail
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.AuditLogs.Add(new AuditLog
                {
                    UserType = "Student",
                    Action = "RestrictionViolation",
                    Details = $"{infraction.TargetType}: {infraction.Target} on {infraction.PcName}",
                    Timestamp = infraction.Timestamp
                });
                await context.SaveChangesAsync();
            }
            catch
            {
                // Audit persistence must never break the real-time alert
            }

            await Clients.Group(HubEventNames.TeachersGroup)
                .SendAsync(HubEventNames.InfractionDetected, infraction);
        }

        // ---- Student status reporting ----

        public async Task ReportIdleStatus(IdleStatusMessage status)
        {
            _monitoringService.ReportIdleStatus(status);
            await Clients.Group(HubEventNames.TeachersGroup)
                .SendAsync(HubEventNames.IdleStatusReceived, status);
        }

        public async Task ReportActiveApp(ActiveAppMessage app)
        {
            _monitoringService.ReportActiveApp(app);
            await Clients.Group(HubEventNames.TeachersGroup)
                .SendAsync(HubEventNames.ActiveAppReceived, app);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _connectionRoles.TryRemove(Context.ConnectionId, out _);
            var student = _monitoringService.UnregisterStudent(Context.ConnectionId);
            if (student != null)
            {
                await Clients.Group(HubEventNames.TeachersGroup)
                    .SendAsync(HubEventNames.StudentDisconnected, student.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
