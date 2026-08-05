# CAMS — Computer Account Management System

**Pardo Elementary School Laboratory Management System**

A LAN-based classroom management and monitoring application built with ASP.NET Core, SignalR, and WinForms. Automates student account tracking, enforces access restrictions, and replaces manual logbooks for 45-minute laboratory sessions.

## Features

| Module | Details |
|---|---|
| **Real-time Monitoring** | Live screen streaming (SignalR ~12 FPS), idle/active status, active app tracker |
| **Remote Control** | Lock/unlock, force logout, remote mouse/keyboard, shutdown workstation |
| **Teacher Control Panel** | Global session Start/Pause/End, multi-monitor grid, workstation action drawer, screen broadcast, infraction alert badge |
| **Student Client** | WinForms agent with sticky toolbar, elapsed-time display, restriction enforcement (app kill + website warning) |
| **Access Restrictions** | Block (blacklist) or Allow (whitelist) rules for apps and websites; violations flashed to teacher instantly |
| **Account Management** | Admin CRUD for student/teacher accounts, workstation mapping matrix |
| **Roles & Permissions** | Admin, Teacher, Student — role-based dashboards with permission enforcement |
| **Audit Trail** | Login attempts (success/fail), account changes, restriction violations, global session actions logged |
| **Reports & Export** | Session records, usage logs, top apps table, audit logs — all CSV-exportable with date-range filter |
| **Security** | PBKDF2 password hashing, station binding (student must log in from assigned workstation) |
| **LAN-Only** | All static assets vendored locally (no CDN), WebSocket for real-time comms over intranet |

## Solution layout

```
CAMS/
├── CAMS-Guide.md          # Full user & developer guide
├── LICENSE                # MIT
├── README.md              # This file
├── DEPLOYMENT.md          # Server deployment walkthrough
├── publish.ps1             # Server publish script
├── publish-client.ps1      # Student client publish + installer builder
├── client-installer.iss    # Inno Setup wizard template
├── DIAGRAMS/               # System diagrams (flowchart, ERD, menu, SignalR flow)
└── Monitoring And Remote Access/
    ├── RemoteMonitoring.sln
    ├── Shared/             # DTOs + SignalR event constants
    ├── Server/             # ASP.NET Core MVC + SignalR hub + EF Core
    │   ├── wwwroot/lib/    # Vendored Bootstrap & SignalR
    │   └── Views/           # Razor pages (Admin, Teacher, Student)
    └── Client/             # WinForms student agent (.NET 8)
```

## Quick start

```powershell
cd "Monitoring And Remote Access"
dotnet restore
dotnet build RemoteMonitoring.sln
dotnet run --project Server
```

Server is at `http://localhost:5000`.

## Default accounts (password hashed)

| Role | Username | Password (before hashing) |
|---|---|---|
| Admin | `admin` | `admin123` |
| Teacher | `teacher1` | `teacher123` |
| Student | `student1` | `student123` |

## Database

to `appsettings.json`:
- `"DatabaseProvider": "Sqlite"` — zero-install, file-based `CAMS.db`
- `"DatabaseProvider": "SqlServer"` — LocalDB / full SQL Server
- `"DatabaseProvider": "MySql"` — MySQL (`Pomelo.EntityFrameworkCore.MySql`)

The schema autocrche via `EnsureCreated()` on first run.

## Configuration

- **Client URL**: edit `Client/Client-settings.json` with the server LAN address, then rebuild
- **Teacher panel**: open `http://<server-ip>:5000/Teacher/Monitoring` in a browser
- **Student client**: launch `Client.exe` (or `dotnet run --project Client`)

## License

MIT — see `LICENSE`.

## Documentation

Full user guide, flowchart, and SignalR event legend in **`CAMS-Guide.md`**.