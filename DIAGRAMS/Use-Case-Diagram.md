# CAMS Use Case Diagram

The actors use fixed roles. Every authenticated operation includes server-side role and object-scope validation; CAMS does not expose configurable RBAC.

```mermaid
flowchart LR
    ADMIN([Admin])
    TEACHER([Teacher])
    STUDENT([Student])
    CLIENT([Student at assigned Windows workstation])
    PUBLIC([Public visitor])

    subgraph PublicUse[Public release surface]
        P1[Read product/deployment guidance]
        P2[Download public release artifacts and hashes]
    end

    subgraph Auth[Authentication]
        L1[Browser login]
        L2[CLIENT login with PC name]
        L3[Verify password hash, role, active/lockout state]
        L4[Verify student-workstation assignment]
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

    subgraph TeacherUse[Teacher-scoped classroom operation]
        T1[Manage assigned classes and rosters]
        T2[Create/edit accessible student account]
        T3[Remove roster association but preserve account]
        T4[Edit accessible workstation name/status]
        T5[Start/pause/resume/end owned sessions]
        T6[Monitor accessible screens/status]
        T7[Send warning/broadcast/authorized commands]
        T8[Manage teacher-owned restrictions]
        T9[Manage scoped alerts and exports]
    end

    subgraph StudentUse[Student experience]
        S1[Use web session/alerts/account portal]
        S2[Enter assigned-workstation client session]
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
| Teacher | Only assigned/access-checked classes, students, computers, sessions, alerts, records, and teacher-owned restrictions. Cannot reassign teachers or globally delete a student through roster removal. |
| Student browser user | Can use Student portal without workstation identity; this is not a monitored CLIENT session. |
| Student CLIENT user | Must match the assigned workstation and then participates in monitoring, policy, and authorized command flows. |

Windows execution remains subject to the target environment. In particular, CAMS unlock cannot unlock the Windows secure desktop, 50 ms is a capture target rather than guaranteed FPS, and restart/shutdown/remote input must be validated under local policy.
