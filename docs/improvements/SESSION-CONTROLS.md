# Session controls — 2026-09-08

Update (2026-09-09): the implementation is included in the local 2.17.0 build artifacts. The server was republished and its installer refreshed; server and client program files were updated in this computer's existing installation folders, preserving settings, database, and certificates. Other student PCs still need the updated client installer. No behavior tests, live input blocking, or workstation restarts were run. See [local update details](LOCAL-UPDATE-2026-09-09.md).

## Teacher actions

| Action | Student workstation behavior |
| --- | --- |
| Pause | The timer stops. A green-and-white CAMS screen covers every monitor, including the taskbar, with “Session paused” and “Please wait for your teacher to continue the session.” There is no student dismiss button. Normal keyboard and mouse input is held. |
| Resume | The full-screen screens disappear, the input hooks are released, and the timer continues from the server's elapsed time. Apps stay open during the pause. |
| End | Session records are ended before restart requests are sent. Connected student PCs show a restart screen and request Windows restart after 10 seconds, forcing applications closed. Unsaved work can be lost; teacher/admin confirmations explicitly state this. |

Global Pause now affects all active running sessions, even if an individual session's rule disables its own Pause button. Individual pause permissions are unchanged. Global End sends to the student SignalR group, not to teacher/admin connections; individual End targets the affected student's connections. Offline PCs cannot receive a live restart request. Command delivery is not a reboot acknowledgement.

Automatic timeout, normal logout, and account deactivation do not acquire the explicit End button's reboot behavior. These retain the existing session-end/logout notification. The server sends the existing `RestartStudent` event before `SessionEnded` for explicit End actions, so the updated client suppresses the old logout/lock dialog during restart.

## Client implementation

- `SessionScreenGuard` owns borderless topmost screens for each display and follows monitor layout changes. Normal messages received during a pause appear inside this screen instead of creating undismissable dialogs above it.
- Low-level keyboard/mouse hooks run on a dedicated Windows message-loop thread, without recording key contents. Resume, logout, and exit release the hooks. Held input is released to avoid leaving applications with stuck key/button state.
- Pause keeps the monitoring connection and capture loop running, allowing the teacher to see the waiting screen and send Resume or End. Remote input and broadcasts cannot replace/interact with the paused desktop.
- Reconnecting to an existing paused session reapplies its saved state. Losing the network does not automatically dismiss the pause screen. A newly created student session still follows the existing registration lifecycle.
- Restart handling suppresses duplicates, checks the `shutdown.exe` result, restores session proxy settings before reboot, and stops hub reconnection once a restart is accepted. A failed restart stays visible and connected so the teacher can retry Restart from monitoring. Windows shutdown is allowed to close the UI without waiting for a logout dialog.
- Ending while paused accounts for the final pause interval. Elapsed-time calculations for ended sessions use their saved end time.

Windows secure attention, secure desktops, and privileged operating-system controls are outside an ordinary desktop overlay's guarantees. This is a classroom interaction guard, not a kiosk security boundary. Input-hook failures are shown in the waiting screen; reboot still requires Windows shutdown permission. The implementation follows Microsoft's [low-level hook guidance](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc) and [shutdown command semantics](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/shutdown).

## Deferred validation

On disposable student workstations, verify teacher/admin Pause → Resume → End, individual End, repeated commands, multiple monitors/DPI, pause while typing/dragging, Alt+Tab/Alt+F4/Windows-key handling, reconnect while paused, and the failure UI when input hooks or restart permission are denied. Confirm global versus individual scope, exclusion of offline/teacher PCs, pause-duration accounting, proxy restoration, normal logout/timeout behavior, and that a scheduled restart does not reopen a session through reconnect. These checks have not been run.
