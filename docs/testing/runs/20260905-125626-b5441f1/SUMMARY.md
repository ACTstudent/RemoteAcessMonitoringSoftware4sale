# CAMS test run — 20260905-125626-b5441f1

| Field | Value |
| --- | --- |
| Started (UTC) | 2026-09-05T12:56:26Z |
| Repository | `C:\Users\Jlard\OneDrive\Desktop\RemoteAcessMonitoringSoftware4sale` |
| Branch | `main` |
| HEAD tested | `b5441f14b90e7f6ec1701b4dc598fdc536c37a2a` |
| Working tree | clean; no uncommitted or untracked source at freeze time |
| Product version | 2.11.5 |
| Raw evidence | `…\AppData\Local\Temp\claude\…\scratchpad\testrun-20260905-125626-b5441f1` |

## Outcome

**Not accepted for release.** All automated tests pass and every executable
behavioural case passed, but seven required live cases are BLOCKED for want of
disposable hardware, and one defect affects every page. Passing automated tests
do not by themselves establish that this system is fit to deploy into a
classroom.

## Baseline reconciliation

The plan's inspection baseline was `b6920e4`. The checkout is four commits
ahead, so the inventory was reconciled against the actual tree rather than
assumed current:

```
b5441f1 Release CAMS v2.11.5 with rebuilt installers
76b782e Make the menu button hide and show the sidebar
2bb2353 Remove the global scope banner from the admin layout
90fc671 Fade the student notifications instead of blocking on a dialog
```

The handoff also expected uncommitted Razor and asset edits in the working tree.
Those had already been committed before this run began, so the tested snapshot
is HEAD and nothing was omitted. Nothing was reset, stashed or cleaned.

## Totals

| Status | Count |
| --- | --- |
| PASS | 51 |
| FAIL | 3 |
| BLOCKED | 7 |
| NOT RUN | 1 |
| NOT ASSESSED | 1 |
| **Total variants** | **63** |

Automated tests, counted from the TRX files rather than console output:

| Suite | Total | Passed | Failed | Skipped |
| --- | --- | --- | --- | --- |
| Server.Tests | 319 | 319 | 0 | 0 |
| Client.Tests | 33 | 33 | 0 | 0 |

Neither test project belongs to `RemoteMonitoring.sln` (the solution references
zero test projects), so both were invoked explicitly. `CamsDbCleaner` was built
separately for the same reason.

## Coverage and its limits

Server line coverage 79.62 percent, branch coverage 33.86 percent
(`Server` 79.57, `Shared` 85.89). **Branch coverage is low**: roughly two thirds
of decision paths are unexercised, so a green suite says less about conditional
logic than the line figure suggests.

Client coverage was **not collected** — no collector is configured for
`Client.Tests`, and it is recorded as NOT RUN rather than claimed.

The unit suites lean on mocked hub contexts. They do not exercise HTTP
middleware, real SignalR transport, or real Windows input. Those were covered
separately by the live probes below.

## What was verified live

- **Authorization matrix** across anonymous, Admin, active Teacher, inactive
  Teacher and Student over real HTTP with real cookies. The intended design
  holds: a teacher reaches the shared admin surfaces but is refused Roles,
  SystemLogs, AuditLogs, LanConfig and AdminDatabase; an inactive teacher cannot
  even sign in; a student is refused every teacher and admin route.
- **SignalR over real transport**, positive and negative. Sixteen cases: the
  monitoring, alert and remote-control paths all work, and a student-authenticated
  connection is refused all eight teacher-only operations, including one aimed at
  a forged connection id.
- **Session integrity**: the same student is admitted on their own workstation
  and refused on a second one.
- **Login throttling** returns 429 past the limit. It also throttled this test
  harness, which is the control behaving correctly.
- **Database**: migrations applied to file-backed SQLite; the standalone cleaner
  was proven non-destructive in report mode (byte-identical file) and correct in
  apply mode (45 rows removed, all account and roster tables intact), against a
  disposable fixture copy — never a live database.
- **Artifacts**: installer hashes and sizes independently recomputed and matched
  against `release-manifest.json` and the sidecar checksums; no `.pfx`, `.p12`
  or `.key` present anywhere in packaged output.
- **Transport**: no plain-HTTP listener; five security headers present; the
  desktop agent refuses a server whose root certificate is untrusted.
- **Browser**: 34 views across three roles, plus the sidebar collapse and toast
  dismissal behaviours, driven in headless Chrome.

## Defects

One, described fully in [DEFECTS.md](DEFECTS.md).

**DEF-001 (Medium)** — the content security policy blocks the interface font
stylesheet on all 34 pages. `site.css` imports Plus Jakarta Sans and Inter from
`fonts.googleapis.com`, which `style-src 'self' 'unsafe-inline'` forbids. The
product never renders its intended typeface, and every page logs a console
error. It degrades gracefully to the fallback font, so it is cosmetic rather
than functional. Notably, a LAN deployment without internet access would fail to
load the font even if the policy were widened, so self-hosting is the suggested
remedy. No production code was changed.

## Blocked, and why

Seven cases could not be executed here. None is a code failure; each needs
hardware this workstation does not have.

| Case | Prerequisite |
| --- | --- |
| INS-01/02/03 | A disposable Windows VM for clean install, upgrade over populated data, and uninstall/reinstall |
| CLASS-01 | A second disposable client machine for the two-client classroom walkthrough |
| CLASS-02 | A disposable client to receive lock, logout, restart and shutdown. These were deliberately not aimed at the working machine |
| NET-01/02 | A disposable network on which an outage and a server address change can be staged |

CAP-01 (capacity and soak) is **NOT ASSESSED**: there is no lab, and no approved
numeric acceptance limits exist. Inventing a pass here would be meaningless.

## Scope boundaries observed

No release was published, no real user records were altered, no remote OS
command was sent to any occupied machine, and the live database was never used
as a fixture. The only processes started were an isolated Release server and
headless Chrome; both were stopped.

## Release decision

**Blocked.** The build is healthy and no functional defect was found in the
paths that could be exercised, but installer behaviour on a real machine,
end-to-end classroom operation and network recovery remain unproven. A required
blocked test prevents acceptance even though every available automated test
passed. See [NEXT-STEPS.md](NEXT-STEPS.md).
