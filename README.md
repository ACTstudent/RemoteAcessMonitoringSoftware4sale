# CAMS — Computer Account Management System

A LAN-based classroom management, real-time screen monitoring, and computer laboratory control system built for **Pardo Elementary School (Cebu City)**.

[![Build Status](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/actions/workflows/ci-full.yml/badge.svg)](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/actions/workflows/ci-full.yml)
[![Release](https://img.shields.io/badge/Release-v2.5.6-emerald.svg)](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale)
[![.NET](https://img.shields.io/badge/.NET-8.0-blueviolet)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![UI: Dark Emerald](https://img.shields.io/badge/UI-Dark%20Emerald-0B3C26)](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

---

## 🌟 Overview

**CAMS** (Classroom Automated Monitoring System) is a LAN-based computer laboratory management and real-time screen streaming application built with **ASP.NET Core**, **SignalR**, and **WinForms (.NET 8)**. It replaces manual logbooks with account tracking, configurable timed lab sessions, real-time screen monitoring, and centralized workstation control.

Featuring an official **Dark Emerald (`#0B3C26` / `#18181b` / `#10B981`) Design System** aligned with modern Figma specifications and exact `pro` project styling, CAMS provides dedicated, high-contrast, responsive portals for **Administrators**, **Teachers**, and **Students**.

---

## 📦 Installer Downloads (v2.5.6 Release Assets)

Pre-built installer setup executables published as versioned GitHub release assets:

| Package | Direct Download Link | Repository File Path | Target / Description | File Size |
| :--- | :--- | :--- | :--- | :--- |
| **CAMS Server Setup** | [📥 **Download CAMS-Server-Setup.exe**](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.6/CAMS-Server-Setup.exe) | [Release v2.5.6](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/tag/v2.5.6) | Teacher / Lab Control PC Installer | ~15 MB |
| **CAMS Student Client** | [📥 **Download CAMS-Client-Setup.exe**](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.6/CAMS-Client-Setup.exe) | [Release v2.5.6](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/tag/v2.5.6) | Student Workstation Agent Installer | ~64 MB |

---

## ✨ Key Features & Primary Logic Systems

### 🔑 Primary Multi-Role Login Authentication (`pro` Aligned)
- **Role Routing**: Single entry point (`/Account/Login`) supporting **Admin**, **Teacher**, and **Student** login credentials.
- **Session Management**: Automatically stores `Username`, `Role`, `AdminId`/`TeacherId`/`StudentId`, and `DisplayName` sessions for seamless authorization and auditing.
- **Account setup**: The testing release seeds `admin1` / `admin123` and `student1` / `student123`; these credentials are not suitable for production. Replace the values in `appsettings.json` or set protected `Cams__InitialAdminPassword` and `Cams__SeededStudentPassword` values before production deployment. Teacher seeding remains optional through `Cams__SeededTeacherPassword`.

### 🎨 Dark Emerald UI Design System (Figma & `pro` Aligned)
- **Theme Palette**: Deep forest green (`#0B3C26`), slate dark (`#18181b`), emerald accents (`#10B981`), and soft slate backgrounds (`#F8FAFC`).
- **Typography & Assets**: `Plus Jakarta Sans` & `Inter` Google Fonts with official Pardo Elementary School branding assets.
- **Components & Overrides**: Metric summary counters (`.metric-card`, `.metric-card-emerald`), class section cards (`.class-card`), search filter controls (`.filter-control`), rounded modals (`rounded-4`), and explicit button overrides (`.btn-primary`, `.btn-success`, `.btn-dark`).

### 🖥 Real-Time Screen Monitoring & Workstation Control
- **Live Monitoring Grid**: Authenticated HTTPS SignalR screen streaming across connected lab workstations, approximately 12 FPS by default.
- **Workstation Control**: Remote workstation lock/unlock, force user logout, shutdown, and reboot triggers.
- **Teacher Broadcast**: One-click screen broadcasting from the teacher's PC to all student monitors.
- **Infraction Alerting**: Automatic application tracking, idle state detection, and warning popup overlays.

### 📂 Integrated Class & Computer Profile Management (`pro` Relationships)
- **`Class` Section Model**: Section Name, Academic Year (`2026-2027`), Status (`Active`/`Archived`), assigned `Teacher` (Adviser/Instructor), and direct `Students` roster collection.
- **`Student` Record Model**: `FirstName`, `LastName`, `FullName`, `Username`, `PasswordHash`, `Status`, `GradeSection`, Foreign Keys to `Class` and `Teacher` (Adviser).
- **`Teacher` Record Model**: `FirstName`, `LastName`, `Email`, `ContactNumber`, `Status`, navigation collections for advised `Students` and assigned `Classes`.
- **Complete CRUD Operations**: Single/Bulk student registration, class section creation/editing, instructor assignment, student unenrollment, and section archiving in Admin (`/Admin/Classes`) and Teacher (`/Teacher/Classes`) portals.

### ⏱ Timed Lab Sessions & Security Policies
- **Lab Sessions**: Configurable timed laboratory sessions with real-time countdown timers and attendance logs.
- **Security Policies**: Application executable & website URL blacklists/whitelists enforced across workstations during active sessions.

### 🛡️ Clean Installation & Crash Prevention Features
- **Clean Installation (`cleaninstall`)**: Installers automatically close running processes and clean out old binary files, cached DLLs, static assets, and `wwwroot` folders before extracting new builds.
- **Launch Crash Prevention**: `AppContext.BaseDirectory` working directory enforcement prevents launch failures when executing `Server.exe` from Desktop icons or custom shortcuts.
- **Safe SQLite Database Initialization**: Safe startup handlers prevent database lock crashes on launch.

---

## 🏗 Architecture

```text
┌──────────────────────────┐                        ┌──────────────────────┐
│       CAMS Server        │    UDP broadcast       │     CAMS Client      │
│    (Teacher / Lab PC)    │ ─────────────────────► │    (Student PC)      │
│                          │                        │                      │
│   ASP.NET Core MVC       │ ◄───────────────────── │   WinForms agent     │
│   Dark Emerald UI System │ HTTPS SignalR :5000   │   Screen capture     │
│   SignalR WebSocket Hub  │ UDP discovery :5001   │   Input simulation   │
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
| **Tests & Packaging** | xUnit in CI, Inno Setup 6 |

---

## 🚀 Quick Start & Login Credentials

### 1. Server Setup (Teacher / Lab PC)
1. Set the protected `Cams__InitialAdminPassword` environment variable before the first launch.
2. Download and run [`CAMS-Server-Setup.exe`](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.6/CAMS-Server-Setup.exe).
3. The installer performs a clean installation to `%LOCALAPPDATA%\CAMS Server`, opens HTTPS TCP port `5000` and UDP discovery port `5001`, and launches the web portal.
4. Configure a trusted production certificate with `Cams__CertificatePath` and `Cams__CertificatePassword`.

### 2. Student Client Setup (Student PCs)
1. Download and run [`CAMS-Client-Setup.exe`](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.6/CAMS-Client-Setup.exe) on each workstation.
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
5. Generate SHA-256 checksum files beside both installers for release verification.

---

## 📁 Repository Structure

```text
CAMS/
├── CAMS-Guide.md              # Comprehensive user & developer guide
├── DEPLOYMENT.md              # Network deployment & silent install guide
├── build-everything.ps1       # Automated build, test & packaging script
├── test-installer.ps1         # Installer validation script
├── server-installer.iss       # Inno Setup configuration (server) with Clean Install
├── client-installer.iss       # Inno Setup configuration (client) with Clean Install
└── Monitoring And Remote Access/
    ├── RemoteMonitoring.sln   # Main solution file
    ├── Shared/                # DTOs & SignalR event contracts
    ├── Server/                # ASP.NET Core MVC + Dark Emerald UI + SignalR hub + EF Core
    │   ├── wwwroot/css/       # site.css (Dark Emerald & pro Design System)
    │   ├── wwwroot/images/    # pardo_logo.png (School Logo)
    │   ├── Views/Admin/       # Admin Portal Views & Layout
    │   ├── Views/Teacher/     # Teacher Portal Views, Live Grid, Class Management & Layouts
    │   ├── Views/Student/     # Student Portal Views & Layout
    │   └── Views/Account/     # Glassmorphic Login View
    ├── Client/                # WinForms student agent (.NET 8)
    └── Server.Tests/          # xUnit test suite
```

---

## 📄 License

Distributed under the **MIT License**. See [`LICENSE`](LICENSE) for details.
