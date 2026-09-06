# Test run report

## Run identity

- Run ID / started / completed (UTC):
- Executor / reviewer:
- Commit / branch / dirty diff reference:
- Product version / installer SHA-256 / manifest:
- OS builds / SDK / browsers / Inno Setup:
- Server and client hardware / network / client count:
- Fixture version / isolated DB location / snapshot references:
- Scope / excluded areas with owner and reason:
- Performance acceptance limits agreed before execution:

Record the **command and fixture** that produce each figure, not a path into a
working directory. Raw logs and the isolated database are not retained; the two
existing runs cited scratch directories that no longer exist and each needed an
addendum to correct it. Derived evidence — the CSVs — goes beside the report,
and the harness that produced it belongs in
[`tools/verification/`](../../tools/verification/README.md).

## Automated execution

| Project/check | Command/log link | Discovered | Passed | Failed | Skipped | Exit code | Evidence |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Server.Tests | | | | | | | |
| Client.Tests | | | | | | | |
| Solution build | | N/A | | | | | |
| Cleaner build/tests | | | | | | | |
| Canonical package | | N/A | | | | | |

Server coverage: line/branch figures, collector settings, uncovered critical paths and artifact link. Client coverage: measured value or NOT MEASURED, never assume zero or full coverage. Record flaky tests and skipped checks with reason.

## Case execution and traceability

Duplicate for every parameter/role/environment variant. Status: NOT RUN / PASS / FAIL / BLOCKED / N/A (reason required).

| Case ID + variant | Source file/action + automated test name | Environment/fixture | Expected | Actual | Status | Executor/time | Evidence/defect |
| --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | NOT RUN | | |

## Defect record (repeat per defect)

- Defect ID / title / severity / owner:
- Case / commit / environment:
- Preconditions and exact steps:
- Expected versus actual behavior:
- Reproducibility / affected identities and data:
- Sanitized logs, screenshots, event timestamps and database assertions:
- Fix commit / retest run / outcome:

Severity: critical = unauthorized control/access or unrecoverable data loss; high = core flow unavailable or major integrity failure; medium = recoverable incorrect behavior; low = minor presentation/documentation issue. Priority reflects scheduling and need not equal severity.

## Performance and recovery

| Scenario/client count | Duration | p50/p95 latency | FPS/frame age | CPU/memory trend | Network/DB latency | Queue/loss | Agreed limit met? |
| --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | | | |

Record outage duration, time to recover, replay counts, before/after logical data counts, backup hashes, migration/restore behavior and installer rollback outcome.

## Release decision

- Decision: NOT ASSESSED / ACCEPT / REJECT.
- Required cases completed and mapped:
- Open critical/high defects:
- Blocked/not-run/skipped cases:
- Deferred issues with rationale, owner and review date:
- Intended environments actually validated:
- Evidence location and access/retention owner:
- Cleanup completed (test processes, data, trust/firewall changes, snapshots):
- Reviewer / date:

Do not mark ACCEPT with unresolved required cases. Attach sanitized summaries only; exclude credentials, private certificates and real student/screen data.
