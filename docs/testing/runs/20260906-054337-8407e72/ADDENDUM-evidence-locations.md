# Addendum — where the evidence and harnesses actually are

Dated 2026-09-06, after commit `296ae19`. The run report is left as written; this
records what has changed about the locations it cites.

When this run was recorded, its scripts existed only in a session scratch
directory. That directory is gone. Anything in the report that points into it
points at nothing, which is what this addendum exists to correct.

## The harnesses are now in the repository

They live in [`tools/verification/`](../../../../tools/verification/README.md).
Every script section 5 of NEXT-STEPS listed as worth keeping was kept:

| Cited in the report as | Now |
| --- | --- |
| `enumerate-routes.js` | `tools/verification/enumerate-routes.js` |
| `authz-anonymous.js` | `tools/verification/authz-anonymous.js` |
| `role-matrix.js` | `tools/verification/role-matrix.js` |
| `ui-sweep.js` | `tools/verification/ui-sweep.js` |
| `scratchpad/inactive-teacher.js` (DEFECTS.md) | `tools/verification/inactive-teacher.js` |
| `scratchpad/csv-safety.js` (NEXT-STEPS.md) | `tools/verification/csv-safety.js` |

Fifteen further harnesses written after this run went in alongside them —
scoping, focus, navigation, table, connection-state, stale-frame, UDP discovery
and real SignalR checks. The README covers what each one asks.

**One cited script was not kept.** `scratchpad/ratelimit-probe.js`, quoted in
DEF-0906-001, was a single diagnostic answering one question and is gone. It is
no loss: the shell loop printed directly above its output in DEFECTS.md
reproduces the finding, and `Server.Tests/Services/RequestThrottleTests.cs` now
covers the same ceiling with an injected clock.

## They no longer run as the report ran them

The migrated scripts had test credentials written into them. Those were removed;
`config.js` now reads `CAMS_TEST_URL`, `CAMS_TEST_ADMIN_USER` and
`CAMS_TEST_ADMIN_PASSWORD` from the environment and stops if any is missing. A
command copied verbatim out of this report will not run until those are set. The
setup section of the harness README has the sequence.

Absolute paths to one machine's checkout were removed at the same time.

## The raw logs are gone, and will not come back

SUMMARY.md places raw logs, TRX and Cobertura XML at
`…/scratchpad/testrun-20260906-054337-8407e72/`, and the isolated snapshot and
its database at `…/scratchpad/snap-8407e72/`. Both directories were session
scratch and no longer exist. They were never committed, deliberately: they hold
absolute paths and full console output.

What survives is the derived evidence — the CSVs beside this file, which carry
the per-case results — and the harnesses that produced them. A figure in this
report that you want to confirm is re-derivable by running the harness against a
fresh isolated server, not by finding the original log.

That is the standing arrangement for this repository: **committed harness plus
committed derived CSV, not retained raw output.** Future runs should assume the
same and record the command rather than the path.
