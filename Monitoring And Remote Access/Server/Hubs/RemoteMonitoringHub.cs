using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using Server.Data;
using Server.Models;
using Server.Services;
using Shared.Contracts;

namespace Server.Hubs;

[Authorize]
public sealed class RemoteMonitoringHub : Hub
{
    private const int MaxFrameBase64Length = 6 * 1024 * 1024;

    private readonly IMonitoringService _monitoringService;
    private readonly ITelemetryService _telemetryService;
    private readonly SessionManagerService _sessionManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, int> _remoteSessions = new();

    public RemoteMonitoringHub(
        IMonitoringService monitoringService,
        ITelemetryService telemetryService,
        SessionManagerService sessionManager,
        IServiceScopeFactory scopeFactory)
    {
        _monitoringService = monitoringService;
        _telemetryService = telemetryService;
        _sessionManager = sessionManager;
        _scopeFactory = scopeFactory;
    }

    private bool IsTeacher => Context.User.IsInRole("Teacher") || Context.User.IsInRole("Admin");
    private bool IsStudent => Context.User.IsInRole("Student");
    private bool IsStudentClientAgent => IsStudent &&
        string.Equals(Context.User.FindFirst(AuthPrincipalFactory.ClientAgentClaim)?.Value, bool.TrueString, StringComparison.OrdinalIgnoreCase);

    private void RequireTeacher()
    {
        if (!IsTeacher)
            throw new HubException("Only teachers can perform this action.");
    }

    private StudentConnectionMessage RequireStudent()
    {
        if (!IsStudentClientAgent)
            throw new HubException("Only student clients can report workstation state.");

        var student = _monitoringService.FindStudent(Context.ConnectionId);
        if (student is null)
            throw new HubException("The student connection is not registered.");

        return student;
    }

    private StudentConnectionMessage RequireTarget(string targetConnectionId)
    {
        RequireTeacher();

        if (string.IsNullOrWhiteSpace(targetConnectionId))
            throw new HubException("A target workstation is required.");

        var target = _monitoringService.FindStudent(targetConnectionId);
        if (target is null)
            throw new HubException("The target workstation is not connected as a student.");

        return target;
    }

    private async Task<StudentConnectionMessage> RequireAuthorizedTargetAsync(string targetConnectionId)
    {
        var target = RequireTarget(targetConnectionId);
        if (Context.User.IsInRole("Admin"))
            return target;

        if (!int.TryParse(Context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var teacherId))
            throw new HubException("The teacher identity is invalid.");

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var student = await context.Students
            .Include(s => s.Class)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StudentNumber == target.StudentId);
        if (student is null || (student.AdviserId != teacherId && student.Class?.TeacherId != teacherId))
            throw new HubException("You are not authorized to control this workstation.");

