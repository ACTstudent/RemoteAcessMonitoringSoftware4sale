# Use Case Diagram

```mermaid
flowchart TD
    subgraph System[Remote Access Monitoring System]
        direction TB
        UC1([Login to System])
        UC2([Register with Monitoring Hub])
        UC3([Stream Live Screen])
        UC4([Monitor Student Screens])
        UC5([View Online Students])
        UC6([Control Student PC Remotely])
        UC7([Record Lab Session])
        UC8([Logout])
    end

    Student([Student])
    Teacher([Teacher / Admin])

    Student --> UC1
    Student --> UC3
    Student --> UC8

    Teacher --> UC1
    Teacher --> UC4
    Teacher --> UC5
    Teacher --> UC6
    Teacher --> UC8

    UC1 -. include .-> UC2
    UC1 -. include .-> UC7
    UC4 -. extend .-> UC6
```

## Legend

| Actor | Description |
| --- | --- |
| **Student** | Runs the WinForms client on a lab PC, streams their live screen, and can receive remote input. |
| **Teacher / Admin** | Logs in on the web dashboard, watches live screens, and can take remote control of any student PC. |

## Notes

- **Login to System** — validates credentials against the `Students` / `Admins` tables.
- **Record Lab Session** — the server creates an active `LabSession` (PC name, IP, start time) on successful student login.
- **Register with Monitoring Hub** — the client joins the SignalR `StudentsGroup` after login.
- **Stream Live Screen** — the client captures the screen (~12 FPS) and sends JPEG frames through the hub.
- **Monitor Student Screens** — the dashboard joins the `TeachersGroup` and renders live frames as student cards.
- **Control Student PC Remotely** — extends monitoring: the teacher's mouse/keyboard events are routed to the selected student connection and executed with `InputSimulator`.
- **Logout** — closes the active `LabSession` (end time set) and ends the stream.
