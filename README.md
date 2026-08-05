# Remote Access Monitoring Software (CAMS)

Computer Account Management System for Pardo Elementary School — a remote lab monitoring system with:

- **WinForms Student Client** — streams the student's screen, receives remote control, lock/unlock, broadcasts, and teacher notifications; reports idle status and the active application.
- **ASP.NET Core Server** — SignalR hub, EF Core, session-based auth, role dashboards.
- **Admin portal** (`/Admin`) — teacher/student accounts, roles & permissions, computer profiles, restriction rules, website/app blacklists, session rules, LAN config, reports, audit trail, system logs.
- **Teacher portal** (`/Teacher`) — session start/pause/end, live monitoring grid, remote control (lock/unlock/force logout), screen broadcast, app tracker, idle monitor, access restrictions, classroom records.
- **Student portal** (`/Student`) — session info with remaining-time countdown, assigned unit, alert center, account settings.

## Solution layout

```
Monitoring And Remote Access/
├── RemoteMonitoring.sln
├── Shared/          # Shared contracts library (DTOs + hub event names)
├── Server/          # ASP.NET Core web app (SignalR hub, controllers, views)
├── Client/          # WinForms student client
└── DIAGRAMS/        # System diagrams (flowchart, message flow, ERD, menu structure)
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (build & run)
- [SQL Server](https://www.microsoft.com/sql-server/sql-server-downloads) or SQL Server LocalDB (the default connection string uses `(localdb)\mssqllocaldb`)

## Build

```powershell
cd "Monitoring And Remote Access"
dotnet restore
dotnet build RemoteMonitoring.sln
```

## Publish (deploy the server)

Run the included publish script, or manually:

```powershell
cd "Monitoring And Remote Access"
dotnet publish Server\Server.csproj -c Release -o ..\publish
```

The publish output is a self-contained server folder. Copy it to the server PC and run `Server.exe` (see `DEPLOYMENT.md`).

## Database

The schema is created/migrated with EF Core:

```powershell
cd "Monitoring And Remote Access"
dotnet tool install --global dotnet-ef
dotnet ef database update --project Server
```

> **Note:** the codebase uses `UseSqlServer`. If you prefer SQLite, switch the provider in `Program.cs` and the package in `Server.csproj`.

## Default seeded accounts

| Role | Username | Password |
| --- | --- | --- |
| Admin | `admin` | `admin123` |
| Teacher | `teacher1` | `teacher123` |
| Student | `student1` | `student123` |

Change these before production use — passwords are stored as plain text in this prototype.

## Client configuration

The student client connects to `http://localhost:5000/remoteMonitoringHub` (see `Client/MainForm.cs`). Point it at your server's LAN IP, e.g. `http://192.168.1.100:5000/remoteMonitoringHub`.

## Client installer (download & setup wizard)

Build a self-contained client + an Inno Setup installer that students can download and run:

1. Install [Inno Setup 6](https://jrsoftware.org/isdl.php) (required to compile the wizard).
2. Run from the repo root:

   ```powershell
   .\publish-client.ps1
   ```

   This publishes the client as a self-contained single `.exe` (no .NET install needed on student PCs) and builds **`client-dist\CAMS-Client-Setup.exe`**.

3. Host that installer on your server/LAN share; students run it and follow the wizard (Next → Install → Finish), then launch the client.

## Running

```powershell
# Server (listens on http://localhost:5000)
dotnet run --project Server

# Client
dotnet run --project Client
```
