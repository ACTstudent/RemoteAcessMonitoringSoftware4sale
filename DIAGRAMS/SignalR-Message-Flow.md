# SignalR Message Flow

How real-time events flow between the Student Client, the Server Hub, and the Teacher Dashboard over SignalR.

```mermaid
flowchart TD
    subgraph Client[Student Client - WinForms]
        C1[MonitoringHubClient]
        C2[ScreenCaptureService]
        C3[InputSimulator]
    end

    subgraph Server[ASP.NET Core Server]
        H[RemoteMonitoringHub]
        subgraph Groups[SignalR Groups]
            G1[StudentsGroup]
            G2[TeachersGroup]
        end
    end

    subgraph Dashboard[Teacher Dashboard - Web]
        D1[signalR.js client]
        D2[Canvas / remote modal]
    end

    C1 -- "1. RegisterStudent(studentId, pcName)" --> H
    C2 -- "2. SendScreenFrame(ScreenFrameMessage)" --> H

    H -->|student added| G1

    D1 -- "3. RegisterTeacher" --> H
    H -->|teacher added| G2

    H -- "ReceiveScreenFrame(connId, frame)" --> D1
    H -- "StudentConnected(student)" --> D2
    H -- "StudentDisconnected(connId)" --> D2

    D2 -- "SendRemoteInput(targetConnId, RemoteInputMessage)" --> H
    H -- "ExecuteRemoteInput(RemoteInputMessage)" --> C1
    C1 --> C3
```

## Event reference

| Direction | Event | Sent by | Payload | Purpose |
| --- | --- | --- | --- | --- |
| Client → Server | `RegisterStudent` | Student client | `studentId`, `pcName` | Add connection to `StudentsGroup`, notify teachers |
| Client → Server | `SendScreenFrame` | Student client | `ScreenFrameMessage` | Broadcast live frame to `TeachersGroup` |
| Dashboard → Server | `RegisterTeacher` | Teacher dashboard | — | Add connection to `TeachersGroup` |
| Server → Dashboard | `ReceiveScreenFrame` | Server hub | `connectionId`, `ScreenFrameMessage` | Render live frame / feed the remote canvas |
| Server → Dashboard | `StudentConnected` / `StudentDisconnected` | Server hub | `StudentConnectionMessage` / `connectionId` | Add / remove student cards |
| Dashboard → Server | `SendRemoteInput` | Teacher dashboard | `targetConnectionId`, `RemoteInputMessage` | Forward a mouse/keyboard event to one student |
| Server → Client | `ExecuteRemoteInput` | Server hub | `RemoteInputMessage` | Target the specific student connection; handled by `InputSimulator` |