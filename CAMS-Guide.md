# CAMS Computer Account Management System

## User And System Guide

CAMS supports supervised classroom monitoring and computer laboratory management on a private Windows LAN. This guide describes the implemented role scope and operational limits. For installation and trust procedures, see [DEPLOYMENT.md](DEPLOYMENT.md).

## System Architecture

```mermaid
flowchart LR
    subgraph Portals[Authenticated browser portals]
        A[Admin]
        T[Teacher]
        W[Student web portal]
    end
    subgraph Server[CAMS Server]
        MVC[ASP.NET Core MVC and API]
        HUB[SignalR hub]
        DB[(SQLite CAMS.db)]
        DH[Admin Deployment Hub]
    end
    C[Windows Student Client]

    A -->|HTTPS TCP 5000| MVC
    T -->|HTTPS TCP 5000| MVC
    W -->|HTTPS TCP 5000| MVC
    C -->|HTTPS login and SignalR TCP 5000| HUB
    MVC --> DB
    HUB --> DB
    A --> DH
    HUB -->|screen, status, alerts| T
    T -->|authorized commands| HUB
    HUB -->|commands and policy| C
    Server -->|UDP broadcast to client port 5001| C
```

The server sends discovery broadcasts; clients receive UDP `5001`. The server requires inbound Private TCP `5000`, not inbound UDP `5001`. LAN Status is detected and read-only; CAMS does not configure DHCP, DNS, gateways, adapters, or runtime network binding.

## Roles And Authorization

Roles are fixed as `Admin`, `Teacher`, and `Student`. They are not configurable RBAC. The Roles and Permissions page is a view of seeded metadata; server authorization uses fixed role claims plus object-level checks.

| Role | Scope |
| --- | --- |
| Admin | Global users, classes, rosters, computers/mappings, policies, session rules, lab-wide session controls, reports, logs, lockouts, database maintenance, LAN status, and Deployment Hub. |
| Teacher | Global operational management of Teacher/Student accounts, classes, rosters/imports, computers/mappings/history, policies, session rules, lab-wide session controls, monitoring, and Student-workstation remote commands. Individual session actions and older Teacher pages, analytics, records and alerts retain teacher/adviser-class checks. Admin accounts, role metadata, Admin reports/logs, database maintenance, LAN status, and Deployment Hub remain Admin-only. |
| Student | Web session/alert/account pages, or a workstation-registered session through the Windows client. |

## Login Semantics

### Browser login

Open `https://<server>:5000/Account/Login`. Admin, Teacher, and Student credentials route to their respective browser portal. A Student browser login does not identify a workstation and therefore does not create the monitored client session.

### Windows CLIENT login

The WinForms client submits the student username or student number, password, and Windows machine name over HTTPS. For an active student, the server creates a missing workstation profile or safely reassigns an existing profile to that student. It rejects archived/maintenance workstations, conflicting active use, or a student already active on another PC. It creates or resumes the student's active `LabSession` and then opens authenticated SignalR.

There are no default passwords. Interactive server setup securely creates or recovers the Administrator account without writing its password to configuration. Teacher and Student accounts are created from the authenticated Admin portal. Accounts store password hashes, not plain passwords.

## Administrator

### Global controls UI

The Admin dashboard provides persisted lab-wide `Pause All`, `Resume Paused`, and `End All` controls. These affect eligible active sessions across teachers and notify connected clients. Admin also has global account activation/unlock controls, global restrictions, default session rules, reports, and database maintenance.

### Account, class, and computer management

- Create, edit, activate/deactivate, unlock, and manage teacher/student accounts.
- Create, update, archive, restore, or delete classes as permitted by data integrity rules.
- Assign/reassign teachers and manage class rosters.
- Create, edit, archive, and map unique workstation profiles. A workstation or student cannot participate in more than one active session, and one workstation cannot be assigned to multiple students.
- Removing/archive operations preserve historical records where the UI states that behavior.

### Policy and records

