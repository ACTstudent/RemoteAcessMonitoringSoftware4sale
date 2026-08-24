# CAMS — Computer Account Management System

## User Guide & System Documentation

Pardo Elementary School Computer Laboratory Management System

---

## Table of Contents
1. [System Architecture & Flowchart](#system-architecture)
2. [Role/User Legend](#user-roles)
3. [How to Use — Teacher](#teacher)
4. [How to Use — Student](#student)
5. [How to Use — Administrator](#administrator)
6. [Session Lifecycle](#session-lifecycle)
7. [Restriction Enforcement](#restrictions)
8. [Reports & Exports](#reports)
9. [Deployment](#deployment)

---

## System Architecture

```
┌─────────────────────────────────────────────────────────┐
│                   TEACHER WORKSTATION                    │
│  ┌─────────────────────────────────────────────────┐    │
│  │  Web Browser (Chrome/Edge)                      │    │
│  │  https://<server>:5000/Teacher/Monitoring       │    │
│  │                                                  │    │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐         │    │
│  │  │Student A  │ │Student B  │ │Student C  │         │    │
│  │  │ Live Feed │ │ Live Feed │ │ Live Feed │         │    │
│  │  │  Lock     │ │  Lock     │ │  Lock     │         │    │
│  │  │  Logout   │ │  Logout   │ │  Logout   │         │    │
│  │  │  Shutdown │ │  Shutdown │ │  Shutdown │         │    │
│  │  └──────────┘ └──────────┘ └──────────┘         │    │
│  └─────────────────────────────────────────────────┘    │
│         │ SignalR WebSocket │ HTTPS (intranet)            │
└─────────┼──────────────────┼──────────────────────────────┘
          │                  │
    ┌─────┴─────┐    ┌───────┴───────┐
    │  Student  │    │   Student B    │
    │  Client   │    │    Client      │
    │ (WinForms)│    │  (WinForms)    │
    │           │    │                │
    │ Screenshot│    │ Screenshot     │
    │ Stream    │    │ Stream         │
    │ Held-     │    │ Held-          │
    │ Apps    │    │     Apps         │
    └───────────┘    └────────────────┘
          │                  │
          └────────┬─────────┘
                   │
          ┌────────┴────────┐
          │  CAMS SERVER    │
          │  (ASP.NET Core) │
          │                  │
          │  SignalR Hub     │
          │  Controllers      │
          │  DB (MySQL/SQL)  │
          │                  │
          │  Local LAN Only  │
          │  No Cloud Reqs   │
          └─────────────────┘
```

## User Roles

| Role | Permissions |
|---|---|
| **Admin** | Manage teachers, students, computers, restriction rules, blacklists, session rules, LAN config, view audit logs and reports |
| **Teacher** | Dashboard with global session controls, live monitoring grid, start/pause/end per-student sessions, remote control, set access restrictions, export records |
| **Student** | Login only on assigned workstation, view session info, alert center, account settings |

## Teacher

### Login
- Open browser → `https://<server-ip>:5000`
- Enter the teacher credentials created by the CAMS administrator
- The dashboard shows active sessions quick-access cards

### Global Session Controls (Header Bar)
- **Start Session** — Opens the lab session; the elapsed timer starts counting upward
- **Pause Session** — Freezes the countdown (students see "Paused")
- **End Session** — Immediately logs out all students and locks their workstations

### Monitoring Grid
- Student cards show: workstation name, live screen feed, idle/active status badge, current active app name
- Click a card → opens the **Workstation Action Drawer** with:
  - **Lock Screen** — locks the student's Windows workstation
  - **Unlock** — releases the lock
  - **Log Out** — forces the student client to disconnect
  - **Shut Down** — shuts down the student workstation
  - **Remote Access** — takes control of mouse + keyboard on the student PC
  - **Broadcast Teacher Screen** — shares teacher's screen to all student monitors
  - **Send Warning** — sends a popup message to the student

### Infraction Alerts
- When a student tries to open a blocked app or website, their card **flashes red**
- The **alert bell badge** in the header increments and logs the violation
- The student is **killed** from the blocked app immediately (applications) or warned (websites)

### Workstation Controls

| Control | Action |
|---|---|
| **Lock Screen** | Locks student's Windows session |
| **Log Out** | Disconnects student from CAMS |
| **Shut Down** | Shuts down student computer |
| **Remote Access** | Teacher mouse/keyboard control the student PC |
| **Broadcast Teacher Screen** | Shares teacher screen to all students |
| **Send Warning Popup** | Sends a popup message to one or all students |

### Per-Session Management
The Session Management page allows the teacher to start individual per-student sessions with optional:
- Student selection
- Computer station assignment
- Session rule (from the admin Session Rules)

---

## Student

### Login
- Launch the WinForms student client (`Client.exe`)
- Enter the assigned Student ID and password; the client validates both over HTTPS before opening SignalR

### Overlay Toolbar
After login, a sticky toolbar appears:
- **Unit** — the machine name
- **Student** — logged-in user name
- **Elapsed Timer** — counts up while the global session is Running or Paused

### Enforcement
- Restricted apps are automatically **killed** if on the blocklist
- Restricted websites trigger **warning popups** (in browser)
- Violations are reported to the teacher instantly
- Whitelist mode: only explicitly permitted apps can run; all others are killed

### Session End
When the teacher ends the session:
- A "Session End" modal appears
- The student is automatically logged out
- The Windows workstation is locked

### Alert Center
The web portal `/Student/Alerts` shows all notifications and warnings.

---

## Administrator

### Account Management
- **Student Accounts** — CRUD with direct workstation-to-student mapping
- **Teacher Accounts** — create/edit/ delete
- **Workstation Mapping** — assign students to specific lab units
- **Computers** — add/remove stations

### Restriction Manager
- **Application rules** — process names to block (e.g., "fortnite", "steam")
- **Website rules** — website names to block (e.g., "facebook", "youtube")
- **Mode**: **Block** (default) or **Allow (whitelist only)** — everything else is killed

### Session Rules
- Configure max duration, pause rights, remote control permissions per session rule

### Reports & Export
- **Audit Trail**: chronological log of logins, account changes, infractions
- **System Logs**: system errors
- **Reports**: session and usage logs with multi-filter (30 days default), top apps table, and CSV download button
- **All reports exportable as CSV** (System Log, Audit Log, Usage Log, Session reports)

---

## Session Lifecycle

Each student session:
1. **Start** in Session** — teacher creates per-student session via HTTP with optional session rule
2. **Running Session** — student is active, screen is being captured, filtered by horinal apps
3. **Pause/Resume** — controls of the session **start from the teacher or admin** — not from student entry
4. **Ending** — the teacher logs** the student, the hardware workstation's stopped, and the session log entry is timestamped

The **global session** (Start/Pause/End CTAs in Monitoring) works similarly but affects **all** connected students as **one batch**
- Start → starts global timer
- End → logs out all students and locks all stations

---

## Restriction

Restriction rules are **downloaded to the student client** after login.

- **Blacklist Rule →** *Block* targets: Scan active app every ~5 seconds. If it matches → kill process → send `Infraction` SignalR message to teacher → teacher panel flashes red → violation logged to Audit
- **Whitelist Rule →** *Allow* rules override list: Only apps matching the whitelist can run. Any others are killed automatically, same as above
- Website monitoring: the client checks paused browser window titles for blocked keywords → if found, a **popup warning** is sent to the student's monitor (browsers not killed, only applist)

---

## Deployment

### Development
```
dotnet run --project Server
```
Server is at `https://localhost:5000`. Set `Cams__InitialAdminPassword` before the first launch; CAMS does not ship passwords. To seed teacher and student accounts during startup, also set `Cams__SeededTeacherPassword` and `Cams__SeededStudentPassword`. Their default usernames are `teacher` and `student`; existing accounts are never overwritten.

### Production LAN Setup
1. Install .NET 8 runtime on the server machine
2. Publish the Server project
3. Configure the MySQL or SQL Server connection string in appsettings.json (or `DatabaseProvider: "Sqlite"`)
4. Run Server.exe
5. Build student installer via `./publish-client.ps1`; installs with bundled .NET
6. Student Client connects to the server IP/port LAN configurable via `client-settings.json`
7. Expose port 5000 for LAN with a firewall rule

---

## Requirements for Developing
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server LocalDB **or** MySQL **or** SQLite (file‑based, zero‑install default)
- PowerShell (for publish helpers)

---

## Server API / URLs

| URL | Use |
|---|---|
| `/Account/Login` | Login page |
| `/Admin` | Admin dashboard |
| `/Admin/Students` | Student account CRUD & mapping |
| `/Admin/Teachers` | Teacher account CRUD |
| `/Admin/Restrictions` | Whitelist/Blacklist rules |
| `/Admin/Blacklists` | Legacy blacklist |
| `/Admin/SessionRules` | Policies for per-session config |
| `/Admin/Computers` | Workstation profiles |
| `/Admin/Reports` | Session + usage report (CSV) |
| `/Admin/AuditLogs` | Audit trail (CSV) |
| `/Admin/SystemLogs` | Errors logged by middleware |
| `/Teacher/Dashboard` | Teacher home |
| `/Teacher/Sessions` | Per-student session start/end |
| `/Teacher/Monitoring` | **Live Monitoring Grid (Control Panel)** |
| `/Teacher/Records` | Export teacher records (CSV) |
| `/Student` | Student session info & alert center |
| `/remoteMonitoringHub` | SignalR Hub (WebSocket) |

---

## SignalR Message Legend

| Event | From → To | Payload | Meaning |
|---|---|---|---|
| Authenticated client connection | Client → Server | signed-in student cookie | Student computer joins the live grid |
| `SendScreenFrame` | Client → Server | base64 jpeg | Live screen bitmap |
| `ReceiveScreenFrame` | Server → Teacher Dashboard | cid, ScreenFrameMessage | Render in teacher grid |
| `StudentConnected/Distributed` | Server → Teacher | student info | Card add/remove in grid |
| `LockStudent/UnlockStudent` | Dashboard → Student | — | Lock or unlock workstation |
| `ForceLogout` | Dashboard → Student | — | Logout student |
| `SendRemoteInput` | Control Centre → Server | control meta | Remote mouse/keyboard |
| `BroadcastScreen` | Dashboard → Server | base64 frames | Tell all students teacher screen |
| `SendWarningPopup` | Dashboard → Client | message | Show warning on student screen |
| `ShutdownStudent` | Dashboard → Client | — | Shut down workstation |
| `ReportInfraction` | Client → Server | infraction data | Report blocked app / site |
| `InfractionDetected` | Server → Teacher | infraction data | Flash alert badge on dashboard |
| `GlobalSessionState` | Server → All | status, elapsed seconds | Push current session status |
| `SessionEnded` | Server → Students | — | Hard logout when session ends |
| `FetchRestrictions` | Student → Server | — | Asking for active rules |
| `RestrictionsReceived` | Server → Student | rules list | Download restrictions to client |
