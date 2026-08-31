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
    private const int MaxTelemetryBatchSize = 50;
    private static readonly ConcurrentDictionary<string, byte> ActiveTelemetryBatches = new();

    private readonly IMonitoringService _monitoringService;
    private readonly ITelemetryService _telemetryService;
    private readonly SessionManagerService _sessionManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly ConcurrentDictionary<string, int> RemoteSessions = new();

    private static string RemoteSessionKey(string teacherConnectionId, string studentConnectionId) =>
        $"{teacherConnectionId}\n{studentConnectionId}";

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

    private void RequireAdmin()
    {
        if (!Context.User.IsInRole("Admin"))
            throw new HubException("Only administrators can control the lab-wide session.");
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
        var authorized = await context.Students
            .AsNoTracking()
            .AnyAsync(s => s.StudentNumber == target.StudentId &&
                (s.AdviserId == teacherId || context.Classes.Any(c => c.TeacherId == teacherId && !c.IsArchived &&
                    (c.Status == "Active" || string.IsNullOrEmpty(c.Status)) &&
                    (c.ClassId == s.ClassId || context.ClassStudents.Any(cs => cs.ClassId == c.ClassId && cs.StudentId == s.Id)))));
        if (!authorized)
            throw new HubException("You are not authorized to control this workstation.");

        return target;
    }

    private async Task<string[]> ResolveViewerGroupsAsync(string studentNumber)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var student = await context.Students.AsNoTracking()
            .Where(s => s.StudentNumber == studentNumber)
            .Select(s => new { s.Id, s.AdviserId, s.ClassId })
            .FirstOrDefaultAsync();
        var teacherIds = new HashSet<int>();
        if (student?.AdviserId is int adviserId) teacherIds.Add(adviserId);
        if (student is not null)
        {
            var classTeachers = await context.Classes.AsNoTracking()
                .Where(c => c.TeacherId.HasValue && !c.IsArchived &&
                    (c.Status == "Active" || string.IsNullOrEmpty(c.Status)) &&
                    (c.ClassId == student.ClassId ||
                     context.ClassStudents.Any(cs => cs.ClassId == c.ClassId && cs.StudentId == student.Id)))
                .Select(c => c.TeacherId!.Value)
                .ToListAsync();
            teacherIds.UnionWith(classTeachers);
        }
        return teacherIds.Select(HubEventNames.TeacherGroup)
            .Append(HubEventNames.AdminsGroup)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<List<(int Id, string StudentNumber)>> AccessibleStudentRowsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var query = context.Students.AsNoTracking().AsQueryable();
        if (!Context.User.IsInRole("Admin"))
        {
            if (!int.TryParse(Context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var teacherId))
                throw new HubException("The teacher identity is invalid.");
            query = query.Where(s => s.AdviserId == teacherId ||
                context.Classes.Any(c => c.TeacherId == teacherId && !c.IsArchived &&
                    (c.Status == "Active" || string.IsNullOrEmpty(c.Status)) &&
                    (c.ClassId == s.ClassId || context.ClassStudents.Any(cs => cs.ClassId == c.ClassId && cs.StudentId == s.Id))));
        }
        var rows = await query.Select(s => new { s.Id, s.StudentNumber }).ToListAsync();
        return rows.Select(row => (row.Id, row.StudentNumber)).ToList();
    }

    private async Task<IClientProxy> AuthorizedViewersAsync(StudentConnectionMessage student) =>
        Clients.Groups(await ResolveViewerGroupsAsync(student.StudentId));

    private async Task AuditCommandAsync(string action, StudentConnectionMessage target, int? remoteSessionId = null)
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
                context.RemoteCommandLogs.Add(new RemoteCommandLog
                {
                    RemoteControlSessionId = remoteSessionId ?? (RemoteSessions.TryGetValue(
                        RemoteSessionKey(Context.ConnectionId, target.ConnectionId), out var sessionId) ? sessionId : null),
                    TeacherId = teacherId,
                    Command = action,
                    StudentId = target.StudentId,
                    PcName = target.PcName,
                    Timestamp = DateTime.UtcNow,
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

    private async Task<(StudentConnectionMessage Target, RemoteControlSession Session)> RequireActiveRemoteSessionAsync(string targetConnectionId)
    {
        var target = await RequireAuthorizedTargetAsync(targetConnectionId);
        if (!int.TryParse(Context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var teacherId))
            throw new HubException("The teacher identity is invalid.");

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var isAdmin = Context.User.IsInRole("Admin");
        var labSession = await context.LabSessions
            .Include(s => s.SessionRule)
            .Include(s => s.Student)
            .FirstOrDefaultAsync(s => s.IsActive &&
                (s.Status == "Running" || s.Status == "Paused") &&
                (isAdmin || s.TeacherId == teacherId) &&
                s.Student != null && s.Student.StudentNumber == target.StudentId);
        if (labSession is null)
            throw new HubException("The workstation has no active lab session.");
        if (labSession.SessionRule is not null && !labSession.SessionRule.AllowRemoteControl)
            throw new HubException("Remote control is disabled by the active session rule.");

        var duration = labSession.MaxDurationMinutes ?? labSession.SessionRule?.MaxDurationMinutes;
        if (duration is > 0 && LabSessionLifecycleService.GetElapsedSeconds(labSession, DateTime.UtcNow) >= duration.Value * 60)
        {
            labSession.IsActive = false;
            labSession.Status = "Ended";
            labSession.EndTime = DateTime.UtcNow;
            var expired = await context.RemoteControlSessions
                .Where(s => s.IsActive && s.TeacherId == teacherId && s.ConnectionId == target.ConnectionId)
                .ToListAsync();
            foreach (var remote in expired)
            {
                remote.IsActive = false;
                remote.EndedAt = DateTime.UtcNow;
            }
            await context.SaveChangesAsync();
            RemoteSessions.TryRemove(RemoteSessionKey(Context.ConnectionId, target.ConnectionId), out _);
            await Clients.Client(target.ConnectionId).SendAsync(HubEventNames.RemoteControlState,
                new RemoteControlStateMessage(target.StudentId, false, DateTime.UtcNow));
            throw new HubException("The lab session has expired.");
        }

        var sessionKey = RemoteSessionKey(Context.ConnectionId, target.ConnectionId);
        var sessionId = RemoteSessions.TryGetValue(sessionKey, out var mappedId) ? mappedId : 0;
        var session = sessionId > 0
            ? await context.RemoteControlSessions.FirstOrDefaultAsync(s => s.RemoteControlSessionId == sessionId &&
                s.IsActive && s.TeacherId == teacherId && s.ConnectionId == target.ConnectionId)
            : await context.RemoteControlSessions.FirstOrDefaultAsync(s => s.IsActive &&
                s.TeacherId == teacherId && s.ConnectionId == target.ConnectionId);
        if (session is null)
            throw new HubException("Start an authorized remote-support session first.");
        RemoteSessions[sessionKey] = session.RemoteControlSessionId;
        return (target, session);
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
            GlobalSessionMessage sessionState;
            using (var scope = _scopeFactory.CreateScope())
            {
                var lifecycle = scope.ServiceProvider.GetRequiredService<LabSessionLifecycleService>();
                var session = await lifecycle.EnsureStudentSessionAsync(
                    accountId,
                    pcName,
                    Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
                sessionState = LabSessionLifecycleService.CreateState(session);
            }
            await TryRecordTelemetryAsync(() => _telemetryService.RecordActivityEventAsync(Context.ConnectionId, student.StudentId, student.PcName, "Connected"));
            await Groups.AddToGroupAsync(Context.ConnectionId, HubEventNames.StudentsGroup);
            await (await AuthorizedViewersAsync(student))
                .SendAsync(HubEventNames.StudentConnected, student);
            await Clients.Client(Context.ConnectionId)
                .SendAsync(HubEventNames.GlobalSessionState, sessionState);
        }
        else if (IsStudent || IsTeacher)
        {
            if (IsTeacher)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, HubEventNames.TeachersGroup);
                if (Context.User.IsInRole("Admin"))
                    await Groups.AddToGroupAsync(Context.ConnectionId, HubEventNames.AdminsGroup);
                else if (int.TryParse(Context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var teacherId))
                    await Groups.AddToGroupAsync(Context.ConnectionId, HubEventNames.TeacherGroup(teacherId));
            }
            else
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, HubEventNames.StudentsGroup);
            }
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
            var computer = await db.Computers.FirstOrDefaultAsync(c =>
                c.LaboratoryStation == pcName && c.AssignedTo == accountId.ToString());
            if (computer == null) return;
            computer.Status = "In Use";

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

        await (await AuthorizedViewersAsync(student))
            .SendAsync(HubEventNames.ReceiveScreenFrame, Context.ConnectionId, canonicalFrame);
    }

    public async Task SendWarningPopup(string targetConnectionId, NotificationMessage warning)
    {
        RequireTeacher();
        if (warning is null || string.IsNullOrWhiteSpace(warning.Title) || string.IsNullOrWhiteSpace(warning.Message) ||
            warning.Title.Length > 120 || warning.Message.Length > 1000)
            throw new HubException("The warning message is invalid.");
        var canonical = warning with
        {
            Type = "Warning",
            Title = warning.Title.Trim(),
            Message = warning.Message.Trim(),
            Timestamp = DateTime.UtcNow
        };
        var accessible = await AccessibleStudentRowsAsync();
        var allowedNumbers = accessible.Select(row => row.StudentNumber).ToHashSet(StringComparer.Ordinal);
        var targets = string.IsNullOrWhiteSpace(targetConnectionId)
            ? _monitoringService.ActiveStudents.Where(student => allowedNumbers.Contains(student.StudentId)).ToList()
            : new List<StudentConnectionMessage> { await RequireAuthorizedTargetAsync(targetConnectionId) };
        if (!string.IsNullOrWhiteSpace(targetConnectionId))
            accessible = accessible.Where(row => row.StudentNumber == targets[0].StudentId).ToList();

        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Notifications.AddRange(accessible.Select(row => new Notification
            {
                StudentId = row.Id,
                Type = canonical.Type,
                Title = canonical.Title,
                Message = canonical.Message,
                CreatedAt = canonical.Timestamp
            }));
            await context.SaveChangesAsync();
        }
        foreach (var target in targets)
            await Clients.Client(target.ConnectionId).SendAsync(HubEventNames.SendWarningPopup, canonical);
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
        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var studentId = await context.Students.AsNoTracking()
                .Where(s => s.StudentNumber == target.StudentId)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync();
            if (studentId.HasValue)
            {
                var lifecycle = scope.ServiceProvider.GetRequiredService<LabSessionLifecycleService>();
                await lifecycle.EndStudentSessionsAsync(studentId.Value, target.PcName);
            }
        }
        await Clients.Client(target.ConnectionId).SendAsync(HubEventNames.ForceLogout);
        _monitoringService.UnregisterStudent(target.ConnectionId);
        await (await AuthorizedViewersAsync(target))
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

    public async Task BulkLockStudents(List<string> targetConnectionIds)
    {
        if (targetConnectionIds is null || targetConnectionIds.Count > 100)
            throw new HubException("The bulk command contains too many workstations.");
        foreach (var target in targetConnectionIds.Distinct(StringComparer.Ordinal))
            await LockStudent(target);
    }

    public async Task BulkForceLogoutStudents(List<string> targetConnectionIds)
    {
        if (targetConnectionIds is null || targetConnectionIds.Count > 100)
            throw new HubException("The bulk command contains too many workstations.");
        foreach (var target in targetConnectionIds.Distinct(StringComparer.Ordinal).ToList())
            await ForceLogout(target);
    }

    public async Task<RemoteCommandResult> SendRemoteInput(string targetConnectionId, RemoteInputMessage input)
    {
        var (target, session) = await RequireActiveRemoteSessionAsync(targetConnectionId);
        if (input is null || string.IsNullOrWhiteSpace(input.EventType) || input.EventType.Length > 32 ||
            input.X is < 0 or > 10000 || input.Y is < 0 or > 10000)
            throw new HubException("Invalid remote input.");

        await Clients.Client(target.ConnectionId)
            .SendAsync(HubEventNames.ExecuteRemoteInput, input);
        return new RemoteCommandResult(true, "Remote input delivered.", session.RemoteControlSessionId);
    }

    public async Task<RemoteCommandResult> StartRemoteControl(string targetConnectionId)
    {
        var target = await RequireAuthorizedTargetAsync(targetConnectionId);
        if (!int.TryParse(Context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var teacherId))
            throw new HubException("The teacher identity is invalid.");

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var isAdmin = Context.User.IsInRole("Admin");
        var labSession = await context.LabSessions.Include(s => s.SessionRule).Include(s => s.Student)
            .FirstOrDefaultAsync(s => s.IsActive &&
                (s.Status == "Running" || s.Status == "Paused") && s.Student != null &&
                (isAdmin || s.TeacherId == teacherId) &&
                s.Student.StudentNumber == target.StudentId);
        if (labSession is null)
            throw new HubException("The workstation has no active lab session.");
        if (labSession.SessionRule is not null && !labSession.SessionRule.AllowRemoteControl)
            throw new HubException("Remote control is disabled by the active session rule.");
        var duration = labSession.MaxDurationMinutes ?? labSession.SessionRule?.MaxDurationMinutes;
        if (duration is > 0 && LabSessionLifecycleService.GetElapsedSeconds(labSession, DateTime.UtcNow) >= duration.Value * 60)
        {
            labSession.IsActive = false;
            labSession.Status = "Ended";
            labSession.EndTime = DateTime.UtcNow;
            foreach (var old in await context.RemoteControlSessions.Where(s => s.IsActive && s.TeacherId == teacherId &&
                s.ConnectionId == target.ConnectionId).ToListAsync())
            {
                old.IsActive = false;
                old.EndedAt = DateTime.UtcNow;
            }
            await context.SaveChangesAsync();
            throw new HubException("The lab session has expired.");
        }
        var previous = await context.RemoteControlSessions.Where(s => s.IsActive && s.TeacherId == teacherId &&
            s.ConnectionId == target.ConnectionId).ToListAsync();
        foreach (var old in previous) { old.IsActive = false; old.EndedAt = DateTime.UtcNow; }
        var session = new RemoteControlSession
        {
            TeacherId = teacherId,
            StudentId = target.StudentId,
            PcName = target.PcName,
            ConnectionId = target.ConnectionId
        };
        context.RemoteControlSessions.Add(session);
        await context.SaveChangesAsync();
        RemoteSessions[RemoteSessionKey(Context.ConnectionId, target.ConnectionId)] = session.RemoteControlSessionId;
        await AuditCommandAsync("RemoteControlStarted", target);
        await Clients.Client(target.ConnectionId).SendAsync(HubEventNames.RemoteControlState,
            new RemoteControlStateMessage(target.StudentId, true, DateTime.UtcNow));
        return new RemoteCommandResult(true, "Remote support started.", session.RemoteControlSessionId);
    }

    public async Task<RemoteCommandResult> StopRemoteControl(string targetConnectionId)
    {
        var target = await RequireAuthorizedTargetAsync(targetConnectionId);
        if (!int.TryParse(Context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var teacherId))
            throw new HubException("The teacher identity is invalid.");
        if (!RemoteSessions.TryRemove(RemoteSessionKey(Context.ConnectionId, target.ConnectionId), out var sessionId))
        {
            using var lookupScope = _scopeFactory.CreateScope();
            var lookup = lookupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var existing = await lookup.RemoteControlSessions.FirstOrDefaultAsync(s => s.IsActive &&
                s.TeacherId == teacherId && s.ConnectionId == target.ConnectionId);
            if (existing is null) return new RemoteCommandResult(false, "No active remote-support session.", null);
            sessionId = existing.RemoteControlSessionId;
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await context.RemoteControlSessions.FirstOrDefaultAsync(s =>
            s.RemoteControlSessionId == sessionId && s.TeacherId == teacherId && s.ConnectionId == target.ConnectionId);
        if (session is not null)
        {
            session.IsActive = false;
            session.EndedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }
        await AuditCommandAsync("RemoteControlStopped", target, sessionId);
        await Clients.Client(target.ConnectionId).SendAsync(HubEventNames.RemoteControlState,
            new RemoteControlStateMessage(target.StudentId, false, DateTime.UtcNow));
        return new RemoteCommandResult(true, "Remote support stopped.", sessionId);
    }

    public async Task BroadcastScreen(string frameBase64)
    {
        RequireTeacher();
        RequireFrame(frameBase64);
        var accessible = await AccessibleStudentRowsAsync();
        var numbers = accessible.Select(row => row.StudentNumber).ToHashSet(StringComparer.Ordinal);
        var message = new BroadcastMessage(frameBase64, DateTime.UtcNow);
        foreach (var student in _monitoringService.ActiveStudents.Where(student => numbers.Contains(student.StudentId)))
            await Clients.Client(student.ConnectionId).SendAsync(HubEventNames.BroadcastScreen, message);
    }

    public async Task StopBroadcast()
    {
        RequireTeacher();
        var accessible = await AccessibleStudentRowsAsync();
        var numbers = accessible.Select(row => row.StudentNumber).ToHashSet(StringComparer.Ordinal);
        foreach (var student in _monitoringService.ActiveStudents.Where(student => numbers.Contains(student.StudentId)))
            await Clients.Client(student.ConnectionId).SendAsync(HubEventNames.BroadcastStopped);
    }

    public async Task SendNotification(NotificationMessage notification)
    {
        RequireTeacher();
        if (notification is null || notification.Title.Length > 120 || notification.Message.Length > 2000)
            throw new HubException("The notification is invalid.");

        var accessible = await AccessibleStudentRowsAsync();
        var studentIds = accessible.Select(row => row.Id).ToList();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Notifications.AddRange(studentIds.Select(studentId => new Notification
                {
                    StudentId = studentId,
                    Type = notification.Type ?? "Info",
                    Title = notification.Title,
                    Message = notification.Message,
                    CreatedAt = notification.Timestamp == default ? DateTime.UtcNow : notification.Timestamp
                }));
            await context.SaveChangesAsync();
        }
        catch
        {
            // Persistence must not prevent delivery to connected workstations.
        }

        var numbers = accessible.Select(row => row.StudentNumber).ToHashSet(StringComparer.Ordinal);
        foreach (var student in _monitoringService.ActiveStudents.Where(student => numbers.Contains(student.StudentId)))
            await Clients.Client(student.ConnectionId).SendAsync(HubEventNames.SendNotification, notification);
    }

    public async Task GlobalStartSession()
    {
        RequireAdmin();
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LabSessionLifecycleService>().ResumeAllSessionsAsync();
    }

    public async Task GlobalPauseSession()
    {
        RequireAdmin();
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LabSessionLifecycleService>().PauseAllSessionsAsync();
    }

    public async Task GlobalEndSession()
    {
        RequireAdmin();
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LabSessionLifecycleService>().EndAllSessionsAsync();
    }

    public async Task FetchRestrictions()
    {
        var connectedStudent = RequireStudent();
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var student = await context.Students.AsNoTracking()
            .Where(s => s.StudentNumber == connectedStudent.StudentId)
            .Select(s => new { s.Id, s.AdviserId, s.ClassId })
            .FirstOrDefaultAsync();
        var teacherIds = new HashSet<int>();
        if (student is not null)
        {
            var activeTeacherIds = await context.LabSessions.AsNoTracking()
                .Where(session => session.StudentId == student.Id && session.IsActive &&
                    session.Status != "Ended" && session.TeacherId.HasValue)
                .Select(session => session.TeacherId!.Value)
                .ToListAsync();
            teacherIds.UnionWith(activeTeacherIds);
        }

        var rules = await context.RestrictionRules
            .Where(r => r.IsActive && (r.IsGlobal || (r.TeacherId.HasValue && teacherIds.Contains(r.TeacherId.Value))))
            .OrderBy(r => r.RuleType)
            .Select(r => new RestrictionRuleMessage(r.RestrictionRuleId, r.RuleType, r.Target, r.Mode))
            .ToListAsync();

        var blacklist = await context.BlacklistItems.Where(b => b.IsActive).ToListAsync();
        rules.AddRange(blacklist.Select(b => new RestrictionRuleMessage(
            -b.BlacklistItemId,
            b.TargetType is "Domain" ? "Website" : b.TargetType is "Process" ? "Application" : b.TargetType,
            b.Value, "Block")));

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
        var canonicalInfraction = CanonicalizeInfraction(student, infraction, useReportedTimestamp: false);

        await TryRecordTelemetryAsync(() => _telemetryService.RecordActivityEventAsync(
            canonicalInfraction.ConnectionId,
            canonicalInfraction.StudentId,
            canonicalInfraction.PcName,
            "RestrictionViolation",
            details: $"{canonicalInfraction.TargetType}: {canonicalInfraction.Target}",
            timestamp: canonicalInfraction.Timestamp));

        await PersistAndPublishInfractionAsync(student, canonicalInfraction);
    }

    private async Task PersistAndPublishInfractionAsync(StudentConnectionMessage student, InfractionMessage canonicalInfraction)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dedupeKey = $"{canonicalInfraction.TargetType}:{canonicalInfraction.Target}".ToLowerInvariant();
            var groupKey = MonitoringAlert.CreateGroupKey(
                canonicalInfraction.StudentId,
                canonicalInfraction.PcName,
                dedupeKey,
                "Restricted activity detected");
            var alert = new MonitoringAlert
            {
                StudentId = canonicalInfraction.StudentId,
                PcName = canonicalInfraction.PcName,
                Severity = "Warning",
                Title = "Restricted activity detected",
                Message = $"{canonicalInfraction.TargetType}: {canonicalInfraction.Target}",
                DedupeKey = dedupeKey,
                GroupKey = groupKey,
                OccurrenceCount = 1,
                FirstSeenAt = canonicalInfraction.Timestamp,
                LastSeenAt = canonicalInfraction.Timestamp,
                CreatedAt = canonicalInfraction.Timestamp
            };
            var existing = await context.MonitoringAlerts
                .Where(a => a.GroupKey == groupKey && !a.IsAcknowledged && a.DismissedAt == null)
                .OrderByDescending(a => a.LastSeenAt)
                .FirstOrDefaultAsync();
            var suppressNotification = existing is not null &&
                existing.LastSeenAt >= canonicalInfraction.Timestamp.AddMinutes(-5);
            if (existing is null)
            {
                context.MonitoringAlerts.Add(alert);
            }
            else
            {
                existing.OccurrenceCount = Math.Max(1, existing.OccurrenceCount) + 1;
                existing.LastSeenAt = canonicalInfraction.Timestamp;
                existing.Message = alert.Message;
                alert = existing;
            }
            context.AuditLogs.Add(new AuditLog
            {
                UserType = "Student",
                Action = "RestrictionViolation",
                Details = $"{canonicalInfraction.TargetType}: {canonicalInfraction.Target} on {canonicalInfraction.PcName}",
                Timestamp = canonicalInfraction.Timestamp
            });
            await context.SaveChangesAsync();
            if (!suppressNotification)
            {
                await (await AuthorizedViewersAsync(student))
                    .SendAsync(HubEventNames.MonitoringAlertReceived, alert);
            }
        }
        catch
        {
            // Audit persistence must never break the real-time alert.
        }

        await (await AuthorizedViewersAsync(student))
            .SendAsync(HubEventNames.InfractionDetected, canonicalInfraction);
    }

    private InfractionMessage CanonicalizeInfraction(
        StudentConnectionMessage student,
        InfractionMessage? infraction,
        bool useReportedTimestamp)
    {
        if (infraction is null || string.IsNullOrWhiteSpace(infraction.Target) ||
            string.IsNullOrWhiteSpace(infraction.TargetType) || infraction.Target.Length > 500 ||
            infraction.TargetType.Length > 50 || infraction.Target.Any(char.IsControl) ||
            infraction.TargetType.Any(char.IsControl))
            throw new HubException("The infraction report is invalid.");
        return infraction with
        {
            ConnectionId = Context.ConnectionId,
            StudentId = student.StudentId,
            PcName = student.PcName,
            Target = infraction.Target.Trim(),
            TargetType = infraction.TargetType.Trim(),
            Timestamp = useReportedTimestamp ? infraction.Timestamp : DateTime.UtcNow
        };
    }

    public async Task ReportIdleStatus(IdleStatusMessage status)
    {
        var student = RequireStudent();
        var canonicalStatus = CanonicalizeIdleStatus(student, status);

        await TryRecordTelemetryAsync(() => _telemetryService.RecordIdleStatusAsync(
            canonicalStatus.ConnectionId,
            canonicalStatus.StudentId,
            canonicalStatus.PcName,
            canonicalStatus.IsIdle,
            canonicalStatus.Timestamp));
        await PublishIdleStatusAsync(canonicalStatus);
    }

    public async Task ReportActiveApp(ActiveAppMessage app)
    {
        var student = RequireStudent();
        var canonicalApp = CanonicalizeActiveApp(student, app);

        await TryRecordTelemetryAsync(() => _telemetryService.RecordApplicationUsageAsync(
            canonicalApp.ConnectionId,
            canonicalApp.StudentId,
            canonicalApp.PcName,
            canonicalApp.ApplicationName,
            canonicalApp.Timestamp));
        await PublishActiveAppAsync(canonicalApp);
    }

    public async Task ReportWebsiteActivity(WebsiteActivityMessage website)
    {
        var student = RequireStudent();
        var canonical = CanonicalizeWebsiteActivity(student, website);
        await TryRecordTelemetryAsync(() => _telemetryService.RecordWebsiteUsageAsync(
            canonical.ConnectionId, canonical.StudentId, canonical.PcName,
            canonical.Domain, canonical.Browser, canonical.Timestamp));
        await PublishWebsiteActivityAsync(canonical);
    }

    public async Task ReportBrowserMonitoringStatus(BrowserMonitoringStatusMessage status)
    {
        var student = RequireStudent();
        var canonical = CanonicalizeBrowserMonitoringStatus(student, status);
        await TryRecordTelemetryAsync(() => _telemetryService.RecordBrowserMonitoringStatusAsync(canonical));
        await PublishBrowserMonitoringStatusAsync(canonical);
    }

    public async Task<TelemetryBatchResult> ReportTelemetryBatch(TelemetryBatchMessage batch)
    {
        var student = RequireStudent();
        if (batch?.Items is null || batch.Items.Count is 0 or > MaxTelemetryBatchSize)
            throw new HubException($"Telemetry batches must contain between 1 and {MaxTelemetryBatchSize} items.");
        if (!ActiveTelemetryBatches.TryAdd(Context.ConnectionId, 0))
            throw new HubException("A telemetry batch is already being processed; retry after backpressure clears.");

        try
        {
            var canonicalItems = new List<TelemetryBatchItem>(batch.Items.Count);
            foreach (var item in batch.Items)
            {
                if (item is null || item.PayloadCount != 1)
                    throw new HubException("Each telemetry batch item must contain exactly one payload.");

                if (item.IdleStatus is { } idle)
                    canonicalItems.Add(TelemetryBatchItem.From(CanonicalizeIdleStatus(student, idle)));
                else if (item.ActiveApp is { } app)
                    canonicalItems.Add(TelemetryBatchItem.From(CanonicalizeActiveApp(student, app)));
                else if (item.WebsiteActivity is { } website)
                    canonicalItems.Add(TelemetryBatchItem.From(CanonicalizeWebsiteActivity(student, website)));
                else if (item.BrowserMonitoringStatus is { } browserStatus)
                    canonicalItems.Add(TelemetryBatchItem.From(CanonicalizeBrowserMonitoringStatus(student, browserStatus)));
                else if (item.Infraction is { } infraction)
                    canonicalItems.Add(TelemetryBatchItem.From(CanonicalizeInfraction(student, infraction, useReportedTimestamp: true)));
            }

            // Durable clients acknowledge a batch only after SQLite commits it successfully.
            await _telemetryService.RecordBatchAsync(canonicalItems);
            foreach (var item in canonicalItems)
            {
                if (item.IdleStatus is { } idle)
                    await PublishIdleStatusAsync(idle);
                else if (item.ActiveApp is { } app)
                    await PublishActiveAppAsync(app);
                else if (item.WebsiteActivity is { } website)
                    await PublishWebsiteActivityAsync(website);
                else if (item.BrowserMonitoringStatus is { } browserStatus)
                    await PublishBrowserMonitoringStatusAsync(browserStatus);
                else if (item.Infraction is { } infraction)
                    await PersistAndPublishInfractionAsync(student, infraction);
            }

            return new TelemetryBatchResult(canonicalItems.Count);
        }
        finally
        {
            ActiveTelemetryBatches.TryRemove(Context.ConnectionId, out _);
        }
    }

    private IdleStatusMessage CanonicalizeIdleStatus(StudentConnectionMessage student, IdleStatusMessage? status)
    {
        if (status is null)
            throw new HubException("The idle status report is invalid.");
        return status with
        {
            ConnectionId = Context.ConnectionId,
            StudentId = student.StudentId,
            PcName = student.PcName
        };
    }

    private ActiveAppMessage CanonicalizeActiveApp(StudentConnectionMessage student, ActiveAppMessage? app)
    {
        if (app is null || !TelemetryValueNormalizer.TryNormalizeApplicationName(app.ApplicationName, out var applicationName))
            throw new HubException("The active application report is invalid.");
        return app with
        {
            ConnectionId = Context.ConnectionId,
            StudentId = student.StudentId,
            PcName = student.PcName,
            ApplicationName = applicationName
        };
    }

    private WebsiteActivityMessage CanonicalizeWebsiteActivity(StudentConnectionMessage student, WebsiteActivityMessage? website)
    {
        if (website is null || !WebsiteDomainNormalizer.TryNormalize(website.Domain, out var domain) || domain.Length > 300 ||
            string.IsNullOrWhiteSpace(website.Browser) || website.Browser.Length > 50 || website.Browser.Any(char.IsControl))
            throw new HubException("The website activity report is invalid.");
        return website with
        {
            ConnectionId = Context.ConnectionId,
            StudentId = student.StudentId,
            PcName = student.PcName,
            Domain = domain,
            Browser = website.Browser.Trim().ToLowerInvariant()
        };
    }

    private BrowserMonitoringStatusMessage CanonicalizeBrowserMonitoringStatus(
        StudentConnectionMessage student,
        BrowserMonitoringStatusMessage? status)
    {
        if (status is null || string.IsNullOrWhiteSpace(status.Browser) || status.Browser.Length > 50 ||
            status.Browser.Any(char.IsControl) || !Enum.IsDefined(status.Mode))
            throw new HubException("The browser monitoring status is invalid.");
        return status with
        {
            ConnectionId = Context.ConnectionId,
            StudentId = student.StudentId,
            PcName = student.PcName,
            Browser = status.Browser.Trim().ToLowerInvariant(),
            Detail = BrowserMonitoringStatusMessage.NormalizeDetail(status.Detail)
        };
    }

    private async Task PublishIdleStatusAsync(IdleStatusMessage status)
    {
        _monitoringService.ReportIdleStatus(status);
        var student = _monitoringService.FindStudent(status.ConnectionId);
        if (student is null) return;
        await (await AuthorizedViewersAsync(student))
            .SendAsync(HubEventNames.IdleStatusReceived, status);
    }

    private async Task PublishActiveAppAsync(ActiveAppMessage app)
    {
        _monitoringService.ReportActiveApp(app);
        var student = _monitoringService.FindStudent(app.ConnectionId);
        if (student is null) return;
        await (await AuthorizedViewersAsync(student))
            .SendAsync(HubEventNames.ActiveAppReceived, app);
    }

    private async Task PublishWebsiteActivityAsync(WebsiteActivityMessage website)
    {
        var student = _monitoringService.FindStudent(website.ConnectionId);
        if (student is null) return;
        await (await AuthorizedViewersAsync(student))
            .SendAsync(HubEventNames.WebsiteActivityReceived, website);
    }

    private async Task PublishBrowserMonitoringStatusAsync(BrowserMonitoringStatusMessage status)
    {
        _monitoringService.ReportBrowserMonitoringStatus(status);
        var student = _monitoringService.FindStudent(status.ConnectionId);
        if (student is null) return;
        await (await AuthorizedViewersAsync(student))
            .SendAsync(HubEventNames.BrowserMonitoringStatusReceived, status);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        ActiveTelemetryBatches.TryRemove(Context.ConnectionId, out _);
        var remoteSessions = RemoteSessions
            .Where(item => item.Key.StartsWith($"{Context.ConnectionId}\n", StringComparison.Ordinal))
            .Select(item => new
            {
                StudentConnectionId = item.Key[(item.Key.IndexOf('\n') + 1)..],
                SessionId = item.Value
            })
            .ToList();
        foreach (var key in RemoteSessions.Keys.Where(key => key.StartsWith($"{Context.ConnectionId}\n", StringComparison.Ordinal)))
            RemoteSessions.TryRemove(key, out _);
        if (remoteSessions.Count > 0)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var remoteSessionIds = remoteSessions.Select(item => item.SessionId).Distinct().ToList();
                var sessions = await context.RemoteControlSessions
                    .Where(session => remoteSessionIds.Contains(session.RemoteControlSessionId))
                    .ToListAsync();
                foreach (var session in sessions)
                {
                    session.IsActive = false;
                    session.EndedAt = DateTime.UtcNow;
                }
                await context.SaveChangesAsync();
                foreach (var remote in remoteSessions.DistinctBy(item => item.StudentConnectionId))
                {
                    var session = sessions.FirstOrDefault(item => item.RemoteControlSessionId == remote.SessionId);
                    if (session is not null)
                        await Clients.Client(remote.StudentConnectionId).SendAsync(HubEventNames.RemoteControlState,
                            new RemoteControlStateMessage(session.StudentId, false, DateTime.UtcNow));
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
            await TryRecordTelemetryAsync(() => _telemetryService.RecordDisconnectedAsync(
                Context.ConnectionId, student.StudentId, student.PcName, exception?.Message));
            await (await AuthorizedViewersAsync(student))
                .SendAsync(HubEventNames.StudentDisconnected, student.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