        return target;
    }

    private async Task AuditCommandAsync(string action, StudentConnectionMessage target)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            int.TryParse(Context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var actorId);
            context.AuditLogs.Add(new AuditLog
            {
                UserType = Context.User.IsInRole("Admin") ? "Admin" : "Teacher",
                UserId = actorId == 0 ? null : actorId,
                Action = action,
                Details = $"{target.StudentId} at {target.PcName} ({target.ConnectionId})",
                Timestamp = DateTime.UtcNow
            });
            if (int.TryParse(Context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var teacherId))
            {
                _remoteSessions.TryGetValue(Context.ConnectionId, out var sessionId);
                context.RemoteCommandLogs.Add(new RemoteCommandLog
                {
                    RemoteControlSessionId = sessionId == 0 ? null : sessionId,
                    TeacherId = teacherId,
                    Command = action,
                    Details = $"{target.StudentId} at {target.PcName}"
                });
            }
            await context.SaveChangesAsync();
        }
        catch
        {
            // Auditing must not prevent delivery of an authorized command.
        }
    }

    private static async Task TryRecordTelemetryAsync(Func<Task> record)
    {
        try
        {
            await record();
        }
        catch
        {
            // Telemetry persistence must never interrupt live monitoring.
        }
    }

    private static void RequireFrame(string? frameBase64)
    {
        if (string.IsNullOrWhiteSpace(frameBase64) || frameBase64.Length > MaxFrameBase64Length)
            throw new HubException("The screen frame is empty or exceeds the maximum size.");
    }

    public override async Task OnConnectedAsync()
    {
        if (IsStudentClientAgent)
        {
            if (!int.TryParse(Context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var accountId))
            {
                Context.Abort();
                return;
            }

            var studentNumber = Context.User.FindFirst(AuthPrincipalFactory.StudentNumberClaim)?.Value;
            var pcName = Context.User.FindFirst(AuthPrincipalFactory.PcNameClaim)?.Value;
            if (string.IsNullOrWhiteSpace(studentNumber) || string.IsNullOrWhiteSpace(pcName))
            {
                Context.Abort();
                return;
            }

            var student = _monitoringService.RegisterStudent(Context.ConnectionId, studentNumber, pcName);
            await UpdateComputerProfileAsync(accountId, studentNumber, pcName);
            await Groups.AddToGroupAsync(Context.ConnectionId, HubEventNames.StudentsGroup);
            await Clients.Group(HubEventNames.TeachersGroup)
                .SendAsync(HubEventNames.StudentConnected, student);
            await Clients.Client(Context.ConnectionId)
                .SendAsync(HubEventNames.GlobalSessionState, _sessionManager.Snapshot());
        }
        else if (IsStudent || IsTeacher)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, IsTeacher ? HubEventNames.TeachersGroup : HubEventNames.StudentsGroup);
            await Clients.Client(Context.ConnectionId)
                .SendAsync(HubEventNames.GlobalSessionState, _sessionManager.Snapshot());
        }
        else
        {
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }

    private async Task UpdateComputerProfileAsync(int accountId, string studentNumber, string pcName)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var computer = await db.Computers.FirstOrDefaultAsync(c => c.LaboratoryStation == pcName);
            if (computer == null)
            {
                computer = new Computer
                {
                    LaboratoryStation = pcName,
                    Status = "Online",
                    AssignedTo = accountId.ToString()
                };
                db.Computers.Add(computer);
            }
            else
            {
                computer.Status = "Online";
                computer.AssignedTo = accountId.ToString();
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CAMS] Error auto-registering computer profile for {studentNumber}: {ex.Message}");
        }
    }

    public async Task SendScreenFrame(ScreenFrameMessage frame)
    {
        var student = RequireStudent();
        if (frame is null)
            throw new HubException("A screen frame is required.");
        RequireFrame(frame.FrameBase64);

        var canonicalFrame = new ScreenFrameMessage(
            student.StudentId,
            student.PcName,
            frame.FrameBase64,
            DateTime.UtcNow);

        await Clients.Group(HubEventNames.TeachersGroup)
            .SendAsync(HubEventNames.ReceiveScreenFrame, Context.ConnectionId, canonicalFrame);
    }

    public async Task SendWarningPopup(string targetConnectionId, NotificationMessage warning)
    {
        RequireTeacher();
        if (warning is null || warning.Title.Length > 120 || warning.Message.Length > 2000)
            throw new HubException("The warning message is invalid.");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Notifications.Add(new Notification
            {
                Type = "Warning",
                Title = warning.Title,
                Message = warning.Message,
                CreatedAt = warning.Timestamp == default ? DateTime.UtcNow : warning.Timestamp
            });
            await context.SaveChangesAsync();
        }
        catch
        {
            // Persistence must not prevent delivery to connected workstations.
        }

        if (string.IsNullOrWhiteSpace(targetConnectionId))
        {
            await Clients.Group(HubEventNames.StudentsGroup)
                .SendAsync(HubEventNames.SendWarningPopup, warning);
        }
        else
        {
            RequireTarget(targetConnectionId);
            await Clients.Client(targetConnectionId)
                .SendAsync(HubEventNames.SendWarningPopup, warning);
        }
    }

    public async Task LockStudent(string targetConnectionId)
    {
        var target = await RequireAuthorizedTargetAsync(targetConnectionId);
        await AuditCommandAsync("LockStudent", target);
        await Clients.Client(target.ConnectionId).SendAsync(HubEventNames.LockStudent);
    }

    public async Task UnlockStudent(string targetConnectionId)
    {
        var target = await RequireAuthorizedTargetAsync(targetConnectionId);
        await AuditCommandAsync("UnlockStudent", target);
        await Clients.Client(target.ConnectionId).SendAsync(HubEventNames.UnlockStudent);
    }

    public async Task ForceLogout(string targetConnectionId)
    {
        var target = await RequireAuthorizedTargetAsync(targetConnectionId);
        await AuditCommandAsync("ForceLogout", target);
        await Clients.Client(target.ConnectionId).SendAsync(HubEventNames.ForceLogout);
        _monitoringService.UnregisterStudent(target.ConnectionId);
        await Clients.Group(HubEventNames.TeachersGroup)
            .SendAsync(HubEventNames.StudentDisconnected, target.ConnectionId);
    }

    public async Task ShutdownStudent(string targetConnectionId)
    {
        var target = await RequireAuthorizedTargetAsync(targetConnectionId);
        await AuditCommandAsync("ShutdownStudent", target);
        await Clients.Client(target.ConnectionId).SendAsync(HubEventNames.ShutdownStudent);
    }

    public async Task RestartStudent(string targetConnectionId)
    {
        var target = await RequireAuthorizedTargetAsync(targetConnectionId);
        await AuditCommandAsync("RestartStudent", target);
        await Clients.Client(target.ConnectionId).SendAsync(HubEventNames.RestartStudent);
    }

    public async Task SendRemoteInput(string targetConnectionId, RemoteInputMessage input)
    {
        var target = await RequireAuthorizedTargetAsync(targetConnectionId);
        if (!_remoteSessions.ContainsKey(Context.ConnectionId))
            throw new HubException("Start an authorized remote-support session first.");
        if (input is null || string.IsNullOrWhiteSpace(input.EventType) || input.EventType.Length > 32 ||
            input.X < 0 || input.Y < 0)
            throw new HubException("Invalid remote input.");

        await Clients.Client(target.ConnectionId)
            .SendAsync(HubEventNames.ExecuteRemoteInput, input);
    }

    public async Task StartRemoteControl(string targetConnectionId)
    {
        var target = await RequireAuthorizedTargetAsync(targetConnectionId);
        if (!int.TryParse(Context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var teacherId))
            throw new HubException("The teacher identity is invalid.");

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = new RemoteControlSession
        {
            TeacherId = teacherId,
            StudentId = target.StudentId,
            PcName = target.PcName,
            ConnectionId = target.ConnectionId
        };
        context.RemoteControlSessions.Add(session);
        await context.SaveChangesAsync();
        _remoteSessions[Context.ConnectionId] = session.RemoteControlSessionId;
        await AuditCommandAsync("RemoteControlStarted", target);
    }

    public async Task StopRemoteControl(string targetConnectionId)
    {
        var target = await RequireAuthorizedTargetAsync(targetConnectionId);
        if (!_remoteSessions.TryRemove(Context.ConnectionId, out var sessionId))
            return;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await context.RemoteControlSessions.FindAsync(sessionId);
        if (session is not null)
        {
            session.IsActive = false;
            session.EndedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }
        await AuditCommandAsync("RemoteControlStopped", target);
    }

    public async Task BroadcastScreen(string frameBase64)
    {
        RequireTeacher();
        RequireFrame(frameBase64);
        await Clients.Group(HubEventNames.StudentsGroup)
            .SendAsync(HubEventNames.BroadcastScreen, new BroadcastMessage(frameBase64, DateTime.UtcNow));
    }

    public async Task SendNotification(NotificationMessage notification)
    {
        RequireTeacher();
        if (notification is null || notification.Title.Length > 120 || notification.Message.Length > 2000)
            throw new HubException("The notification is invalid.");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Notifications.Add(new Notification
            {
                Type = notification.Type ?? "Info",
                Title = notification.Title,
                Message = notification.Message,
                CreatedAt = notification.Timestamp == default ? DateTime.UtcNow : notification.Timestamp
            });
            await context.SaveChangesAsync();
        }
        catch
        {
            // Persistence must not prevent delivery to connected workstations.
        }

        await Clients.Group(HubEventNames.StudentsGroup)
            .SendAsync(HubEventNames.SendNotification, notification);
    }

    public Task GlobalStartSession()
    {
        RequireTeacher();
        _sessionManager.StartSession();
        return Task.CompletedTask;
    }

    public Task GlobalPauseSession()
    {
        RequireTeacher();
        _sessionManager.PauseSession();
        return Task.CompletedTask;
    }

    public Task GlobalEndSession()
    {
        RequireTeacher();
        _sessionManager.EndSession();
        return Task.CompletedTask;
    }

    public async Task FetchRestrictions()
    {
        RequireStudent();
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rules = await context.RestrictionRules
            .Where(r => r.IsActive)
            .OrderBy(r => r.RuleType)
            .Select(r => new RestrictionRuleMessage(r.RestrictionRuleId, r.RuleType, r.Target, r.Mode))
            .ToListAsync();

        var applicationCategories = await context.ApplicationCategories
            .Where(c => c.IsActive)
            .Select(c => new RestrictionRuleMessage(-c.ApplicationCategoryId, "Application", c.Pattern, c.Mode))
            .ToListAsync();
        var websiteCategories = await context.WebsiteCategories
            .Where(c => c.IsActive)
            .Select(c => new RestrictionRuleMessage(-c.WebsiteCategoryId, "Website", c.DomainPattern, c.Mode))
            .ToListAsync();
        rules.AddRange(applicationCategories);
        rules.AddRange(websiteCategories);

        await Clients.Client(Context.ConnectionId)
            .SendAsync(HubEventNames.RestrictionsReceived, rules);
    }

    public async Task ReportInfraction(InfractionMessage infraction)
    {
        var student = RequireStudent();
        if (infraction is null || string.IsNullOrWhiteSpace(infraction.Target) || string.IsNullOrWhiteSpace(infraction.TargetType) || infraction.Target.Length > 500 || infraction.TargetType.Length > 50)
            throw new HubException("The infraction report is invalid.");

        var canonicalInfraction = infraction with
        {
            ConnectionId = Context.ConnectionId,
            StudentId = student.StudentId,
            PcName = student.PcName,
            Timestamp = DateTime.UtcNow
        };

        await TryRecordTelemetryAsync(() => _telemetryService.RecordActivityEventAsync(
            canonicalInfraction.ConnectionId,
            canonicalInfraction.StudentId,
            canonicalInfraction.PcName,
            "RestrictionViolation",
            details: $"{canonicalInfraction.TargetType}: {canonicalInfraction.Target}",
            timestamp: canonicalInfraction.Timestamp));

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var alert = new MonitoringAlert
            {
                StudentId = canonicalInfraction.StudentId,
                PcName = canonicalInfraction.PcName,
                Severity = "Warning",
                Title = "Restricted activity detected",
                Message = $"{canonicalInfraction.TargetType}: {canonicalInfraction.Target}",
                CreatedAt = canonicalInfraction.Timestamp
            };
            context.MonitoringAlerts.Add(alert);
            context.AuditLogs.Add(new AuditLog
            {
                UserType = "Student",
                Action = "RestrictionViolation",
                Details = $"{canonicalInfraction.TargetType}: {canonicalInfraction.Target} on {canonicalInfraction.PcName}",
                Timestamp = canonicalInfraction.Timestamp
            });
            await context.SaveChangesAsync();
            await Clients.Group(HubEventNames.TeachersGroup)
                .SendAsync(HubEventNames.MonitoringAlertReceived, alert);
        }
        catch
        {
            // Audit persistence must never break the real-time alert.
        }

        await Clients.Group(HubEventNames.TeachersGroup)
            .SendAsync(HubEventNames.InfractionDetected, canonicalInfraction);
    }

    public async Task ReportIdleStatus(IdleStatusMessage status)
    {
        var student = RequireStudent();
        if (status is null)
            throw new HubException("The idle status report is invalid.");
        var canonicalStatus = status with
        {
            ConnectionId = Context.ConnectionId,
            StudentId = student.StudentId,
            PcName = student.PcName,
            Timestamp = DateTime.UtcNow
        };

        _monitoringService.ReportIdleStatus(canonicalStatus);
        await TryRecordTelemetryAsync(() => _telemetryService.RecordIdleStatusAsync(
            canonicalStatus.ConnectionId,
            canonicalStatus.StudentId,
            canonicalStatus.PcName,
            canonicalStatus.IsIdle,
            canonicalStatus.Timestamp));
        await Clients.Group(HubEventNames.TeachersGroup)
            .SendAsync(HubEventNames.IdleStatusReceived, canonicalStatus);
    }

    public async Task ReportActiveApp(ActiveAppMessage app)
    {
        var student = RequireStudent();
        if (app is null || string.IsNullOrWhiteSpace(app.ApplicationName) || app.ApplicationName.Length > 500)
            throw new HubException("The active application report is invalid.");

        var canonicalApp = app with
        {
            ConnectionId = Context.ConnectionId,
            StudentId = student.StudentId,
            PcName = student.PcName,
            Timestamp = DateTime.UtcNow
        };

        _monitoringService.ReportActiveApp(canonicalApp);
        await TryRecordTelemetryAsync(() => _telemetryService.RecordApplicationUsageAsync(
            canonicalApp.ConnectionId,
            canonicalApp.StudentId,
            canonicalApp.PcName,
            canonicalApp.ApplicationName,
            canonicalApp.Timestamp));
        await Clients.Group(HubEventNames.TeachersGroup)
            .SendAsync(HubEventNames.ActiveAppReceived, canonicalApp);
    }

    public async Task ReportWebsiteActivity(WebsiteActivityMessage website)
    {
        var student = RequireStudent();
        if (website is null || string.IsNullOrWhiteSpace(website.Domain) || website.Domain.Length > 300 ||
            string.IsNullOrWhiteSpace(website.Browser) || website.Browser.Length > 50)
            throw new HubException("The website activity report is invalid.");

        var canonical = website with
        {
            ConnectionId = Context.ConnectionId,
            StudentId = student.StudentId,
            PcName = student.PcName,
            Timestamp = DateTime.UtcNow
        };
        await TryRecordTelemetryAsync(() => _telemetryService.RecordWebsiteUsageAsync(
            canonical.ConnectionId, canonical.StudentId, canonical.PcName,
            canonical.Domain, canonical.Browser, canonical.Timestamp));
        await Clients.Group(HubEventNames.TeachersGroup)
            .SendAsync(HubEventNames.WebsiteActivityReceived, canonical);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_remoteSessions.TryRemove(Context.ConnectionId, out var sessionId))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var session = await context.RemoteControlSessions.FindAsync(sessionId);
                if (session is not null)
                {
                    session.IsActive = false;
                    session.EndedAt = DateTime.UtcNow;
                    await context.SaveChangesAsync();
                }
            }
            catch
            {
                // Connection cleanup must not block disconnect processing.
            }
        }

        var student = _monitoringService.UnregisterStudent(Context.ConnectionId);
        if (student != null)
        {
            await Clients.Group(HubEventNames.TeachersGroup)
                .SendAsync(HubEventNames.StudentDisconnected, student.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
