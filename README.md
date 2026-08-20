# CAMS — Computer Account Management System

**Pardo Elementary School Laboratory Management System**

A LAN-based classroom management and monitoring application built with ASP.NET Core, SignalR, and WinForms. Automates student account tracking, enforces access restrictions, and replaces manual logbooks for 45-minute laboratory sessions.

---

## Download (pre-built — no .NET SDK or compiling needed)

[![Download Server](https://img.shields.io/badge/Download-CAMS_Server_Setup.exe-blue)](https://raw.githubusercontent.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/main/dist/CAMS-Server-Setup.exe)
[![Download Client](https://img.shields.io/badge/Download-CAMS_Client_Setup.exe-green)](https://raw.githubusercontent.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/main/dist/CAMS-Client-Setup.exe)

| Package | Download | For |
|---|---|---|
| **CAMS Server Installer** | [CAMS-Server-Setup.exe](https://raw.githubusercontent.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/main/dist/CAMS-Server-Setup.exe) | Teacher / lab PC |
| **CAMS Student Client Installer** | [CAMS-Client-Setup.exe](https://raw.githubusercontent.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/main/dist/CAMS-Client-Setup.exe) | Each student PC |
| **Server Source Code (ZIP)** | [CAMS-Server-Source.zip](https://raw.githubusercontent.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/main/dist/CAMS-Server-Source.zip) | Server source code |
| **Client Source Code (ZIP)** | [CAMS-Client-Source.zip](https://raw.githubusercontent.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/main/dist/CAMS-Client-Source.zip) | Client source code |

Both are **self-contained installer wizards**: download and run the `.exe` — the wizard handles everything (no .NET install needed on any PC). The server auto-creates its database on first run; student clients auto-discover the server on the LAN.

> **Note:** After downloading, right-click the `.exe` → **Properties** → check **Unblock** → **Apply** → **OK**. Then run the installer. (See [Troubleshooting](#troubleshooting))

> **Direct download links:** [Server EXE](https://raw.githubusercontent.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/main/dist/CAMS-Server-Setup.exe) | [Client EXE](https://raw.githubusercontent.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/main/dist/CAMS-Client-Setup.exe)

---

## How to Use

### 1. Start the server

Download `CAMS-Server-Setup.exe` on the teacher/lab PC and run the installer wizard. The wizard will:
- Install the server to `%LOCALAPPDATA%\CAMS Server`
- Open Windows Firewall port 5000 automatically
- Optionally start the server automatically with Windows
- Launch the server after install

Log in:

| Role | Username | Password |
|---|---|---|
| Admin | `admin` | `admin123` |
| Teacher | `teacher1` | `teacher123` |
| Student | `student1` | `student123` |

> Change passwords in the Admin portal after first login.

### 2. Start the student clients

On each student PC, download `CAMS-Client-Setup.exe` and run the installer wizard. During installation, you'll be prompted to enter the server address (your teacher will provide this).

The client auto-discovers the server on the LAN (UDP broadcast) — if auto-discovery fails, just type the server IP in the client settings. Students log in with their student credentials.

### 3. Begin class

- **Teacher:** open `http://localhost:5000/Teacher/Monitoring` → log in → click **Start Session**
- **Students:** already connected via the client — log in and their screens appear on the monitoring grid
- Teacher can monitor screens, lock workstations, broadcast messages, enforce restrictions, and view reports

---

## Build From Source (if you want to customize)

If you only want to **use** CAMS, skip this — use the download links above. This section is for developers who want to modify the code.

### Prerequisites

| Tool | Download |
|---|---|
| .NET 8 SDK | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Inno Setup 6 | https://jrsoftware.org/isdl.php |

### Build

Open the `CAMS` folder in PowerShell and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build-everything.ps1
```

This runs six steps automatically:
1. Runs 47 xUnit tests
2. Builds the solution (`RemoteMonitoring.sln`)
3. Publishes the server to `server-publish\`
4. Publishes the client (self-contained, single `.exe`) to `client-publish\`
5. Compiles both Inno Setup wizards to `server-dist\` and `client-dist\`

Outputs:

| Output | Purpose |
|---|---|
| `server-dist\CAMS-Server-Setup.exe` | Inno Setup wizard — run on the teacher/lab PC |
| `client-dist\CAMS-Client-Setup.exe` | Inno Setup wizard — run on each student PC |

If Inno Setup is not installed, the script exits with an error and tells you to install it first.

### After building

- Copy `server-dist\CAMS-Server-Setup.exe` to the teacher PC and run it.
- Copy `client-dist\CAMS-Client-Setup.exe` to each student PC and run it.
- Follow the **How to Use** section above.

---

## Troubleshooting

| Problem | Solution |
|---|---|
| "Windows protected your PC" message | Click "More info" and "Run anyway" |
| Client not connecting | Ensure the server is running on the same LAN. The client auto-discovers it. Open firewall port 5000 on the server PC |
| Auto-discovery not working | Client falls back to manual IP entry during install. Edit `client-settings.json` in the install folder if needed |
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
├── build-everything.ps1       # One-shot build (tests + publish + installers)
├── test-installer.ps1         # Validate built installers
├── server-installer.iss       # Inno Setup wizard for server
├── client-installer.iss       # Inno Setup wizard for client
├── start-server.bat           # Manual server launcher
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