- Manage global application/domain block or allow rules.
- Manage duration, pause, remote-control, default, and active flags for session rules.
- Review and export usage, sessions, audit, system, alert, browser-status, and command records available in the UI.
- Back up and validate SQLite; a staged restore is revalidated and applied at restart.

### LAN Status and Deployment Hub

LAN Status displays the detected request and SignalR paths only. Deployment Hub at `/Admin/Deployment` is authenticated and local: it validates the packaged client installer, checksum, manifest, client/server version match, active certificate, and compatible endpoints. It displays connected-client count and creates offline workstation bundles. The public GitHub Pages portal is informational and must never host a deployment's root CER or private PFX.

## Teacher

### Scope rules

Active teachers can monitor and control all connected student clients and use lab-wide pause/resume/end actions. Explicitly shared `/Admin/...` actions provide global account, class, roster, workstation and policy management. Individual session actions, older `/Teacher/...` management pages, and analytics/record queries retain their teacher or adviser/class checks. Admin-only administration remains restricted. The server revalidates Teacher status for management requests and SignalR commands; deactivating a Teacher blocks later operations from an existing authenticated session. Shared `/Admin/...` management actions use an explicit allow-list, so unlisted Administrator pages remain forbidden even when a link is hidden.

### Global management through shared Admin actions

- Create, edit, activate/deactivate, unlock, and soft-archive Student accounts globally.
- Create, edit, activate/deactivate, unlock, and soft-archive peer Teacher accounts. A Teacher cannot change their own account from global management, and active classes must be reassigned or archived before their Teacher is deactivated.
- Create, edit, assign/reassign, archive, restore, or delete classes when data-integrity rules permit it.
- Manage any class roster, explicitly move existing Students, and import Students individually, in bulk, or by CSV.
- Create, rename, assign, unassign, change status, archive, and inspect history for workstations globally.
- Account and archive operations retain historical records where stated by the UI.

### Sessions and monitoring

1. On `/Teacher/Sessions`, start a session for a student in one of your active classes, optionally selecting an available workstation and active session rule.
2. Individual pause/resume/end actions apply to your own sessions. The global buttons affect eligible sessions across all teachers; pause still requires a permitting session rule.
3. Monitor every connected Student client, including screen frames, connectivity, active application, browser status, and idle state.
4. Globally use lock, release CAMS lock state, force logout, restart, shutdown, warning, notification, broadcast, and remote support/input commands.
5. End the session to persist its end state, notify the connected client, release the workstation, and log out the client.

The capture loop targets a 50 ms delay with one frame in flight. This is not a 20 FPS guarantee; capture and network conditions determine observed updates.

### Windows control limits

`Lock` calls the Windows workstation lock. `Unlock` only releases CAMS-maintained lock state; it cannot dismiss or inject credentials into the Windows secure desktop. A user must sign in to Windows normally. Remote input, restart, shutdown, and lock effects depend on Windows privileges, endpoint security, and local policy and must be validated on target machines.

### Restrictions, warnings, and alerts

Teachers can create and edit global block/allow rules, blacklists, whitelists, application/website categories, and session rules. Rules changed through the global Teacher management surface are stored as global and are refreshed on clients after policy changes.

- Application block violations may terminate the matching process and report an infraction.
- Website telemetry stores normalized domains, not browser credentials, cookies, page content, full paths, queries, or fragments. Live screen frames can still show any content visible on the display.
- A website violation does not close the browser. It shows a CAMS-owned, topmost WinForms dialog, not a browser popup.
- Teacher warnings also appear as CAMS topmost dialogs.
- Alerts can be grouped, filtered, acknowledged, dismissed with a reason, reopened, and exported where supported.

## Student

### Client session

After client login and workstation registration, the client shows the unit, student identity, session state, and timer. It streams screen frames and status while connected, applies active policies, queues bounded telemetry during temporary interruptions, and reconnects according to configured resilience behavior.

When a session ends, the client displays a CAMS session-ended dialog, logs out/exits, and may lock the workstation. Secure-desktop unlock still requires Windows sign-in.

### Web portal

