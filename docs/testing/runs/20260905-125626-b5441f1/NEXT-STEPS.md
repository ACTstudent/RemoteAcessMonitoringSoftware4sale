# Next steps — run 20260905-125626-b5441f1

Test execution finished for everything this workstation can reach. Product
acceptance has **not** been achieved: the cases below remain, and each needs
hardware or a decision that was unavailable during the run.

## 1. Installer behaviour on a real machine — INS-01, INS-02, INS-03

Prerequisite: a disposable Windows VM with snapshot support and **no** .NET
runtime pre-installed, so the self-contained package is genuinely exercised.

Artifacts to test, already built and hash-verified:

- `server-dist\CAMS-Server-Setup.exe` — `185820D7CB15AE9B1278B426AFC79D9806AD9E2A24FAA65F27B3393C2BD472A2`
- `client-dist\CAMS-Client-Setup.exe` — `4A1D4A4CC4D76BD37A3EB7C7CA561F98FB36593FB5E9812A2545CB2099A7B0D8`

Steps: snapshot, clean install, confirm the service starts and the portal is
reachable; restore, install v2.11.4 and upgrade to v2.11.5 over a populated
database and confirm accounts and history survive; uninstall, confirm what is
left behind, then reinstall.

Completion condition: each of the three variants has an observed result and the
upgrade preserves data.

Note: matching hashes prove artifact identity only. They cannot close INS-01 or
INS-02, which is why those are still open.

## 2. Classroom walkthrough — CLASS-01, CLASS-02

Prerequisite: two disposable client machines on the same LAN as a test server,
plus the root certificate `CAMS-Server-Root.cer` installed in each client's
trusted store. Without that trust the agent correctly refuses to connect, which
was confirmed during this run.

Cover: screen delivery from both clients, input targeting the intended machine,
policy enforcement, alert delivery, session lifecycle and reconnection after the
agent is restarted.

CLASS-02 additionally covers lock, logout, restart and shutdown. **Send these
only to an identified disposable client and record the machine name.** They were
deliberately not aimed at the working machine during this run.

Completion condition: every operation observed on a named target machine.

## 3. Network recovery — NET-01, NET-02

Prerequisite: a disposable network segment where an outage can be staged.

Cover: pull the LAN mid-session and confirm the agent reconnects; block
discovery and confirm the manual server address fallback; change the server
address and confirm certificate and address reconciliation.

Completion condition: recovery observed for each, with the agent returning to a
working session.

## 4. Capacity — CAP-01

Currently **NOT ASSESSED**, and it should not be marked otherwise until two
things exist: a lab with a realistic client count, and **approved numeric
acceptance limits**. No such limits are recorded anywhere in the repository.

Required decision before testing: state the target concurrent client count and
the acceptable frame latency and server resource ceilings. Without those,
measurements can be reported but no pass or fail can be claimed.

## 5. Client test coverage — UNIT-04

`Client.Tests` has no coverage collector configured, so client coverage is
unknown rather than low. Add a collector to that project and rerun to obtain a
figure.

Command shape: `dotnet test Client.Tests/Client.Tests.csproj --collect:"XPlat Code Coverage"`
after adding the collector package.

## 6. Branch coverage on the server

Server branch coverage is 33.86 percent against 79.62 percent line coverage.
Roughly two thirds of decision paths are unexercised, so conditional logic —
session expiry boundaries, authorization branches, retention thresholds — is
substantially untested even though the suite is green. Worth targeting before
the next release rather than treating 319 passing tests as sufficient.

## 7. DEF-001

Decide between self-hosting the two font families (recommended, and the only
option that works on an offline school LAN) or widening the content security
policy. Details and both options are in [DEFECTS.md](DEFECTS.md). No production
code was changed during this run.
