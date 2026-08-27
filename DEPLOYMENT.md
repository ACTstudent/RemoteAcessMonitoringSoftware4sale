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
- Refreshes Windows Firewall HTTPS TCP port **5000** and UDP discovery port **5001** rules
- Optionally starts the server automatically with Windows
- Launches the server after install
- Generates a local CA and a LAN HTTPS certificate on first server start

**No database setup needed** — SQLite auto-creates `CAMS.db` next to `Server.exe` on first run.

## Secure LAN Certificate Setup

The server generates these files in `%LOCALAPPDATA%\CAMS Server` when no production certificate is configured:

- `CAMS-Server-Root.cer` — public trust certificate to copy to student PCs
- `CAMS-Server.cer` — public server certificate for inspection
- `certificates\CAMS-Server-Root.pfx` — private CA key; never distribute it
- `certificates\CAMS-Server.pfx` — private server key; never distribute it

Copy only `CAMS-Server-Root.cer` to each student PC. The client installer has a **Server Trust** page where the teacher can select that file. It imports the certificate into the current user's Windows root store; no private key is copied to the student PC.

For a production certificate, set `Cams__CertificatePath` and `Cams__CertificatePassword`. The certificate must contain the hostname or LAN IP used by the client in its Subject Alternative Name list.

## Publishing the client installer

The `build-everything.ps1` script builds both server and client installers. The client installer is **`client-dist\CAMS-Client-Setup.exe`** — a self-contained installer (no .NET required on student PCs). Students run the wizard, enter the server address, and select the copied `CAMS-Server-Root.cer` file.

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

Copy `client-publish\` to each student PC. Copy `CAMS-Server-Root.cer` beside the folder, install it into the current user's Trusted Root Certification Authorities store, edit `client-settings.json` with `https://<server-ip>:5000/remoteMonitoringHub`, then run `Client.exe`. Start or restart `Server.exe` after connecting the server PC to the target Wi-Fi so its certificate contains the current LAN address.

## Firewall

The server installer refreshes the named rules automatically. To do it manually:

```powershell
netsh advfirewall firewall delete rule name="CAMS Server"
netsh advfirewall firewall add rule name="CAMS Server" dir=in action=allow protocol=TCP localport=5000 profile=any
netsh advfirewall firewall delete rule name="CAMS Discovery"
netsh advfirewall firewall add rule name="CAMS Discovery" dir=in action=allow protocol=UDP localport=5001 profile=any
```

For a Wi-Fi network or cellphone hotspot, use the teacher PC's Wi-Fi IPv4 address from `ipconfig`. Both PCs must be on the same non-guest network subnet. If the network blocks client-to-client traffic or UDP broadcast, disable client isolation if possible and use the manual server URL in the client setup; TCP port 5000 must still be reachable.

## URLs

| URL | Role |
|---|---|
| `https://<server-ip>:5000/Account/Login` | Login page |
| `https://<server-ip>:5000/Admin` | Admin dashboard |
| `https://<server-ip>:5000/Teacher/Monitoring` | Teacher monitoring panel |
| `https://<server-ip>:5000/Student` | Student portal |
| `https://<server-ip>:5000/remoteMonitoringHub` | SignalR hub (client connects here) |

## Initial account setup

The testing release seeds `admin1` / `admin123` and `student1` / `student123`. Replace those values with protected configuration before production deployment. Teacher accounts can be seeded with `Cams__SeededTeacherPassword`; otherwise create them through the authenticated admin portal. Existing accounts are never overwritten.

## Optional: run as a Windows Service

```powershell
sc create CAMS binPath= "%LOCALAPPDATA%\CAMS Server\Server.exe" start= auto
sc start CAMS
```
