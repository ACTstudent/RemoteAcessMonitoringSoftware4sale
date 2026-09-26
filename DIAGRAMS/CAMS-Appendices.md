# CAMS Appendices

Appendix B, C and D for **CAMS: Computer Account Management System for Pardo Elementary School** — the flow of the system, the application prototype, and sample program code.

Every screen in Appendix C is a photograph of the system running, not a mock-up. Every listing in Appendix D is copied from the repository, not written for the document.

---

## Appendix B. Flow of the System

### Proposed Flow of the System

The proposed flow of CAMS begins before any user signs in, at the point where the software reaches the school. An administrator downloads the server installer from the public portal, verifies it against its published SHA-256 checksum, and installs it on the laboratory control computer. On first start the server creates its local database, generates its own certificate authority, and asks for the credentials of the first administrator account. No password ships with the product, so the school owns the only administrative credential from the first minute.

Once the server is running, the flow divides at the front door according to who is arriving. A teacher or an administrator reaches the system through a browser on the laboratory network and signs in at the web portal. A student never does. The portal refuses a student account at the login screen and directs them to the CAMS client installed on their workstation, so a pupil can reach the system only from the machine they are sitting at, and only while that machine is registered. This is a deliberate boundary rather than an oversight: it means a student cannot browse to the server from any other computer on the network.

An administrator signing in arrives at the global operations dashboard, which holds a setup checklist while anything is outstanding and disappears once the laboratory is configured. From there the administrator follows one of four management pathways. The people pathway creates and maintains teacher, student and administrator accounts, and organises students into classes with a grade level, section, subject and schedule. The laboratory pathway registers each workstation, maps it to a student, and keeps the history of every status change. The policy pathway defines what may run and what may not: restriction rules, blacklist and whitelist directories, application and website categories, and the session rules that govern how long a laboratory session may last and whether it may be paused or remotely controlled. The administration pathway covers the operations that keep the installation healthy — role definitions, LAN status, the deployment hub that publishes the client installer, database backup and restore, and the reporting and audit surfaces.

A teacher signing in reaches a classroom dashboard scoped to the classes they advise, and works along a narrower path. The teacher starts a laboratory session against a session rule, which fixes its duration and its permissions. Each student workstation running the CAMS client then appears on the live monitoring wall as a card carrying the current screen, the signed-in pupil, the active application, and the remaining time. From that wall the teacher may lock a workstation, unlock it, sign a student out, shut a machine down, restart it, take remote control of it, broadcast one screen to the room, or send a notification or warning that appears on top of whatever the child is doing. Every one of those commands is written to the remote command log before it is sent, so the record of what was done to a workstation exists independently of the workstation.

The student pathway runs entirely inside the client. The pupil signs in at the workstation with their student number and password. The client authenticates over HTTPS against the server, binds the sign-in to the machine name, and opens a SignalR connection. From that point the client reports continuously: the active application, the website in the foreground browser, whether the machine has gone idle, and a periodic batch of telemetry. It also fetches the restriction rules that apply and enforces them locally, closing a blocked website and reporting the infraction. When the teacher's commands arrive they are applied on the workstation — the lock screen appears, the warning is displayed, the machine restarts. The pupil may open the client window from the notification area, check the connection and remaining session time, or exit the client, which signs the session out first so the workstation is released rather than left showing an occupant who has gone.

The flow closes where it began, in the database. Every session, command, infraction, telemetry record and administrative action is persisted as it happens, so the reports, the classroom records, the analytics and the audit trail are all reading the same history rather than a separate log. Ending a session releases its workstation, writes its duration, and returns the laboratory to the state the next class will find it in.

### Figure B.1: Flow of the system

```mermaid
flowchart TD
    A[Administrator installs CAMS Server] --> B[First start: create database, certificate, first admin]
    B --> C{Who is signing in?}

    C -- Administrator or Teacher --> D[Web portal sign in]
    C -- Student --> E[Refused at the portal]
    E --> F[CAMS client on the workstation]

    D --> G{Role}
    G -- Administrator --> H[Global operations dashboard]
    G -- Teacher --> I[Classroom dashboard]

    H --> H1[People: teachers, students, classes]
    H --> H2[Laboratory: workstations, mapping, history]
    H --> H3[Policies: restrictions, lists, session rules]
    H --> H4[Administration: roles, LAN, deployment, database, reports, audit]

    I --> J[Start laboratory session against a session rule]
    J --> K[Live monitoring wall]
    K --> L[Remote commands: lock, unlock, sign out, shut down, restart, control, broadcast, notify]
    L --> M[(Remote command log)]

    F --> N[Authenticate workstation over HTTPS]
    N --> O[Open SignalR connection]
    O --> P[Report application, website, idle state, telemetry]
    O --> Q[Fetch and enforce restriction rules]
    Q --> R{Blocked target reached?}
    R -- Yes --> S[Close it and report the infraction]
    R -- No --> P
    L --> T[Command applied on the workstation]

    P --> U[(Central database)]
    S --> U
    M --> U
    U --> V[Reports, classroom records, analytics, audit trail]

    J --> W{Session ends}
    W --> X[Release the workstation and write the duration]
    X --> U
```

