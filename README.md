# CAMS — Computer Account Management System

A LAN-based classroom management, real-time screen monitoring, and computer laboratory control system built for **Pardo Elementary School (Cebu City)**.

[![Build Status](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/actions/workflows/ci-full.yml/badge.svg)](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/actions/workflows/ci-full.yml)
[![Release](https://img.shields.io/github/v/release/ACTstudent/RemoteAcessMonitoringSoftware4sale?include_prereleases&color=emerald)](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/latest)
[![.NET](https://img.shields.io/badge/.NET-8.0-blueviolet)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![UI: Dark Emerald](https://img.shields.io/badge/UI-Dark%20Emerald-0B3C26)](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

---

## 🌟 Overview

**CAMS** (Classroom Automated Monitoring System) is a complete LAN-based computer laboratory management and real-time screen streaming application built with **ASP.NET Core**, **SignalR**, and **WinForms (.NET 8)**. It replaces traditional manual logbooks with automated account tracking, timed 45-minute lab sessions, real-time screen stream monitoring, and centralized workstation control.

Featuring an official **Dark Emerald (`#0B3C26` / `#18181b` / `#10B981`) Design System** aligned with modern Figma specifications, CAMS provides dedicated, high-contrast, responsive portals for **Administrators**, **Teachers**, and **Students**.

---

## 📦 Downloads (v2.5.2)

Pre-built installer executables and source packages for the latest release:

| Package | Download Link | Target / Description |
| :--- | :--- | :--- |
| **CAMS Server Setup** | [`CAMS-Server-Setup.exe`](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Server-Setup.exe) | Teacher / Lab Control PC Installer (Includes Dark Emerald Web Portal) |
| **CAMS Student Client** | [`CAMS-Client-Setup.exe`](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Client-Setup.exe) | Student Workstation Agent Installer |
| **Server Source Code** | [`CAMS-Server-Source.zip`](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Server-Source.zip) | ASP.NET Core MVC + SignalR Source Package |
| **Client Source Code** | [`CAMS-Client-Source.zip`](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Client-Source.zip) | WinForms Student Agent Source Package |

---

## ✨ Key Features & UI System

### 🎨 Dark Emerald UI Design System (Figma Aligned)
- **Theme Palette**: Deep forest green (`#0B3C26`), slate dark (`#18181b`), emerald accents (`#10B981`), and soft slate backgrounds (`#F8FAFC`).
- **Typography & Assets**: `Plus Jakarta Sans` & `Inter` Google Fonts with official Pardo Elementary School branding assets.
- **Components**: Metric summary counters, high-contrast status badges (`Available`, `In Use`, `Maintenance`), rounded modals (`rounded-4`), search filter controls, and mobile navigation drawer toggles.

### 🖥 Real-Time Screen Monitoring & Workstation Control
- **Live Monitoring Grid**: High-frequency SignalR WebSocket screen streaming grid across all connected lab workstations.
- **Workstation Control**: Remote workstation lock/unlock, force user logout, shutdown, and reboot triggers.
- **Teacher Broadcast**: One-click screen broadcasting from the teacher's PC to all student monitors.
- **Infraction Alerting**: Automatic application tracking, idle state detection, and warning popup overlays.

### 📂 Integrated Class & Computer Profile Management
- **Class Management**: Section advisory setup, subject schedules, academic year tracking, and student roster enrollment.
- **Computer Profiles**: Station mapping (Station #, IP, MAC address, Assigned Room) and live station status badges.
- **Teacher & Student Accounts**: Full RBAC account directory with modal dialog creation and LRN/Student ID mapping.

### ⏱ Timed Lab Sessions & Security Policies
- **Lab Sessions**: Timed 45-minute laboratory sessions with real-time countdown timers and automated attendance logs.
- **Security Policies**: Application executable & website URL blacklists/whitelists enforced across workstations during active sessions.

---

## 🏗 Architecture

```text
┌──────────────────────────┐                        ┌──────────────────────┐
│       CAMS Server        │    UDP broadcast       │     CAMS Client      │
│    (Teacher / Lab PC)    │ ─────────────────────► │    (Student PC)      │
│                          │                        │                      │
│   ASP.NET Core MVC       │ ◄───────────────────── │   WinForms agent     │
│   Dark Emerald UI System │   SignalR websocket    │   Screen capture     │
│   SignalR WebSocket Hub  │     ports 5000/5001    │   Input simulation   │
│   EF Core + SQLite       │                        │   Infraction guard   │
└──────────────────────────┘                        └──────────────────────┘
```

- **Server**: Hosts the web portals (`/Admin`, `/Teacher`, `/Student`, `/Account/Login`), the `RemoteMonitoringHub` SignalR hub, and EF Core + SQLite.
- **Client**: Discovers the server via UDP broadcast, connects over a SignalR WebSocket, streams screen frames, reports active processes/idle states, and executes remote commands.
- **Shared**: DTO contracts and hub event signatures shared between Server and Client.

---

## ⚙️ Technology Stack

| Component | Technology |
| :--- | :--- |
| **Server Web App** | ASP.NET Core MVC (.NET 8), Razor Views, SignalR WebSockets |
| **UI Design System**| Dark Emerald CSS System (`#0B3C26` / `#10B981`), `Plus Jakarta Sans`, Bootstrap 5 |
| **Client Agent** | WinForms (.NET 8), self-contained desktop process |
| **Database** | SQLite via EF Core (`EnsureCreated()`) |
| **Discovery** | UDP Broadcast |
| **Tests & Packaging** | xUnit, Inno Setup 6 |

---

## 🚀 Quick Start & Login Credentials

### 1. Server Setup (Teacher / Lab PC)
1. Download and run [`CAMS-Server-Setup.exe`](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Server-Setup.exe).
2. The installer provisions the application to `%LOCALAPPDATA%\CAMS Server`, opens firewall ports `5000` & `5001`, and launches the web portal.
3. Default Portal Login Credentials:

| Portal / Role | Username | Password | Access Level |
| :--- | :--- | :--- | :--- |
| **Admin** | `admin` | `admin123` | Full system control, accounts, classes, LAN & audit rules |
| **Teacher** | `teacher1` | `teacher123` | Session control, live monitoring, workstation lock & records |
| **Student** | `student1` | `student123` | Session info, alert center, and unit status |

> **Security Note:** Change all default passwords before deploying on a production network.

### 2. Student Client Setup (Student PCs)
1. Download and run [`CAMS-Client-Setup.exe`](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Client-Setup.exe) on each workstation.
2. The client auto-discovers the CAMS server on the local network over UDP.

---

## 🛠 Building From Source

Prerequisites:
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (for compiling installer executables)

Execute the full build, unit test, and packaging pipeline from PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build-everything.ps1
```

The script will:
1. Run the xUnit test suite (`dotnet test`).
2. Build `RemoteMonitoring.sln` in Release mode.
3. Publish server and client binaries.
4. Compile setup executables into `server-dist\CAMS-Server-Setup.exe` and `client-dist\CAMS-Client-Setup.exe`.

---

## 📁 Repository Structure

```text
CAMS/
├── CAMS-Guide.md              # Comprehensive user & developer guide
├── DEPLOYMENT.md              # Network deployment & silent install guide
├── build-everything.ps1       # Automated build, test & packaging script
├── test-installer.ps1         # Installer validation script
├── server-installer.iss       # Inno Setup configuration (server)
├── client-installer.iss       # Inno Setup configuration (client)
└── Monitoring And Remote Access/
    ├── RemoteMonitoring.sln   # Main solution file
    ├── Shared/                # DTOs & SignalR event contracts
    ├── Server/                # ASP.NET Core MVC + Dark Emerald UI + SignalR hub + EF Core
    │   ├── wwwroot/css/       # site.css (Dark Emerald Design System)
    │   ├── wwwroot/images/    # pardo_logo.png (School Logo)
    │   ├── Views/Admin/       # Admin Portal Views & Layout
    │   ├── Views/Teacher/     # Teacher Portal Views, Live Grid & Layouts
    │   ├── Views/Student/     # Student Portal Views & Layout
    │   └── Views/Account/     # Glassmorphic Login View
    ├── Client/                # WinForms student agent (.NET 8)
    └── Server.Tests/          # xUnit test suite
```

---

## 📄 License

Distributed under the **MIT License**. See [`LICENSE`](LICENSE) for details.
