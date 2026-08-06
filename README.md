# CAMS — Computer Account Management System

**Pardo Elementary School Laboratory Management System**

A LAN-based classroom management and monitoring application built with ASP.NET Core, SignalR, and WinForms. Automates student account tracking, enforces access restrictions, and replaces manual logbooks for 45-minute laboratory sessions.

---

## Download (pre-built installers)

| Installer | Download |
|---|---|
| **CAMS Server** | [CAMS-Server-Setup.exe](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/latest/download/CAMS-Server-Setup.exe) — run on the teacher/lab PC |
| **CAMS Student Client** | [CAMS-Client-Setup.exe](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/latest/download/CAMS-Client-Setup.exe) — run on each student PC |

> One-click installers. No .NET or SQL Server needed on student PCs.
> The server installs to `%LOCALAPPDATA%\CAMS Server` and auto-creates the database via SQLite.

---

## Easy Installation — Step-by-Step

### Overview

One build machine produces two `.exe` wizards. No .NET or SQL Server needed on student PCs.

---

### Step 1: Install prerequisites on the build machine

Download and install both:

| Tool | Download |
|---|---|
| .NET 8 SDK | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Inno Setup 6 | https://jrsoftware.org/isdl.php |

---

### Step 2: Build both installers

Open the `CAMS` folder, **double-click**:

```
build-installers.bat
```

A PowerShell window opens and runs all steps automatically:
1. Builds the solution
2. Publishes the server
3. Publishes the client (self-contained, single `.exe`)
4. Compiles both Inno Setup wizards

When it finishes, two `.exe` installers are created:

| Installer | Purpose |
|---|---|
| `server-dist\CAMS-Server-Setup.exe` | Run on the **teacher/lab PC** (the server) |
| `client-dist\CAMS-Client-Setup.exe` | Run on **each student PC** |
| `client-publish\` | Portable client folder — copy to a network share for instant access |

---

### Step 3: Install the server (teacher/lab PC)

1. Copy `server-dist\CAMS-Server-Setup.exe` to the teacher's computer (e.g. via USB or network share).
2. **Double-click** the installer.
3. Click **Next** → **Install** → **Finish**.

The wizard automatically:
- Installs to `%LOCALAPPDATA%\CAMS Server`
- Opens Windows Firewall port **5000**
- Creates a desktop shortcut
- Launches the server after install

4. After install, open a browser and go to:
   ```
   http://localhost:5000/Admin
   ```
   Log in as:
   | Role | Username | Password |
   |---|---|---|
   | Admin | `admin` | `admin123` |
   | Teacher | `teacher1` | `teacher123` |

   > **Note about SQLite:** No database installation is required. SQLite automatically creates
   > the database file (`CAMS.db`) next to `Server.exe` the first time the server starts.
   > No SQL Server, no manual setup — it just works.

   > **Auto-discovery:** The server broadcasts its address on the LAN automatically.
   > Student clients find it without any IP configuration.

---

### Step 4: Install on each student PC

**Option A — Installer (recommended for permanent lab PCs):**

1. Copy `client-dist\CAMS-Client-Setup.exe` to each student PC.
2. **Double-click** the installer.
3. Click **Next** until the **"Server Address"** page.
4. The address is usually auto-filled by discovery. If not, enter the server IP — ask your teacher or use `http://192.168.1.100:5000/remoteMonitoringHub`.
5. Click **Install** → **Finish**.

**Option B — Portable (no install needed):**

1. Copy the entire `client-publish\` folder to a network share (e.g., `\\server\CAMS\`).
2. Students navigate to the share and **double-click `client-portable.bat`**.
3. The client discovers the server automatically — no configuration, no installer.

> **No .NET install needed on student PCs** — `Client.exe` is a self-contained single file.

---

### Step 5: Start class

1. **Teacher:** open `http://localhost:5000/Teacher/Monitoring` → log in → click **Start Session**.
2. **Students:** the client is already running (auto-connected). They log in with their student credentials.
3. Teacher can now monitor screens, lock workstations, broadcast messages, and enforce restrictions.

---

### Troubleshooting

| Problem | Fix |
|---|---|
| "Windows protected your PC" when running installer | Click "More info" → "Run anyway" |
| Client can't connect (server not found) | Make sure the server is running. Client auto-discovers it on the LAN. If still failing, click "Enter server IP manually" on the error dialog. |
| Client can't connect (port blocked) | Run on server: `netsh advfirewall firewall add rule name="CAMS" dir=in action=allow protocol=TCP localport=5000` |
| Auto-discovery not working | Client falls back to `client-settings.json`. Edit it manually or use the installer's IP page. |
| Deploy to 30+ PCs at once (silent install) | `client-dist\CAMS-Client-Setup.exe /VERYSILENT /ServerIP="http://192.168.1.100:5000/remoteMonitoringHub"` |
| Server won't start | Make sure .NET 8 Runtime is installed on the server PC |

---

## Solution layout

```
CAMS/
├── CAMS-Guide.md              # Full user & developer guide
├── LICENSE                    # MIT
├── README.md                  # This file
├── DEPLOYMENT.md              # Deployment guide
├── build-installers.bat       # ← Double-click this to build both .exe installers
├── build-installers.ps1       # One-shot script (server + client publish + Inno Setup)
├── publish.ps1                # Server-only publish + installer
├── publish-client.ps1         # Client-only publish + installer
├── server-installer.iss       # Inno Setup wizard for server
├── client-installer.iss       # Inno Setup wizard for client
├── start-server.bat           # Manual launcher (open publish folder, double-click)
├── client-portable.bat        # Portable client launcher (no install needed)
├── DIAGRAMS/                  # System diagrams
└── Monitoring And Remote Access/
    ├── RemoteMonitoring.sln
    ├── Shared/                # DTOs + SignalR event constants
    ├── Server/                # ASP.NET Core MVC + SignalR hub + EF Core
    │   └── wwwroot/lib/       # Vendored Bootstrap & SignalR (LAN-only)
    └── Client/                # WinForms student agent (.NET 8)
```

## Quick start (developer)

If you're editing code, run the server directly:

```powershell
cd "Monitoring And Remote Access"
dotnet run --project Server
```

Server: `http://localhost:5000`

## Database providers

Edit `Monitoring And Remote Access\Server\appsettings.json` to switch providers:

| Provider | Config | Install needed |
|---|---|---|
| **Sqlite** (default) | `"DatabaseProvider": "Sqlite"` | Nothing |
| SqlServer | `"DatabaseProvider": "SqlServer"` | SQL Server / LocalDB |
| MySql | `"DatabaseProvider": "MySql"` | MySQL server |

Schema is auto-created via `EnsureCreated()` on first run.

## License

MIT — see `LICENSE`.

## Documentation

Full user guide, flowchart, and SignalR event legend in **`CAMS-Guide.md`**. Deployment details in **`DEPLOYMENT.md`**.