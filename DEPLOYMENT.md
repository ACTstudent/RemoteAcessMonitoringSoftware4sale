# Deployment Guide

## Easy Install (Recommended)

Run the publish script on a machine with the .NET 8 SDK and Inno Setup 6:

```powershell
.\publish.ps1
```

This creates **`server-dist\CAMS-Server-Setup.exe`** — copy it to the server PC and run the wizard.

The wizard does everything:
- Installs to `%LOCALAPPDATA%\CAMS Server`
- Opens Windows Firewall port **5000**
- Optionally starts the server automatically with Windows
- Launches the server after install

**No database setup needed** — SQLite auto-creates `CAMS.db` next to `Server.exe` on first run.

## Publishing the client installer

From the same build machine:

```powershell
.\publish-client.ps1
```

This creates **`client-dist\CAMS-Client-Setup.exe`** — a self-contained installer (no .NET required on student PCs). Students run the wizard, enter the server address when asked, and the client is ready.

## Manual folder install (fallback)

If you can't build installers, do the folder-copy approach:

### Server

```powershell
cd "Monitoring And Remote Access"
dotnet publish Server\Server.csproj -c Release -o ..\publish
```

Copy the `publish\` folder to the server PC. Open `appsettings.json` and verify `DatabaseProvider` is set:

| Provider | Config | Notes |
|---|---|---|
| **Sqlite** (default) | `"DatabaseProvider": "Sqlite"` | Zero-install, file-based `CAMS.db` — recommended |
| **SqlServer** | `"DatabaseProvider": "SqlServer"` | Requires SQL Server or LocalDB |
| **MySql** | `"DatabaseProvider": "MySql"` | Requires MySQL server |

Then run `Server.exe` from the publish folder.

### Client

```powershell
cd "Monitoring And Remote Access\Client"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ..\..\client-publish
```

Copy `client-publish\` to each student PC. Edit `client-settings.json` with the server address, then run `Client.exe`.

## Firewall

The server installer opens port 5000 automatically. To do it manually:

```powershell
netsh advfirewall firewall add rule name="CAMS Server" dir=in action=allow protocol=TCP localport=5000
```

## URLs

| URL | Role |
|---|---|
| `http://<server-ip>:5000/Account/Login` | Login page |
| `http://<server-ip>:5000/Admin` | Admin dashboard |
| `http://<server-ip>:5000/Teacher/Monitoring` | Teacher monitoring panel |
| `http://<server-ip>:5000/Student` | Student portal |
| `http://<server-ip>:5000/remoteMonitoringHub` | SignalR hub (client connects here) |

## Default accounts (change these)

| Role | Username | Password |
|---|---|---|
| Admin | `admin` | `admin123` |
| Teacher | `teacher1` | `teacher123` |
| Student | `student1` | `student123` |

## Optional: run as a Windows Service

```powershell
sc create CAMS binPath= "%LOCALAPPDATA%\CAMS Server\Server.exe" start= auto
sc start CAMS
```