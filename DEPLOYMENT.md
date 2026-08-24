# Deployment Guide

## Easy Install (Recommended)

Download the installer wizards directly from the [latest release](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/latest).

Or build them from source on a machine with the .NET 8 SDK and Inno Setup 6:

```powershell
.\build-everything.ps1
```

This creates **`server-dist\CAMS-Server-Setup.exe`** and **`client-dist\CAMS-Client-Setup.exe`**, plus SHA-256 checksum files beside them. Verify the checksums before copying the installers to target PCs.

The wizard does everything:
- Installs to `%LOCALAPPDATA%\CAMS Server`
- Opens Windows Firewall HTTPS TCP port **5000** and UDP discovery port **5001**
- Optionally starts the server automatically with Windows
- Launches the server after install

**No database setup needed** — SQLite auto-creates `CAMS.db` next to `Server.exe` on first run.

## Publishing the client installer

The `build-everything.ps1` script builds both server and client installers. The client installer is **`client-dist\CAMS-Client-Setup.exe`** — a self-contained installer (no .NET required on student PCs). Students run the wizard, enter the server address when asked, and the client is ready.

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
| `https://<server-ip>:5000/Account/Login` | Login page |
| `https://<server-ip>:5000/Admin` | Admin dashboard |
| `https://<server-ip>:5000/Teacher/Monitoring` | Teacher monitoring panel |
| `https://<server-ip>:5000/Student` | Student portal |
| `https://<server-ip>:5000/remoteMonitoringHub` | SignalR hub (client connects here) |

## Initial account setup

Set `Cams__InitialAdminPassword` before the first server launch. CAMS does not ship passwords. To seed teacher and student accounts during startup, also set `Cams__SeededTeacherPassword` and `Cams__SeededStudentPassword`; otherwise create them through the authenticated admin portal. Existing accounts are never overwritten.

## Optional: run as a Windows Service

```powershell
sc create CAMS binPath= "%LOCALAPPDATA%\CAMS Server\Server.exe" start= auto
sc start CAMS
```
