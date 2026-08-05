# CAMS — Computer Account Management System

**Pardo Elementary School Laboratory Management System**

A LAN-based classroom management and monitoring application built with ASP.NET Core, SignalR, and WinForms. Automates student account tracking, enforces access restrictions, and replaces manual logbooks for 45-minute laboratory sessions.

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

5. (Optional) Find the server's LAN IP address so students can connect:
   ```powershell
   ipconfig
   ```
   Look for `IPv4 Address` (e.g., `192.168.1.100`). The server hub URL will be:
   ```
   http://192.168.1.100:5000/remoteMonitoringHub
   ```

---

### Step 4: Install on each student PC

1. Copy `client-dist\CAMS-Client-Setup.exe` to each student machine (USB, shared folder, or LAN).
2. **Double-click** the installer on each PC.
3. **Click Next** until you reach the **"Server Address"** page.
4. Enter the server's LAN IP:
   ```
   http://192.168.1.100:5000/remoteMonitoringHub
   ```
5. Click **Next** → **Install** → **Finish**.

The client launches automatically and connects to the server.

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
| Client can't connect | Verify the server IP is correct; check firewall on the server PC |
| Server won't start | Make sure .NET 8 Runtime is installed on the server PC (included if you ran the SDK, otherwise get it from https://dotnet.microsoft.com/download/dotnet/8.0) |
| Port 5000 blocked | Run on server: `netsh advfirewall firewall add rule name="CAMS" dir=in action=allow protocol=TCP localport=5000` |

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