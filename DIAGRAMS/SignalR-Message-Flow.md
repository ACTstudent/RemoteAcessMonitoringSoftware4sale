# CAMS SignalR Message Flow

Operational events use authenticated HTTPS SignalR on server TCP `5000`. UDP `5001` is separate, optional server-to-client discovery and is not part of the hub flow.

```mermaid
sequenceDiagram
    participant C as Student WinForms Client
    participant API as HTTPS Client Login API
    participant H as RemoteMonitoringHub
    participant T as Teacher Dashboard
    participant A as Admin Dashboard
    participant DB as SQLite

    C->>API: Credentials plus PC name
    API->>DB: Validate hash, active account, assigned station
    alt Invalid credentials or station
        API-->>C: Reject and audit
    else Valid assigned workstation
        API->>DB: Create or resume active LabSession
        API-->>C: Auth cookie and student identity
        C->>H: Authenticated connect as client agent
        T->>H: Authenticated connect as Teacher
        A->>H: Authenticated connect as Admin
        H-->>T: StudentConnected if teacher is authorized
        H-->>A: StudentConnected

        loop Capture target 50 ms; effective rate varies
            C->>H: SendScreenFrame
            H-->>T: ReceiveScreenFrame if authorized
            H-->>A: ReceiveScreenFrame
        end

        C->>H: Activity, idle, website status, infraction, telemetry batch
        H->>DB: Persist normalized operational records
        H-->>T: Scoped status or InfractionDetected

        T->>H: Scoped warning, lock, logout, restart, shutdown, input, broadcast
        H->>DB: Recheck scope and audit authorized command
        H-->>C: Target command/event
        A->>H: Authorized global/session action
        H->>DB: Persist state and audit
        H-->>C: GlobalSessionState, RestrictionsReceived, or SessionEnded

        C--xH: Temporary disconnect
        H-->>T: StudentDisconnected
        Note over C,DB: Persisted active session can remain for reconnect
        C->>H: Reconnect with authenticated identity
    end
```

## Event Reference

| Direction | Event/method | Purpose and scope |
| --- | --- | --- |
| Client to Server | HTTPS `/api/client/login` | Validates student credentials and exact assigned workstation before issuing the client-agent identity. |
| Client to Hub | `SendScreenFrame` | Sends a screen frame; the server forwards it only to authorized teacher/admin viewers. The 50 ms delay is a target, not guaranteed FPS. |
| Client to Hub | activity/idle/browser/infraction and telemetry batch methods | Reports bounded, normalized classroom state. Browser data excludes credentials, page content, paths, queries, and fragments. |
| Hub to Dashboard | `StudentConnected` / `StudentDisconnected` | Adds/removes a live card within viewer scope; disconnect does not necessarily end the persisted session. |
| Hub to Dashboard | `ReceiveScreenFrame` / `InfractionDetected` | Updates authorized monitoring UI and alert state. |
| Dashboard to Hub | `SendRemoteInput` | Rechecks target scope and session permission, audits, then targets one client. |
| Hub to Client | `ExecuteRemoteInput` | Requests input simulation. Result depends on Windows desktop, privilege, and security policy. |
| Dashboard to Hub | warning method / `BroadcastScreen` | Sends a CAMS topmost warning dialog or teacher screen to accessible clients. |
| Dashboard to Hub | lock/unlock/logout/restart/shutdown methods | Rechecks target scope and audits before routing. Unlock releases CAMS state only, not Windows secure desktop. |
| Hub to Client | `GlobalSessionState` / `SessionEnded` | Synchronizes persisted session state or ends/logs out the target client. Admin has global controls; teacher bulk actions remain teacher-owned. |
| Client to Hub | `FetchRestrictions` | Requests active global plus active-session teacher rules. |
| Hub to Client | `RestrictionsReceived` | Delivers applicable application/domain allow/block rules and refreshes after policy changes. |

## Trust And Discovery Boundary

SignalR starts only after HTTPS trust and authentication. The interactive installer configures the endpoint and current-user certificate trust; the offline bundle verifies the installer/hash, configures `/ServerUrl`, and tests `/api/deployment/ping`. Discovery merely advertises candidate URLs over server outbound UDP to client port `5001`; it does not authenticate a user or replace TLS validation.