---

## Appendix C. Application Prototype

This appendix presents the CAMS interface as it runs, showing the sign-in screen and each administrative surface. The screens were photographed from a running server against seeded records, so the layout, navigation and controls are the real ones rather than a drawing of them.

The sidebar groups every destination under five headings — Dashboard, People, Computers, Policies and Administrator Only — and collapses to an icon rail on narrow screens.

### Sign in

![Portal sign in](prototype-images/login.png)

**Figure C.1: Portal sign in.** The single front door for teachers and administrators. A student account entered here is refused and told to use the workstation client instead.

### Dashboard

![Administrator dashboard](prototype-images/admin-dashboard.png)

**Figure C.2: Administrator dashboard.** The setup checklist runs in two columns while anything is outstanding and disappears once the laboratory is configured. Below it sit the lab-wide session controls, the four counters, and the management cards.

### People

![Teacher management](prototype-images/admin-teachers.png)

**Figure C.3: Teacher management.** Each counter carries a line saying what it counts. Status reads as a tinted chip with a dot, and the row actions show no outline until they are reached for.

![Student management](prototype-images/admin-students.png)

**Figure C.4: Student management.** Student profiles with their workstation assignments, searchable and filterable by status.

![Class management](prototype-images/admin-classes.png)

**Figure C.5: Class management.** Classes are cards rather than rows, each carrying its academic year, adviser, subject and enrolment count. The directory scrolls in place rather than paging.

### Laboratory

![Computer profiles](prototype-images/admin-computers.png)

**Figure C.6: Computer profiles.** Every registered workstation with its station name, status and mapped student. A workstation may be archived, which keeps its history, or deleted outright, which does not.

### Policies

![Whitelist and blacklist policies](prototype-images/admin-restrictions.png)

**Figure C.7: Whitelist and blacklist security policies.** Restriction rules and the application and website categories they draw on.

![Whitelist directory](prototype-images/admin-whitelist.png)

**Figure C.8: Whitelist directory.** Allow rules, which are exceptions that take precedence over matching categories and blacklist entries.

![Session policy rules](prototype-images/admin-sessionrules.png)

**Figure C.9: Session policy rules.** The rules that fix how long a laboratory session runs and whether it may be paused or remotely controlled. Deactivating a rule keeps the sessions already recorded against it.

### Administration

![Account security](prototype-images/admin-accounts.png)

**Figure C.10: Account security.** Administrator accounts, lockout state, and password resets.

![Roles and permissions](prototype-images/admin-roles.png)

**Figure C.11: Roles and permissions.** The role definitions the authorisation surface reads.

![LAN operational status](prototype-images/admin-lan.png)

**Figure C.12: LAN operational status.** The endpoint the server is actually answering on, the SignalR path, and the discovery service. Status only; nothing here changes a network setting.

![Deployment hub](prototype-images/admin-deployment.png)

**Figure C.13: Deployment hub.** Where the client installer, its checksum and the local root certificate are published to the school's own administrators.

![Database maintenance](prototype-images/admin-database.png)

**Figure C.14: Database maintenance.** Database health, online backups, and restart-safe restore staging.

### Reporting

![Analytics and reports](prototype-images/admin-reports.png)

**Figure C.15: Administrative analytics and reports.** Attendance, usage and remote command exports over a chosen date range.

![System audit trail](prototype-images/admin-audit.png)

**Figure C.16: System audit trail.** Who did what, grouped by the account that did it.

![System and exception logs](prototype-images/admin-logs.png)

**Figure C.17: System and exception logs.** Server diagnostic trace, runtime exceptions and warnings.

### Teacher portal

The teacher works along a narrower path than the administrator, scoped to the classes they advise. These screens were photographed while signed in as a teacher account, so what is shown is what a teacher actually sees.

