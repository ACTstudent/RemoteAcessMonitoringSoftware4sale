# 🖥️ CAMS — Computer Account Management System
### Pardo Elementary School Laboratory Management System

[![Build Status](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/actions/workflows/ci-full.yml/badge.svg)](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/actions/workflows/ci-full.yml)
[![Release Version](https://img.shields.io/github/v/release/ACTstudent/RemoteAcessMonitoringSoftware4sale?include_prereleases&color=blue)](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**CAMS** is a robust, LAN-based classroom management and real-time monitoring application built with **ASP.NET Core, SignalR, and WinForms**. It automates student account tracking, enforces strict laboratory access restrictions, and replaces traditional manual logbooks for 45-minute computer laboratory sessions.

---

## 📥 Quick Downloads (v2.5.2)

No .NET SDK or compilation required. Download the pre-built installer or source code package:

| Package | Download Link | Description / Target |
| :--- | :--- | :--- |
| **CAMS Server Setup** | [📥 CAMS-Server-Setup.exe](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Server-Setup.exe) | Teacher / Lab Control PC |
| **CAMS Student Client** | [📥 CAMS-Client-Setup.exe](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Client-Setup.exe) | Student Workstations |
| **Server Source Code** | [📦 CAMS-Server-Source.zip](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Server-Source.zip) | ASP.NET Core MVC Backend |
| **Client Source Code** | [📦 CAMS-Client-Source.zip](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Client-Source.zip) | WinForms Student Agent |

---

## 🚀 Core Features & System Scope

- **Real-Time Live Monitoring:** High-performance screen monitoring grid on the teacher dashboard via SignalR websockets.
- **LAN Auto-Discovery:** Automatic server broadcasting over UDP so student clients connect instantly without manual IP configuration.
- **Automatic Computer Profile Registration:** Student workstations automatically register and provision computer profiles on the server upon client connection.
- **Role-Based Access Control (RBAC):** Dedicated portals and permissions for Administrators, Teachers, and Students.
- **Lab Session Automation:** 45-minute lab session countdown timers, automated attendance logs, and workstation locking.
- **Security & Policy Enforcement:** Application and URL restriction blacklisting during active lab sessions.
- **Zero-Friction Deployment:** Self-contained installer wizards with automated database creation (`EnsureCreated()`) and Windows Firewall configuration.

---

## 📖 Installation & Usage

### 1. Server Setup (Teacher / Lab PC)
1. Download and run `CAMS-Server-Setup.exe`.
2. The installer automatically provisions the app in `%LOCALAPPDATA%\CAMS Server`, opens firewall ports `5000` & `5001`, and launches the dashboard.
3. Log in with default credentials:
   - **Admin:** `admin` / `admin123`
   - **Teacher:** `teacher1` / `teacher123`
   - **Student:** `student1` / `student123`

### 2. Client Setup (Student PCs)
1. Download and run `CAMS-Client-Setup.exe` on each student workstation.
2. The client auto-discovers the server on the LAN and connects automatically.
3. Students log in using their assigned credentials.

---

## 🛠️ Developer Guide (Build From Source)

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Inno Setup 6](https://jrsoftware.org/isdl.php)

### One-Shot Build Pipeline
Open PowerShell in the repository root and run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build-everything.ps1
```
This script automatically:
1. Executes unit tests (`xUnit`)
2. Builds the solution (`RemoteMonitoring.sln`)
3. Publishes server & client binaries
4. Compiles Inno Setup installers into `server-dist\` and `client-dist\`

---

## 📂 Repository Architecture

```text
CAMS/
├── CAMS-Guide.md              # Comprehensive user & developer guide
├── DEPLOYMENT.md              # Network deployment & silent install guide
├── build-everything.ps1       # Automated build & packaging script
├── test-installer.ps1         # Installer validation script
├── server-installer.iss       # Inno Setup configuration (Server)
├── client-installer.iss       # Inno Setup configuration (Client)
└── Monitoring And Remote Access/
    ├── RemoteMonitoring.sln   # Main solution file
    ├── Shared/                # DTOs & SignalR event contracts
    ├── Server/                # ASP.NET Core MVC + SignalR Hub + EF Core
    └── Client/                # WinForms Student Agent (.NET 8)
```

---

## 📝 License

Distributed under the **MIT License**. See `LICENSE` for details.
