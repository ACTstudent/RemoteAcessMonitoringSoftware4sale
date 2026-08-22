# CAMS — Computer Account Management System

A LAN-based classroom management and real-time monitoring system for school computer laboratories.

[![Build Status](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/actions/workflows/ci-full.yml/badge.svg)](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/actions/workflows/ci-full.yml)
[![Release](https://img.shields.io/github/v/release/ACTstudent/RemoteAcessMonitoringSoftware4sale?include_prereleases&color=blue)](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/latest)
[![.NET](https://img.shields.io/badge/.NET-8.0-blueviolet)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Overview

CAMS is a LAN-based classroom management and monitoring application for computer laboratories, built with **ASP.NET Core**, **SignalR**, and **WinForms**. It replaces manual logbooks with automated account tracking, timed lab sessions, and centralized workstation control.

The system serves three roles: **Administrators** manage accounts, classes, and policy; **Teachers** run sessions and monitor student screens in real time; **Students** connect through a lightweight client that auto-discovers the server on the local network.

## Downloads (v2.5.2)

Pre-built installer executables and source packages for the latest release:

| Package | Download Link | Target / Description |
| :--- | :--- | :--- |
| **CAMS Server Setup** | [`CAMS-Server-Setup.exe`](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Server-Setup.exe) | Teacher / Lab Control PC Installer |
| **CAMS Student Client** | [`CAMS-Client-Setup.exe`](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Client-Setup.exe) | Student Workstation Agent Installer |
| **Server Source Code** | [`CAMS-Server-Source.zip`](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Server-Source.zip) | ASP.NET Core MVC + SignalR Source Package |
| **Client Source Code** | [`CAMS-Client-Source.zip`](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Client-Source.zip) | WinForms Student Agent Source Package |

## Key Features

**Monitoring**
- Real-time screen monitoring grid via SignalR websockets
- Active application tracking and idle/active status detection
- Restriction-violation alerts on the teacher dashboard

**Workstation Control**
- Lock, unlock, force logout, and shutdown from the teacher dashboard
- Remote access to student workstations with mouse and keyboard input
- Teacher screen broadcast to all connected students

**Sessions & Policy**
- 45-minute lab sessions with countdown timers and automated attendance logs
- Application and URL blacklisting during active sessions

**Management**
- Role-based access control (RBAC) with dedicated portals for Admins, Teachers, and Students
- Automatic computer profile registration when student clients connect
- LAN auto-discovery over UDP — no manual IP configuration

**Deployment**
- Self-contained installer wizards with automated database creation (`EnsureCreated()`) and Windows Firewall configuration

## Architecture

```text
┌──────────────────────────┐                        ┌──────────────────────┐
│       CAMS Server        │    UDP broadcast       │     CAMS Client      │
│    (Teacher / Lab PC)    │ ─────────────────────► │    (Student PC)      │
│                          │                        │                      │
│   ASP.NET Core MVC       │ ◄───────────────────── │   WinForms agent     │
│   SignalR hub            │   SignalR websocket    │   Screen capture     │
│   EF Core + SQLite       │     ports 5000/5001    │   Input simulation   │
└──────────────────────────┘                        └──────────────────────┘
```

- **Server** hosts the web dashboards, the `RemoteMonitoringHub` SignalR hub, and a SQLite database created automatically on first startup.
- **Client** discovers the server via UDP broadcast, connects over a SignalR websocket, streams screen frames, reports the active application and idle state, and executes remote commands.
- **Shared** contains the DTOs and hub event contracts used by both sides.

## Technology Stack

| Component           | Technology                                |
| :------------------ | :---------------------------------------- |
| Server              | ASP.NET Core MVC (.NET 8), SignalR        |
| Client              | WinForms (.NET 8), self-contained         |
| Database            | SQLite via EF Core (`EnsureCreated()`)    |
| Real-time transport | SignalR WebSockets                        |
| Discovery           | UDP broadcast                             |
| Tests               | xUnit                                     |
| Packaging           | Inno Setup 6                              |

## Installation

### Server (Teacher / Lab PC)

1. Download and run [`CAMS-Server-Setup.exe`](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Server-Setup.exe).
2. The installer provisions the app to `%LOCALAPPDATA%\CAMS Server`, opens firewall ports `5000` and `5001`, and launches the dashboard.
3. Log in with a default account:

| Role    | Username   | Password     |
| :------ | :--------- | :----------- |
| Admin   | `admin`    | `admin123`   |
| Teacher | `teacher1` | `teacher123` |
| Student | `student1` | `student123` |

> **Security note:** Change all default passwords before deploying on a production network.

### Student Client (Student PCs)

1. Download and run [`CAMS-Client-Setup.exe`](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/download/v2.5.2/CAMS-Client-Setup.exe) on each workstation.
2. The client auto-discovers the server on the LAN and connects automatically.
3. Students log in with their assigned credentials.

## Build From Source

Prerequisites:

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Inno Setup 6](https://jrsoftware.org/isdl.php)

Run the full pipeline from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build-everything.ps1
```

The script runs the unit tests (`xUnit`), builds `RemoteMonitoring.sln`, publishes the server and client binaries, and compiles the Inno Setup installers into `server-dist\` and `client-dist\`.

## Repository Structure

```text
CAMS/
├── CAMS-Guide.md              # Comprehensive user & developer guide
├── DEPLOYMENT.md              # Network deployment & silent install guide
├── build-everything.ps1       # Automated build & packaging script
├── test-installer.ps1         # Installer validation script
├── server-installer.iss       # Inno Setup configuration (server)
├── client-installer.iss       # Inno Setup configuration (client)
└── Monitoring And Remote Access/
    ├── RemoteMonitoring.sln   # Main solution file
    ├── Shared/                # DTOs & SignalR event contracts
    ├── Server/                # ASP.NET Core MVC + SignalR hub + EF Core
    ├── Client/                # WinForms student agent (.NET 8)
    └── Server.Tests/          # xUnit test suite
```

## Network Deployment

Requirements:

- Server and client machines on the same LAN or subnet.
- Inbound firewall rules for TCP `5000` and `5001` on the server (configured by the installer).
- UDP discovery traffic permitted between the server and clients.

```text
Teacher PC (Server) ── UDP discovery broadcast ──► Student PCs (Clients)
        │                                                │
        └───────────── SignalR :5000 / :5001 ────────────┘
```

See [`DEPLOYMENT.md`](DEPLOYMENT.md) for silent installation and lab-wide rollout instructions.

## Testing

Run the test suite directly:

```powershell
dotnet test "Monitoring And Remote Access/RemoteMonitoring.sln"
```

Validate a built installer:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File test-installer.ps1
```

## Documentation

- [`CAMS-Guide.md`](CAMS-Guide.md) — comprehensive user and developer guide
- [`DEPLOYMENT.md`](DEPLOYMENT.md) — network deployment and silent installation guide

## License

Distributed under the **MIT License**. See [`LICENSE`](LICENSE) for details.
