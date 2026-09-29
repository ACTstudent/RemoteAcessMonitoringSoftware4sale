# CAMS Use Case Diagram

An editable draw.io copy sits beside this file: [`CAMS-Use-Case-Diagram.drawio`](CAMS-Use-Case-Diagram.drawio). One page, 38 module boxes, 182 use cases, with `<<include>>` and `<<extend>>` arrows. Every use case is strict verb-noun: an imperative verb first, then the noun it acts on. Each one still maps to the function that implements it, and the module caption names the declaring type - an ellipse reading `CREATE STUDENT` is `AdminController.CreateStudent` - but where the identifier is a bare noun, a noun phrase, or puts its modifier in front of the verb, the caption supplies the word order a reader expects: `AdminController.Teachers` reads `VIEW TEACHERS`, `LabUtilization` reads `VIEW LAB UTILIZATION`, and `GlobalEndSession` reads `END LAB SESSIONS`. No use case deletes anything. Where a record can be taken out of use the case is `TOGGLE ... STATUS` - `TOGGLE STUDENT STATUS` is `AdminController.SetAccountActive`, `TOGGLE RESTRICTION STATUS` is the rule's Active switch in `UpdateRestriction`, `TOGGLE CLASS STATUS` is `ArchiveClass`, which archives or restores - so the record and its history are kept and the change can be undone. The delete actions the server still exposes are not drawn. Captions run from two to four words; none is a single word, and none names a threading convention - the `*Async` service methods that used to appear as `<<include>>` of their own callers have been removed. Each box carries a single actor standing outside it on the left. The drawing itself carries no figure numbers - those belong to the written specifications, where each module is reproduced under a numbered caption (*Figure 3.3: System Use Case for Manage Admin Account*) in both the Word and PDF copies. Open it at [app.diagrams.net](https://app.diagrams.net) with **File > Open From > Device**.

Only interactions between a person and the system are drawn. Behaviour CAMS performs on its own is part of the use case a person starts, not a use case of its own: the monitoring wall polling `LiveState` and receiving `SendScreenFrame` from each client, the navigation badge polling `OpenAlertCount`, the lab status polling `GlobalSessionState`, the JSON feed `ActivityTimeline`, and the client agent pinging the server (`DeploymentPingController.Get`) and discovering it (`ServerDiscoveryClient.DiscoverAsync`) are therefore not drawn. A *VIEW* use case is drawn only where the person opens a page to read something - a list, a report, a log, a history - never for data a page fetches by itself, and never twice for the same page.

Coverage is checked rather than assumed: a script walks every controller action and every hub method and maps each one to a use case, so an action nobody has accounted for shows up as a gap. Beyond the delete actions and the system's own background calls described above, seventeen actions are deliberately excluded. `Error` and `AccessDenied` are error pages rather than actor goals, and `OnConnectedAsync` and `OnDisconnectedAsync` are SignalR lifecycle callbacks nobody initiates. One is `TeacherController.ExportRecordsCsv`: the endpoint works, but nothing in the interface calls it - no link, no form, no script - and the only Export button on the Records page runs `window.print()`. A use case nobody can reach is not a use case, so it is not drawn. Five are the whole of `StudentController` - `Index`, `Alerts`, `MarkRead`, `Settings` and `ResetPassword` - because the student web portal is closed by design: `AccountController.Login` answers a student with *"This portal is for teachers and administrators. Sign in on the CAMS Student Client on your workstation instead"* and never signs them in, and nothing anywhere sets the `StudentId` session key those actions read. A student reaches CAMS only through the client agent, so the student band carries only the client's use cases. The last seven are the hub methods the client agent calls on its own - `FetchRestrictions`, `ReportActiveApp`, `ReportWebsiteActivity`, `ReportIdleStatus`, `ReportBrowserMonitoringStatus`, `ReportTelemetryBatch` and `ReportInfraction`. No student starts them: the agent runs them in the background for as long as a student is signed in, fetching the rules it enforces and sending the activity the teacher sees, so they are the system's behaviour rather than any actor's goal, and they are not drawn. What the student does start from the client - changing their own password through `ClientAuthController.ChangePassword` - is drawn, as *MANAGE OWN ACCOUNT*.

That coverage script walks the **server** project alone - its controllers and its hub. The four use cases in *USE THE CLIENT AGENT* and *SET SERVER ADDRESS* have no controller action behind them: they are Windows Forms handlers in the client project (`MainForm.RestoreFromTray`, `MainForm.ShowTrayStatus`, `MainForm.ExitFromTray`, `MainForm.ShowServerUrlDialog`), and their specifications are marked CLIENT rather than given an HTTP verb they do not have. They were read off the client source by hand, so unlike the server side they are not machine-checked for completeness.

The actors use fixed roles. Every authenticated operation includes server-side role and object-scope validation; CAMS does not expose configurable RBAC.

There are exactly three actors: **Administrator**, **Teacher** and **Student**. The division between the administrator and teacher bands is read off the code rather than assumed: `AdminController` is `[Authorize(Roles = AdminOrTeacher)]`, and its authorization filter admits a teacher only to actions marked `[TeacherSharedAction]`. Fifty-six of its actions carry that attribute, so the teacher band repeats the whole shared administration surface - peer teacher accounts, student accounts, workstations, classes, rosters, restriction rules, lists, categories, session rules, and lab-wide pause, resume and end. The administrator band keeps what is not shared: administrator accounts, roles, LAN configuration, reports, audit and system logs, and everything in `AdminDatabaseController` and `AdminDeploymentController`.

One asymmetry is worth knowing because it looks like a mistake and is not: the administrator can pause, resume and end a laboratory-wide session but cannot start one. `AdminController` exposes `PauseAllSessions`, `ResumeAllSessions` and `EndAllSessions` and no start; `GlobalStartSession` lives on `TeacherController`. The diagram follows the code.

The workstation client and the hosted background workers are **not** actors. They are parts of the system, so what they do is drawn as included behaviour of the case a person actually starts: capturing application, website and idle telemetry runs inside the client for as long as a student is signed in and is not drawn at all, discovering the server is included by *Log in at the workstation*, and ending a session that has run past its limit is included by *Apply the governing session rule*.

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
        S5[Change own password in the client]
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
    CLIENT --> S5
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
