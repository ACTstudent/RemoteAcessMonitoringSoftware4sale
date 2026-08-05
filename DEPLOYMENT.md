# Deployment Guide (Server Side)

How to install and run the CAMS server on a lab machine.

## 1. Install prerequisites on the server PC

1. **.NET 8 Hosting Bundle / SDK** — https://dotnet.microsoft.com/download/dotnet/8.0
2. **SQL Server** (Express is fine) — https://www.microsoft.com/sql-server/sql-server-downloads

## 2. Publish the server

Run `.\publish.ps1` from the repo root (or):

```powershell
cd "Monitoring And Remote Access"
dotnet publish Server\Server.csproj -c Release -o ..\publish
```

This creates a `publish\` folder containing `Server.exe` and all dependencies.

## 3. Copy to the server PC

Copy the entire `publish\` folder to the server, e.g. `C:\CAMS\publish`.

## 4. Configure the database

Edit `publish\appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=LAB-PC\\SQLEXPRESS;Database=MonitoringDb;Trusted_Connection=True;MultipleActiveResultSets=true"
  }
}
```

Replace `LAB-PC\SQLEXPRESS` with your SQL Server instance name.

Create/apply the database schema (run once, from the machine with the SDK and the source):

```powershell
cd "Monitoring And Remote Access"
dotnet tool install --global dotnet-ef
dotnet ef database update --project Server
```

## 5. Run the server

```powershell
cd C:\CAMS\publish
.\Server.exe
```

The server listens on `http://localhost:5000` and exposes:
- Admin portal: `http://<server-ip>:5000/Admin`
- Teacher portal: `http://<server-ip>:5000/Teacher`
- SignalR hub: `http://<server-ip>:5000/remoteMonitoringHub`

## 6. Point the client at the server

Edit `Client\MainForm.cs` and set:

```csharp
private const string ServerUrl = "http://<server-ip>:5000/remoteMonitoringHub";
```

Rebuild and redeploy the client.

## Optional: run as a Windows Service

So the server survives reboots and starts automatically:

```powershell
sc create CAMS binPath= "C:\CAMS\publish\Server.exe" start= auto
sc start CAMS
```

Or use IIS with the ASP.NET Core Module.

## Firewall

Allow inbound TCP port **5000** on the server so LAN clients can connect:

```powershell
netsh advfirewall firewall add rule name="CAMS" dir=in action=allow protocol=TCP localport=5000
```