![Teacher classroom dashboard](prototype-images/teacher-dashboard.png)

**Figure C.18: Teacher classroom dashboard.** Scoped to the classes the teacher advises. The counters are live: running sessions, connected students, and the hub connection state.

![Laboratory sessions](prototype-images/teacher-sessions.png)

**Figure C.19: Laboratory sessions.** Opening a session against a session rule fixes its duration and whether it may be paused or remotely controlled.

![Live monitoring wall](prototype-images/teacher-monitoring.png)

**Figure C.20: Live monitoring wall.** One card per connected workstation, carrying the current screen, the signed-in pupil, the active application and the remaining time.

![Remote command history](prototype-images/teacher-remotehistory.png)

**Figure C.21: Remote command history.** Every lock, sign-out, shutdown, restart and remote-control session, with who issued it and when.

![Workstations](prototype-images/teacher-computers.png)

**Figure C.22: Workstations.** The teacher view of the laboratory machines and their power and lock state.

![Class list](prototype-images/teacher-classes.png)

**Figure C.23: Class list.** The classes this teacher advises.

![Student profiles](prototype-images/teacher-students.png)

**Figure C.24: Student profiles.** The pupils enrolled in those classes.

![Class restrictions](prototype-images/teacher-restrictions.png)

**Figure C.25: Class restrictions.** The application and website rules enforced on the workstations during a session.

![Classroom records](prototype-images/teacher-records.png)

**Figure C.26: Classroom records.** Attendance, session duration and usage, read back from the same history the monitoring wall writes.

![Laboratory utilisation](prototype-images/teacher-lab.png)

**Figure C.27: Laboratory utilisation.** How heavily the laboratory is used, by period.

![Activity timeline](prototype-images/teacher-timeline.png)

**Figure C.28: Activity timeline.** Sessions, commands and infractions on one chronological axis.

![Browser monitoring history](prototype-images/teacher-browser.png)

**Figure C.29: Browser monitoring history.** The websites reached on each workstation, and which were blocked.

![Monitoring alerts](prototype-images/teacher-alerts.png)

**Figure C.30: Monitoring alerts.** Infractions raised by the client, acknowledged, dismissed or reopened.

![Account settings](prototype-images/teacher-settings.png)

**Figure C.31: Account settings.** The teacher’s own account and password.

---

## Appendix D. Sample Program Code

The listings below are copied from the repository. Each names the file it comes from.

### D.1 The portal refuses a student at the front door

`Monitoring And Remote Access/Server/Controllers/AccountController.cs`

The boundary described in Appendix B is one branch of the sign-in switch. A student's credentials may be entirely correct; the portal still refuses them, and deliberately does not count the attempt against the lockout ceiling, because it is the wrong door rather than a failed login.

```csharp
switch (result.Role)
{
    case AccountRole.Student:
        // The web portal is for teachers and administrators. A
        // student's account is real and their password is right, so
        // this is not a failed login and must not count against the
        // attempt ceiling - it is the wrong door. They are told
        // which door is theirs.
        //
        // The student agent signs in through ClientAuthController
        // instead, which is unaffected by this.
        ViewBag.Error = "This portal is for teachers and administrators. " +
            "Sign in on the CAMS Student Client on your workstation instead.";
        return View();

    case AccountRole.Teacher:
        await SignInAsync(result, string.Empty);
        HttpContext.Session.SetInt32("TeacherId", result.AccountId!.Value);
        HttpContext.Session.SetString("TeacherName", result.DisplayName ?? "");
        HttpContext.Session.SetString("Role", RoleNames.Teacher);
        _loginCache.Remove(key);
        return RedirectToAction("Dashboard", "Teacher");
```

### D.2 Every remote command is guarded and logged before it is sent

`Monitoring And Remote Access/Server/Hubs/RemoteMonitoringHub.cs`

No command reaches a workstation without first proving the caller is a teacher and that the target is a connected student. The audit entry is written before the message is dispatched, so the record survives even if the workstation never receives it.

```csharp
private async Task<StudentConnectionMessage> RequireTargetAsync(string targetConnectionId)
{
    await RequireTeacherAsync();

    if (string.IsNullOrWhiteSpace(targetConnectionId))
        throw new HubException("A target workstation is required.");

    var target = _monitoringService.FindStudent(targetConnectionId);
    if (target is null)
        throw new HubException("The target workstation is not connected as a student.");

    return target;
}

public async Task LockStudent(string targetConnectionId)
{
    var target = await RequireTargetAsync(targetConnectionId);
    await AuditCommandAsync("LockStudent", target);
    await Clients.Client(target.ConnectionId).SendAsync(HubEventNames.LockStudent);
}
```

