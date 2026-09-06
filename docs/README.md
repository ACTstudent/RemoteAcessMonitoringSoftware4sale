# CAMS documentation

Every document in this repository, in the order you are likely to want it. The
[repository README](../README.md) is the starting point for what CAMS is and how
to build it; this page is the map of everything else.

## Using CAMS

| Document | What it covers |
| --- | --- |
| [CAMS Guide](../CAMS-Guide.md) | The product: roles, the Admin, Teacher and Student portals, lab sessions, monitoring, alerts and policy |
| [Repository README](../README.md) | Scope, architecture, first start, building from source, and the Windows/LAN validation checklist |

## Deploying and troubleshooting

| Document | What it covers |
| --- | --- |
| [Deployment Guide](../DEPLOYMENT.md) | Trust bootstrap, offline bundle, versions, firewall and the full validation procedure |
| [Repository README — First Start](../README.md#first-start) | Administrator provisioning and the local CA on a fresh install |
| [Repository README — Windows and LAN validation](../README.md#windows-and-lan-validation-checklist) | What to confirm on real classroom hardware before rollout |

There are no default passwords, and the two `.pfx` files are never distributed —
only the public `CAMS-Server-Root.cer`. The deployment guide is the authority on
both points.

## Architecture

| Document | What it covers |
| --- | --- |
| [Entity Relationship Diagram](../DIAGRAMS/ERD.md) | Database entities and their relationships |
| [System Flowchart](../DIAGRAMS/Flowchart.md) | End-to-end system flow |
| [SignalR Message Flow](../DIAGRAMS/SignalR-Message-Flow.md) | Hub messages between server and client |
| [Menu Structure](../DIAGRAMS/Menu-Structure-Diagram.md) | Navigation across the three roles |
| [Use Cases](../DIAGRAMS/Use-Case-Diagram.md) | Actors and their tasks |
| [Source inventory](testing/SOURCE-INVENTORY.md) | Every source file, what it does and which tests touch it |
| [Design system](DESIGN-SYSTEM.md) | Tokens, control variants and the shared page shell — what the interface is made of |

## Running the tests

The whole suite, 585 tests, runs from the solution:

```powershell
dotnet test "Monitoring And Remote Access\RemoteMonitoring.sln"
```

The canonical release build runs both suites and then packages and validates the
installers. It rebuilds the tracked installers, so run it when you intend to
produce a release candidate, not to check a change:

```powershell
.\build-everything.ps1
```

| Document | What it covers |
| --- | --- |
| [Test plan](testing/TEST-PLAN.md) | Scope, approach and what acceptance means |
| [Test cases](testing/TEST-CASES.md) | The case families and their identifiers |
| [Agent handoff](testing/AGENT-HANDOFF.md) | How to execute a run in isolation, and the constraints on doing so |
| [Run report template](testing/RUN-REPORT-TEMPLATE.md) | The shape a run report must take |
| [Verification harnesses](../tools/verification/README.md) | Checks that drive a running server — authorization, accessibility, SignalR, discovery. These produced the evidence in the run reports |

In CI, `test.yml` is the fast baseline on ubuntu and runs the server tests only —
`Client.Tests` targets `net8.0-windows` and cannot build there. `ci-full.yml`
runs the canonical build on windows, which covers both suites and the installers.
Both upload their TRX results, including when they fail.

### Test runs

| Run | Revision | Outcome |
| --- | --- | --- |
| [20260906-054337-8407e72](testing/runs/20260906-054337-8407e72/SUMMARY.md) | `8407e72` | Not accepted for release: 12 cases blocked for want of hardware. All 153 controller actions refuse an anonymous caller and every shared admin action matches its `[TeacherSharedAction]` attribute. The three defects it found have since been fixed ([addendum](testing/runs/20260906-054337-8407e72/ADDENDUM-defect-fixes.md)), the live transport lane tested ([addendum](testing/runs/20260906-054337-8407e72/ADDENDUM-live-transport.md)), and its harnesses committed ([addendum](testing/runs/20260906-054337-8407e72/ADDENDUM-evidence-locations.md)) |
| [20260905-125626-b5441f1](testing/runs/20260905-125626-b5441f1/SUMMARY.md) | `b5441f1` | Not accepted for release: seven live cases blocked for want of hardware. DEF-001 has since been fixed — see the [addendum](testing/runs/20260905-125626-b5441f1/ADDENDUM-install-and-DEF-001.md). Its raw evidence path no longer exists ([addendum](testing/runs/20260905-125626-b5441f1/ADDENDUM-evidence-locations.md)) |

Run reports are kept as written. When something in one later turns out to be
wrong or superseded, the correction goes in a dated addendum beside it and the
original is left intact, so the history stays readable.

**What is retained, and what is not.** A run keeps its derived evidence — the
summary, the case results and the CSVs — beside the report, and the harness that
produced them is committed under
[`tools/verification/`](../tools/verification/README.md). Raw logs, TRX and
Cobertura XML are not retained: they carry absolute paths and full console
output, and they date immediately. A figure you want to confirm is re-derived by
running the harness against a fresh isolated server. So a report should record
the command and the fixture, never a path into a working directory — both runs
above did the latter and needed an addendum for it.

## Improvement ledger

| Document | What it covers |
| --- | --- |
| [Professionalization plan](improvements/PROFESSIONALIZATION-PLAN.md) | The phased plan being worked through |
| [Progress](improvements/PROGRESS.md) | Per-item status and evidence. VERIFIED requires evidence; blocked items say what they are blocked on |
| [Decisions](improvements/DECISIONS.md) | Choices made along the way, each with the alternative that was rejected |
| [Repository inventory](improvements/REPOSITORY-INVENTORY.md) | What is tracked, sorted into source and release artifacts — including which vendored assets carry a licence and which do not |
| [Journey results](improvements/JOURNEY-RESULTS.md) | The Phase 0 journey baseline, and which journeys cannot be observed on one machine |

## What is not written down

Stated so it is not mistaken for an oversight:

- **Live validation on real hardware.** Install and connect, and connection
  recovery, need a second Windows machine and a disposable network. Both are
  release gates and both are open. See LIVE-03 to LIVE-06 in
  [PROGRESS.md](improvements/PROGRESS.md).
- **Capacity limits.** No client count or numeric performance limit has been
  agreed, so none is documented. The 50 ms capture figure is an interval, not a
  promised frame rate.
- **Operator observations.** The journey baselines were captured by an agent
  driving the interface, which gives step counts and nothing about how the
  interface reads to a teacher.