The authenticated Student portal provides session information, alert history, and account/password settings. It is distinct from the WinForms client: portal login is not proof that the user is at the assigned workstation, does not open the client SignalR agent, and does not start screen monitoring.

## Session Lifecycle

```mermaid
stateDiagram-v2
    [*] --> BrowserPortal: Student browser login
    BrowserPortal --> [*]: Browser logout

    [*] --> ClientValidation: CLIENT login with PC name
    ClientValidation --> Rejected: Invalid credentials or conflicting/unavailable station
    ClientValidation --> Running: Workstation created, safely assigned, or resumed
    Running --> Paused: Authorized pause and rule allows it
    Paused --> Running: Authorized resume
    Running --> Reconnecting: Temporary SignalR loss
    Paused --> Reconnecting: Temporary SignalR loss
    Reconnecting --> Running: Reauthenticated/reconnected
    Reconnecting --> Paused: Persisted state is paused
    Running --> Ended: Logout, authorized end, or expiration
    Paused --> Ended: Logout or authorized end
    Ended --> [*]
    Rejected --> [*]
```

Only one active session per student and per computer is allowed. A transient hub disconnect removes the live connection/card but does not by itself guarantee that the persisted lab session is ended; reconnection can resume it. Confirm recovery behavior on the target LAN.

## Key URLs

| URL | Purpose |
| --- | --- |
| `/Account/Login` | Browser login for all three fixed roles. |
| `/Admin` | Admin dashboard and global session controls. |
| `/Admin/LanConfig` | Detected, read-only LAN status. |
| `/Admin/Deployment` | Authenticated local Deployment Hub. |
| `/AdminDatabase` | Admin database maintenance. |
| `/Teacher` | Teacher dashboard. |
| `/Teacher/Sessions` | Teacher-owned individual session actions and lab-wide bulk controls. |
| `/Teacher/Monitoring` | Lab-wide live monitoring and workstation controls. |
| `/Student` | Student web portal. |
| `/remoteMonitoringHub` | Authenticated SignalR hub used by client and dashboards. |
| `/api/client/login` | Strict student CLIENT login with workstation identity. |
| `/api/deployment/ping` | TLS readiness check used by the offline bundle. |

## Deployment Summary

1. Configure the first Admin secret and install the self-contained server.
2. Establish first trust from the locally generated public root CER or use a public production certificate. Never distribute PFX files.
3. Sign in to local `/Admin/Deployment`; select a detected certificate-compatible endpoint.
4. Verify hashes/manifests and matching client/server versions.
5. Install interactively per intended Windows user, or use the approved offline bundle procedure.
6. Open server inbound Private TCP `5000`. Clients receive UDP `5001` only for optional discovery.
7. Restart the server after a network/address change, confirm certificate coverage, and update saved client URLs if needed.
8. Confirm each connected client in Deployment Hub and the teacher's lab-wide monitoring grid.

## Windows And LAN Validation Checklist

- Verify current-user installation and trust context for both interactive setup and offline bundles.
- Verify TLS fingerprint/SHA-256 and endpoint SAN coverage without bypassing warnings.
- Test browser portal login separately from CLIENT login with workstation registration.
- Test TCP `5000`, optional client-received UDP `5001`, client isolation, and reconnect behavior.
- Test the 50 ms capture target under realistic load without assuming guaranteed FPS.
- Test CAMS warning dialogs, policy enforcement, monitoring scope, and connected-client confirmation.
- Test lock/CAMS-unlock semantics and Windows secure desktop behavior.
- Test remote input, restart, and shutdown under the site's Windows and endpoint-security policies.
- Change the server network/address, restart, verify the new compatible endpoint, update client URL, and retest.

## Related Diagrams

- [ERD](DIAGRAMS/ERD.md)
- [System Flowchart](DIAGRAMS/Flowchart.md)
- [Menu Structure](DIAGRAMS/Menu-Structure-Diagram.md)
- [SignalR Message Flow](DIAGRAMS/SignalR-Message-Flow.md)
- [Use Cases](DIAGRAMS/Use-Case-Diagram.md)
