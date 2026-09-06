# Next steps — run 20260906-054337-8407e72

This run finished its automated, authorization, UI and accessibility scope. It
did **not** finish product acceptance: 12 cases are BLOCKED on hardware this
environment does not have, 8 are NOT RUN, and 1 has no agreed acceptance limits.
A blocked required case blocks release acceptance regardless of how many
automated tests pass.

## 1. Defects to decide on

> **Closed 2026-09-06.** All three were fixed with regression tests and
> re-verified; see [ADDENDUM-defect-fixes.md](ADDENDUM-defect-fixes.md).
> Sections 2 onward are unchanged.

None of these was fixed during the run itself, which changed test code only.

| ID | Severity | Decision needed |
| --- | --- | --- |
| DEF-0906-001 | Medium | Whether the login ceiling should count only failed POSTs. One-line area in `Program.cs:180-218`. |
| DEF-0906-002 | Low | Align `AdminController.cs:1783` onto `DateTime.UtcNow` like the other three exports. |
| OBS-0906-003 | Low | Render the deactivate form's `isActive` value explicitly so it does not depend on a failed model bind. |

Completion condition: each is fixed with a regression test, or accepted with a
written rationale and an owner.

## 2. Cases still needing a real fixture (no hardware required)

These need only seeded data and can be done in the same isolated environment.

| Case | What is missing | Command to start from |
| --- | --- | --- |
| REP-01 | The fixture holds no sessions, usage or alerts, so no export could be reconciled against the interface. | Seed known sessions and telemetry, then compare `/Admin/ExportUsageCsv` totals against SQLite. |
| SEC-01 (CSV formula) | The student named `=HYPERLINK(...)` produced no telemetry, so the value never reached an export cell. | Seed a usage row for that student, re-run `scratchpad/csv-safety.js`, and assert the emitted cell does not begin with a bare `=`. |
| TOOL-01 | `CamsDbCleaner` was built but never executed. | Copy the run's `CAMS.db` to a scratch path, run the cleaner in report mode, then `--apply`, asserting row counts before and after. |
| DEP-01 (downloads) | The browser sweep skipped 14 download endpoints so navigation would not hang. | Fetch each with an HTTP client, compare SHA-256 against the manifest, and confirm no private key material is included. |
| DB-01 (prior schemas) | No prior-release fixture database exists here. | Obtain a database from each supported prior release and start the server against a copy of each. |
| UI-02 (keyboard, zoom) | Not attempted. | Keyboard-only traversal of the six core flows at 100/150/200% scaling. |

## 3. Cases blocked on hardware

Each needs equipment this environment does not have. None can be closed by
reading source.

| Case | Prerequisite |
| --- | --- |
| SES-01 | Two disposable Windows clients to race onto one workstation. |
| HUB-01, HUB-02, HUB-03 | A real SignalR client harness, plus two clients for delivery and control. Present hub coverage is mocked and cannot establish transport behaviour. |
| WIN-01, WIN-02 | An interactive Windows client, snapshot-backed. Lock, logout, restart and shutdown must never be sent to the operator's own machine. |
| POL-01, TEL-01 | A connected agent to observe policy propagation and queue bounds. |
| NET-01 | A disposable network for UDP discovery, outage and DHCP change. `ServerDiscoveryService` is 101 lines at 0% coverage. |
| INS-01, INS-02 | Snapshot-backed VMs without a separate .NET runtime. Packaging was deliberately not run because it rebuilds the tracked installers. |
| TLS-01 (client trust) | A second machine to reject an invalid or wrong-host certificate. |

## 4. Not assessed

PERF-01 has no approved numeric limits for this deployment. Measurements without
agreed limits would be numbers, not a verdict. Set the limits first, then
measure; do not record an invented pass.

## 5. Harness work worth keeping

The scripts written for this run live in the session scratchpad and are not
committed. Anything worth reusing should be moved into the repository:

- `enumerate-routes.js` — derives all 153 controller actions from source, so the
  matrix cannot drift from the code.
- `authz-anonymous.js` — the anonymous row across every action.
- `role-matrix.js` — checks every `AdminController` GET against the
  `[TeacherSharedAction]` attribute, which is the real oracle.
- `ui-sweep.js` — per-role page sweep with console, network and accessibility capture.
- `inactive-teacher.js` — the deactivation flow, using an isolated browser
  context per role.

Two traps these scripts already account for, worth preserving in any successor:
Puppeteer pages in one browser share a cookie jar, so each role needs its own
context; and a sweep that signs in repeatedly will trip the login ceiling
described in DEF-0906-001.
