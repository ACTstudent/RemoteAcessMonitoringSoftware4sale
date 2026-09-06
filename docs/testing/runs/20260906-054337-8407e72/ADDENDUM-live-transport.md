# Addendum, 2026-09-06 — the live transport lane, tested

The run report and its first addendum both said the classroom half of the
product was unproven: no agent had connected, no screen had been delivered, no
command had been sent, discovery had never executed, and hub coverage was
mocked. Most of that turned out to be testable on one machine after all. This
addendum records what was closed, what was closed only in part, and what is
still genuinely blocked.

Everything below ran against the isolated published server on
`https://localhost:5100` with its own database. No destructive command was sent
and no software was installed on the operator's machine.

## Closed

### NET-01 — UDP discovery, executed for the first time

`ServerDiscoveryService` is 101 lines that had never run: measured at 0%
coverage, with no test naming it. A listener bound to UDP 5001, the way the
Windows agent does, received **8 advertisements in 11 seconds** — the documented
~3 s interval — each carrying:

```json
{"serverUrl":"https://192.168.1.19:5100/remoteMonitoringHub","appName":"CAMS"}
```

7 of 7 checks passed: valid JSON, names the product, advertises an HTTPS hub URL
on a routable LAN address rather than loopback, uses the port the server is
actually bound to, and repeats consistently.

### HUB-01, HUB-02, HUB-03 — real SignalR transport, 24 of 24

A real `Microsoft.AspNetCore.SignalR.Client` connection, authenticated the same
way the agent authenticates — `POST /api/client/login` for the student, the
browser form for the teacher. The existing hub tests all use a mocked
`IHubContext`, which can show what a method does with its arguments but nothing
about transport.

| Check | Result |
| --- | --- |
| An unauthenticated socket | Refused, `401` before the handshake completes |
| Student agent connects and registers | `S-R0906-1` on `HARNESS-PC-1`, from the claims |
| Teacher is told a student connected | Received |
| A frame reaches the watching teacher | Received |
| **A frame sent with a forged identity** | **Rewritten to the real one.** The client claimed `SPOOFED-STUDENT` / `SPOOFED-PC`; the teacher received it attributed to `S-R0906-1` / `HARNESS-PC-1`. The hub rebuilds the message from the authenticated connection rather than trusting the sender |
| A student invoking a teacher-only method | `HubException: Only teachers can perform this action.` |
| A second student receiving the first's screen | Nothing arrived |
| Student drops | Teacher told, `StudentDisconnected` |
| Student reconnects | Re-registers, new connection id, frames flow again |
| A 7 MiB frame against the 6 MiB ceiling | Refused; the teacher's connection survived |

### TEL-01 — telemetry from a real client, in the database

An active application reported over the hub was found in `UsageLogs` against the
right student and workstation, and `ActivityEvents` recorded the connect,
the application use and the disconnect under the real student number:

```
UsageLogs:       StudentId 1, PcName HARNESS-PC-1, AppName HarnessApp-57dffe0c
ActivityEvents:  Connected / ApplicationUsed / Disconnected, S-R0906-1, HARNESS-PC-1
```

### POL-01 — policy delivery, with correct scoping

Six rules were seeded: two global, four owned by a teacher. The connected client
asked for its policy and received **exactly the two global rules** and none of
the four teacher-scoped ones, because no lab session on that student names a
teacher. A teacher invoking the same method was refused — only a registered
student client can pull a policy set.

### WIN-02, the safe subset — a real remote command

A teacher sent a warning to a specific student over the hub. It arrived at that
student, the other connected student received nothing, and the server set the
message type itself rather than trusting the sender's. The notification was
persisted against the right student. A command aimed at a connection id that is
not a registered student was refused.

Lock, force-logout, restart and shutdown were **not** sent. They act on the
machine they reach, and the only machine here is the operator's.

## Closed in part

### The Windows agent

The agent was published self-contained and its command-line configuration path
exercised for real: `Client.exe --configure-server https://…/remoteMonitoringHub`
returned 0 and wrote the expected `client-settings.json`. Launched, it starts and
presents its window (`CAMS Student Client`, responding, window handle present).

It has **no automated sign-in** — only `--configure-server` is accepted, so the
credentials must be typed into the form. Everything the agent does *after* that
point is what the SignalR harness above exercises over the same transport. What
remains untested is the part unique to the WinForms process: real desktop
capture and real input injection. Both need someone at the keyboard of a
disposable machine.

### INS-01 — the installer, built and validated but not installed

The client installer was compiled from the snapshot with Inno Setup 6:

| | |
| --- | --- |
| Artifact | `CAMS-Client-Setup.exe`, 51,338,883 bytes |
| SHA-256 | `01b22d6aa0bb065a7cdd1d9e870d846d8fe4ab612ea8566f877f713d1cf6e008` |
| Private key material in the payload | none — no `.pfx`, `.key` or `.p12` |
| Self-contained | yes — `coreclr.dll`, `hostfxr.dll`, `hostpolicy.dll` and 187 framework assemblies, so it needs no separate .NET runtime |

This was built inside the snapshot, so no tracked installer was rebuilt and no
release was published. **Running it was deliberately not done**: installing CAMS
on the operator's working machine is a real deployment change. Clean install,
upgrade, uninstall and rollback still need snapshot-backed VMs.

## Still blocked

| Case | What is still missing |
| --- | --- |
| WIN-01 | Real desktop capture at different resolutions, scaling and multiple monitors |
| WIN-02 (destructive) | Lock, logout, restart and shutdown, on a disposable client only |
| SES-01 | Two machines racing onto one workstation allocation |
| INS-01, INS-02 | Installing, upgrading, uninstalling and rolling back on VMs |
| NET-01 (recovery) | A planned LAN outage, a DHCP change and manual-endpoint fallback |
| PERF-01 | Capacity, and numeric limits nobody has agreed yet |

## Correction to the earlier reports

Both said the hub was "tested only against mocks" and that discovery had "never
executed". Those were accurate when written and are now out of date: the hub has
24 checks over real transport, and discovery has been observed on the wire. What
stays true is that **no real screen has been captured and no real input
injected** — the frames in these tests were synthetic payloads, not pixels from
a desktop.

One thing done wrong during this work, recorded so it is not repeated: a screen
capture was taken to try to photograph the agent's window. The agent was behind
another window, so the capture contained the operator's own desktop instead. It
was deleted immediately and never committed. Proving a window exists does not
need a screenshot — the process handle and window title are the evidence, and
capturing an operator's screen is not something a test should do casually.