### D.3 Archiving a workstation is not deleting it

`Monitoring And Remote Access/Server/Controllers/AdminController.cs`

The two operations are distinct and the system keeps them distinct. `DeleteComputer` marks the record archived and clears its assignment, so past lab sessions still resolve to a named station. Both refuse while a session is running.

```csharp
[HttpPost]
[TeacherSharedAction]
public async Task<IActionResult> DeleteComputer(int id)
{
    if (!CheckAccess()) return Denied();
    var computer = await _context.Computers.FindAsync(id);
    if (computer != null)
    {
        if (await _context.LabSessions.AnyAsync(s => s.ComputerId == id && s.IsActive))
        {
            TempData["ErrorMessage"] = "End the active lab session before archiving this workstation.";
            return RedirectToAction(nameof(Computers));
        }
        computer.Status = WorkstationStatus.Archived;
        computer.AssignedTo = null;
        await _context.SaveChangesAsync();
        await AuditAsync("ArchiveComputer", $"Archived {computer.LaboratoryStation}; historical records retained");
        TempData["Message"] = $"Workstation '{computer.LaboratoryStation}' archived. Historical records were retained.";
    }
    return RedirectToAction(nameof(Computers));
}
```

### D.4 The client reports telemetry through a durable queue

`Monitoring And Remote Access/Client/Services/MonitoringHubClient.cs`

Screen frames are sent immediately because a stale frame is worthless. Everything else is queued, so a dropped connection loses no record of what a pupil was doing.

```csharp
public async Task SendScreenFrameAsync(ScreenFrameMessage frame)
{
    EnsureConnected();
    await _connection!.InvokeAsync(HubMethodNames.SendScreenFrame, frame);
}

public async Task ReportIdleStatusAsync(IdleStatusMessage status)
{
    await QueueTelemetryAsync(TelemetryBatchItem.From(status));
}

public async Task ReportActiveAppAsync(ActiveAppMessage app)
{
    await QueueTelemetryAsync(TelemetryBatchItem.From(app));
}
```

### D.5 Exiting the client releases the workstation

`Monitoring And Remote Access/Client/MainForm.cs`

Quitting from the notification area signs the lab session out first. Without this a workstation would be left showing an occupant who has gone.

```csharp
private void ExitFromTray()
{
    _exitRequested = true;
    if (_hubClient is not null)
    {
        _ = ForceLogout(true); // logs the session out, then Application.Exit()
    }
    else
    {
        Close();
    }
}
```

### D.6 One navigation definition for every portal

`Monitoring And Remote Access/Server/Services/NavigationBuilder.cs`

The shared sections are declared once and concatenated into whichever sidebar is being built, so a page cannot end up with two names depending on who is looking at it.

```csharp
private static IEnumerable<NavSection> GlobalSections() => new[]
{
    new NavSection("Overview", new[]
    {
        new NavItem("Dashboard", "grid-fill", "Index", "Admin")
    }),
    new NavSection("People", new[]
    {
        new NavItem("Teachers", "person-badge-fill", "Teachers", "Admin"),
        new NavItem("Students", "mortarboard-fill", "Students", "Admin"),
        new NavItem("Classes", "folder-fill", "Classes", "Admin",
            AlsoActiveOn: new[] { "ClassDetails" })
    }),
    // ...
};
```

---

## Related documents

| Document | What it holds |
|---|---|
| [`Use-Case-Diagram.md`](Use-Case-Diagram.md) | The use case model and its coverage check |
| [`CAMS-Use-Case-Specifications.pdf`](CAMS-Use-Case-Specifications.pdf) | 195 written use cases, ten fields each |
| [`ERD.md`](ERD.md) | Entity relationship model and database schema |
| [`CAMS-User-Interface.pdf`](CAMS-User-Interface.pdf) | The same 31 screens as a printable PDF |
| [`Flowchart.md`](Flowchart.md) | Deployment and login flow in more detail |
| [`SignalR-Message-Flow.md`](SignalR-Message-Flow.md) | Every hub message and its direction |
| [`Menu-Structure-Diagram.md`](Menu-Structure-Diagram.md) | Navigation structure per role |
