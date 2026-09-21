# CAMS Use Case Diagram

An editable draw.io copy sits beside this file: [`CAMS-Use-Case-Diagram.drawio`](CAMS-Use-Case-Diagram.drawio). One page, 41 module boxes, 219 use cases, with `<<include>>` and `<<extend>>` arrows. Each use case is named after the function that implements it - a controller action, a hub method, or a service method where the behaviour has no endpoint of its own - so an ellipse reading CREATE STUDENT is `AdminController.CreateStudent` and one reading LOGIN ASYNC is `AuthenticationService.LoginAsync`. The module caption names the declaring type. No use case is a bare noun: where the function name is a single word the caption carries the verb that action performs, so `AdminController.Teachers` reads VIEW TEACHERS and `AdminDeploymentController.Installer` reads DOWNLOAD INSTALLER. Captions run from two to four words. Each box carries a single actor standing outside it on the left. Open it at [app.diagrams.net](https://app.diagrams.net) with **File > Open From > Device**.

Coverage is checked rather than assumed: a script walks every controller action and every hub method and maps each one to a use case, so an action nobody has accounted for shows up as a gap. It currently reports full coverage of **180 distinct actions and hub methods**, with four deliberately excluded - `Error` and `AccessDenied` are error pages rather than actor goals, and `OnConnectedAsync` and `OnDisconnectedAsync` are SignalR lifecycle callbacks nobody initiates.

The actors use fixed roles. Every authenticated operation includes server-side role and object-scope validation; CAMS does not expose configurable RBAC.

There are exactly three actors: **Administrator**, **Teacher** and **Student**. The division between the administrator and teacher bands is read off the code rather than assumed: `AdminController` is `[Authorize(Roles = AdminOrTeacher)]`, and its authorization filter admits a teacher only to actions marked `[TeacherSharedAction]`. Fifty-six of its actions carry that attribute, so the teacher band repeats the whole shared administration surface - peer teacher accounts, student accounts, workstations, classes, rosters, restriction rules, lists, categories, session rules, and lab-wide pause, resume and end. The administrator band keeps what is not shared: administrator accounts, roles, LAN configuration, reports, audit and system logs, and everything in `AdminDatabaseController` and `AdminDeploymentController`.

One asymmetry is worth knowing because it looks like a mistake and is not: the administrator can pause, resume and end a laboratory-wide session but cannot start one. `AdminController` exposes `PauseAllSessions`, `ResumeAllSessions` and `EndAllSessions` and no start; `GlobalStartSession` lives on `TeacherController`. The diagram follows the code.

The workstation client and the hosted background workers are **not** actors. They are parts of the system, so what they do is drawn as included behaviour of the case a person actually starts: capturing application, website and idle telemetry is included by the student's *Work at a monitored workstation*, discovering the server is included by *Log in at the workstation*, and ending a session that has run past its limit is included by *Apply the governing session rule*.

```mermaid
flowchart LR
    ADMIN([Admin])
    TEACHER([Teacher])
    STUDENT([Student])
    CLIENT([Student at Windows workstation])
    PUBLIC([Public visitor])

    subgraph PublicUse[Public release surface]
        P1[Read product/deployment guidance]
        P2[Download public release artifacts and hashes]
    end

    subgraph Auth[Authentication]
        L1[Browser login]
        L2[CLIENT login with PC name]
        L3[Verify password hash, role, active/lockout state]
        L4[Create or safely reassign workstation]
        L1 -. includes .-> L3
        L2 -. includes .-> L3
        L2 -. includes .-> L4
    end

    subgraph AdminUse[Global administration]
        A1[Manage teacher/student accounts and lockouts]
        A2[Manage classes, rosters, computers, mappings]
        A3[Manage global restrictions and session rules]
        A4[Pause, resume, or end lab-wide sessions]
        A5[Review/export reports, alerts, audit, system records]
        A6[Back up/validate/stage restore of SQLite]
        A7[View detected read-only LAN Status]
        A8[Use authenticated Deployment Hub]
        A9[Validate versions, hashes, certificate, endpoints]
        A10[Create offline client bundle and confirm clients]
        A8 -. includes .-> A9
        A10 -. extends .-> A8
    end

    subgraph TeacherUse[Teacher classroom operation]
        T1[Manage global classes and rosters via shared Admin actions]
        T2[Manage global student and peer teacher accounts]
        T3[Remove roster association but preserve account]
        T4[Manage global workstations via shared Admin actions]
        T5[Start/pause/resume/end owned sessions]
        T6[Monitor all connected student screens/status]
        T7[Send warning/broadcast/authorized commands]
        T8[Manage global policies via shared Admin actions]
        T9[Manage scoped alerts and exports]
        T10[Lab-wide pause/resume/end]
    end

    subgraph StudentUse[Student experience]
        S1[Use web session/alerts/account portal]
        S2[Enter workstation-registered client session]
        S3[See timer/session state]
        S4[Receive CAMS topmost dialogs and policies]
    end

    PUBLIC --> P1
    PUBLIC --> P2
    ADMIN --> L1
    TEACHER --> L1
    STUDENT --> L1
    CLIENT --> L2
    ADMIN --> A1
    ADMIN --> A2
    ADMIN --> A3
    ADMIN --> A4
    ADMIN --> A5
    ADMIN --> A6
    ADMIN --> A7
    ADMIN --> A8
    TEACHER --> T1
    TEACHER --> T2
    TEACHER --> T3
    TEACHER --> T4
    TEACHER --> T5
    TEACHER --> T6
    TEACHER --> T7
    TEACHER --> T8
    TEACHER --> T9
    TEACHER --> T10
    STUDENT --> S1
    CLIENT --> S2
    CLIENT --> S3
    CLIENT --> S4
```

## Scope And Limits

| Actor | Boundary |
| --- | --- |
| Public visitor | Receives public packages and documentation only. No local root CER, PFX, credentials, offline bundle, or Admin access. |
| Admin | Global controls and deployment administration through the UI. LAN Status is read-only and does not configure DHCP/DNS/network adapters. |
| Teacher | Active teachers can monitor and control all connected student clients and use lab-wide pause/resume/end actions. Explicitly shared `/Admin/...` actions provide global account, class, roster, workstation and policy management. Individual session actions, older `/Teacher/...` management pages, and analytics/record queries retain their teacher or adviser/class checks. Admin-only administration remains restricted. |
| Student browser user | Can use Student portal without workstation identity; this is not a monitored CLIENT session. |
| Student CLIENT user | Registers or safely reassigns the workstation, rejects conflicting active use, and then participates in monitoring, policy, and authorized command flows. |

Windows execution remains subject to the target environment. In particular, CAMS unlock cannot unlock the Windows secure desktop, 50 ms is a capture target rather than guaranteed FPS, and restart/shutdown/remote input must be validated under local policy.
