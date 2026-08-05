# CAMS — Computer Account Management System

**Pardo Elementary School Laboratory Management System**

A LAN-based classroom management and monitoring application built with ASP.NET Core, SignalR, and WinForms. Automates student account tracking, enforces access restrictions, and replaces manual logbooks for 45-minute laboratory sessions.

---

## Easy Installation

Build both installers on **one machine** (teacher's PC or lab server) — student PCs only need the wizard, no .NET install required.

### Prerequisites (build machine only)

| Tool | Download |
|---|---|
| .NET 8 SDK | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Inno Setup 6 | https://jrsoftware.org/isdl.php |

### A. Server side — the lab PC

Run from the repo root:

```powershell
.\publish.ps1
```

Two outputs:
- `server-publish\` — raw folder (manual fallback)
- **`server-dist\CAMS-Server-Setup.exe`** ← copy this to the lab/teacher PC (requires Windows admin for install)

**Run the wizard → done.** SQLite auto-creates the database on first launch, no SQL Server needed. Firewall port 5000 is opened automatically.

After install, open the server dashboard:
```
http://localhost:5000/Admin          (admin panel)
http://localhost:5000/Teacher/Monitoring  (teacher panel)
```

### B. Client side — each student PC

```powershell
.\publish-client.ps1
```

This creates **`client-dist\CAMS-Client-Setup.exe`** — a self-contained installer (no .NET needed on student PCs).

Distribute it to every student machine. The wizard asks for the **server address** (e.g. `http://192.168.1.100:5000/remoteMonitoringHub`).

Student runs: Next → enters server IP → Install → Finish. Client launches and connects.

---

## Solution layout

```
CAMS/
├── CAMS-Guide.md              # Full user & developer guide
├── LICENSE                    # MIT
├── README.md                  # This file
├── DEPLOYMENT.md              # Deployment guide
├── publish.ps1                # Server publish + installer builder
├── publish-client.ps1         # Client publish + installer builder
├── server-installer.iss       # Inno Setup wizard for server
├── client-installer.iss       # Inno Setup wizard for client
├── start-server.bat           # Manual launcher (open Server folder, double-click)
├── DIAGRAMS/                  # System diagrams
└── Monitoring And Remote Access/
    ├── RemoteMonitoring.sln
    ├── Shared/                # DTOs + SignalR event constants
    ├── Server/                # ASP.NET Core MVC + SignalR hub + EF Core
    │   └── wwwroot/lib/       # Vendored Bootstrap & SignalR (LAN-only)
    └── Client/                # WinForms student agent (.NET 8)
```

## Quick start (developer)

```powershell
cd "Monitoring And Remote Access"
dotnet restore
dotnet build RemoteMonitoring.sln
dotnet run --project Server
```

Server: `http://localhost:5000`

## Default accounts

| Role | Username | Password |
|---|---|---|
| Admin | `admin` | `admin123` |
| Teacher | `teacher1` | `teacher123` |
| Student | `student1` | `student123` |

Passwords are hashed (PBKDF2); seeds use hashed values. Change these in the Admin portal after first login.

## Database

Edit `server/app-settings.json` to switch providers:

| Provider | Config | Install needed |
|---|---|---|
| **Sqlite** (default) | `"DatabaseProvider": "Sqlite"` | Nothing |
| SqlServer | `"DatabaseProvider": "SqlServer"` | SQL Server / LocalDB |
| MySql | `"DatabaseProvider": "MySql"` | MySQL server |

Schema is auto-created via `EnsureCreated()` on first run.

## Client configuration

If installing manually (no installer), edit `client-publish.\client-settings.json` before distributing:

```json
{
  "ServerUrl": "http://192.168.1.100:5000/remoteMonitoringHub"
}
```

The installer wizard handles this during setup — no manual edit needed.

## License

MIT — see `LICENSE`.

## Documentation

Full user guide, flowchart, and SignalR event legend in **`CAMS-Guide.md`**. Deployment details in **`DEPLOYMENT.md`**.