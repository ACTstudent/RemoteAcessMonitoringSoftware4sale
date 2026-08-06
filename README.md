# CAMS — Computer Account Management System

**Pardo Elementary School Laboratory Management System**

A LAN-based classroom management and monitoring application built with ASP.NET Core, SignalR, and WinForms. Automates student account tracking, enforces access restrictions, and replaces manual logbooks for 45-minute laboratory sessions.

---

## Download (pre-built — just extract and run)

No .NET SDK, no Inno Setup, no compiling. Download the zip, extract, use it.

| Bundle | Download | What to do |
|---|---|---|
| **CAMS Server** | [CAMS-Server-Portable.zip](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/latest/download/CAMS-Server-Portable.zip) | Extract anywhere on the teacher/lab PC. Double-click `Server.exe`. |
| **CAMS Student Client** | [CAMS-Client-Portable.zip](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/latest/download/CAMS-Client-Portable.zip) | Extract anywhere on each student PC. Double-click `client-portable.bat`. |

> **Client is self-contained** — no .NET or SQL Server needed on student PCs at all.
> **Server auto-creates** the database (`CAMS.db`) via SQLite on first run — zero database setup.
> **Auto-discovery** — the server broadcasts via UDP on the LAN. Student clients find it automatically.

---

## How to Use

### 1. Start the server

After extracting `CAMS-Server-Portable.zip`, double-click `Server.exe` on the teacher PC.

Open a browser and go to `http://localhost:5000/Admin`. Log in:

| Role | Username | Password |
|---|---|---|
| Admin | `admin` | `admin123` |
| Teacher | `teacher1` | `teacher123` |
| Student | `student1` | `student123` |

> Change passwords in the Admin portal after first login.

### 2. Start the student clients

After extracting `CAMS-Client-Portable.zip` on each student PC, double-click `client-portable.bat`.

The client auto-discovers the server on the LAN (UDP broadcast) — no IP configuration needed. Students log in with their student credentials.

### 3. Begin class

- **Teacher:** open `http://localhost:5000/Teacher/Monitoring` → log in → click **Start Session**.
- **Students:** already connected via the client — log in and their screens appear on the monitoring grid.
- Teacher can monitor screens, lock workstations, broadcast messages, enforce restrictions, and view reports.

---

## Build From Source (if you want to customize)

If you only want to **use** CAMS, skip this — use the download links above. This section is for developers who want to modify the code.

### Prerequisites

| Tool | Download |
|---|---|
| .NET 8 SDK | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Inno Setup 6 | https://jrsoftware.org/isdl.php |

### Build

Open the `CAMS` folder and **double-click**:

```
build-installers.bat
```

This runs four steps automatically:
1. Builds the solution (`RemoteMonitoring.sln`)
2. Publishes the server to `server-publish\`
3. Publishes the client (self-contained, single `.exe`) to `client-publish\`
4. Compiles both Inno Setup wizards to `server-dist\` and `client-dist\`

Outputs:

| Output | Purpose |
|---|---|
| `server-dist\CAMS-Server-Setup.exe` | Inno Setup wizerd — run on the teacher/lab PC |
| `client-dist\CAMS-Client-Setup.exe` | Inno Setup wizerd — run on each student PC |
| `client-publish\` | Portable client folder — copy to a network share for instant access |

If Inno Setup is not installed, installer executables are skipped but the portable folders are still created.

### After building

- **Distribute the server installer or portable folder** to the teacher PC. Run `Server.exe` or the installer.
- **Distribute to each student PC**: copy `client-publish\` or run `CAMS-Client-Setup.exe`.
- Follow the **How to Use** section above.

---

## Troubleshooting

| Problem | Solution |
|---|---|
| "Windows protected your PC" message | Click "More info" and "Run anyway" |
| Client not connecting | Ensure the server is running on the same LAN. The client auto-discovers it. Open firewall port 5000 on the server PC |
| Auto-discovery not working | Client falls back to `client-settings.json`. Edit it with the server IP if needed |
| Server cannot start | Make sure .NET 8 Runtime is installed on the server PC: https://dotnet.microsoft.com/download/dotnet/8.0 |
| Port 5000 blocked | Run on server PC: `netsh advfirewall firewall add rule name="CAMS" dir=in action=allow protocol=TCP localport=5000` |
| Silent deployment to 30+ PCs | See DEPLOYMENT.md for `/VERYSILENT` network deployment |

---

## Solution layout

```
CAMS/
├── CAMS-Guide.md              # Full user & developer guide
├── LICENSE                    # MIT
├── README.md                  # This file
├── DEPLOYMENT.md              # Deployment guide
├── build-installers.bat       # One-click build (server + client publisher)
├── build-installers.ps1       # One-shot build script
├── publish.ps1                # Server-only publisher. Installer
├── publish-client.ps1         # Client-only publish + wizer installer
├── server-installer.iss       # Inno Setup wizer for server
├── client-installer.iss       # Inno Setup wizer for client
├── start-server.bat           # Manual server launcher
├── client-portable.bat       # Portable client launcher (no install)
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
dotnet run --project Server
```

Server is at `http://localhost:5000`. Database auto-creates on first run.

## Database providers

Edit `Monitoring And Remote Access\Server\appsettings.json`:

| Provider | Config | Setup needed |
|---|---|---|
| **Sqlite** (default) | `"DatabaseProvider": "Sqlite"` | Nothing |
| SqlServer | `"DatabaseProvider": "SqlServer"` | SQL Server / LocalDB |
| MySql | `"DatabaseProvider": "MySql"` | MySQL server |

Schema is auto-created on first run via `EnsureCreated()`.

## License

MIT — see `LICENSE`.

## Documentation

Full user guide, flowchart, and SignalR event legend in **`CAMS-Guide.md`**. Deployment details in **`DEPLOYMENT.md`**.