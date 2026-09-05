# Handoff: execute the CAMS whole-codebase test plan

## Copy this instruction into the testing agent's task

> Test the CAMS codebase in this workspace using docs/testing/AGENT-HANDOFF.md. Execute the tests and document actual results; do not stop after producing another plan. Read the linked plan, cases and inventory. Preserve existing edits, freeze an isolated copy of the current working tree, run both test projects explicitly, build the solution and database cleaner, and work through the applicable integration, browser, Windows, packaging and recovery cases. Add focused tests or isolated harnesses for important uncovered behavior when feasible. Do not change production behavior to make tests pass. Save a run report, case results, source coverage mapping and reproducible defects under docs/testing/runs/<run-id>/. Record unsupported environments as BLOCKED with exact prerequisites while continuing independent work. Never claim a check passed without execution evidence. Finish with tested scope, totals, defects, evidence paths and remaining blockers.

## Objective and scope

The requested output is an evidence-backed test execution of the current codebase. Planning documents already exist; implement the execution workflow below. Testing includes authored server/client/shared code, UI, persistence, standalone tooling and deployment surfaces. Do not deploy a release, publish artifacts, change real user records, or issue remote OS commands to occupied machines.

Read in order:

1. Any applicable AGENTS.md instructions in the workspace used for execution.
2. [TEST-PLAN.md](TEST-PLAN.md) for environments, baseline commands and acceptance.
3. [TEST-CASES.md](TEST-CASES.md) for the 37 case families and classroom walkthrough.
4. [SOURCE-INVENTORY.md](SOURCE-INVENTORY.md) for the original tracked-file checklist.
5. [RUN-REPORT-TEMPLATE.md](RUN-REPORT-TEMPLATE.md), [README](../../README.md), [Deployment guide](../../DEPLOYMENT.md) and [CAMS Guide](../../CAMS-Guide.md).

The original inspection baseline was b6920e4ebe581a961cf9a8236f45b6e8155584e9. Reconcile the inventory against the actual checkout; do not assume the baseline is still current or that existing tests pass. At handoff creation, the working tree contains user/other-agent changes in Razor views and web assets, plus the testing documents and README. Preserve and include current edits in the test snapshot; a clean worktree created solely from HEAD would omit them.

## Phase 1: freeze inputs and prepare evidence

1. Record UTC start time, repository path, HEAD, branch, git status, diff and untracked file list. Read source without displaying secrets. Establish a run ID such as 20260905-143000-<shortsha>.
2. Prepare an isolated checkout or directory that includes current source edits and relevant untracked source. Avoid copying live databases, credentials, certificates, bin/obj or installed runtime state. Capture a file/hash manifest and sanitized diff so the tested snapshot is reproducible. Do not reset, stash, overwrite or clean the user's original changes.
3. Inspect prerequisites: OS/architecture, dotnet --info, Inno Setup, usable browser automation and availability of disposable Windows clients/server. Record versions and missing capabilities. Missing Inno Setup does not block unit tests; missing interactive Windows access does not block SQLite tests.
4. Create docs/testing/runs/<run-id>/ in the original workspace for sanitized reports. Keep raw logs, TRX, coverage and large artifacts in a dedicated external run directory; record its absolute path. Copy the report template into SUMMARY.md.
5. Create CASE-RESULTS.csv with columns: CaseId,Variant,Source,TestName,Environment,Expected,Actual,Status,Evidence,DefectId,ExecutedAtUtc. Initialize planned variants as NOT RUN; update immediately after execution. Create SOURCE-COVERAGE.csv with File,CaseIds,AutomatedTests,ReviewStatus,Evidence,ExclusionReason.

Checkpoint: report identifies exactly what will be tested and where evidence will be saved.

## Phase 2: establish the automated baseline

1. Run the baseline commands in TEST-PLAN.md individually. Check each exit code, retain stdout/stderr and duration, and preserve TRX. Explicitly execute Server.Tests and Client.Tests: neither belongs to the solution. Build CamsDbCleaner separately as well.
2. If restore/build fails, distinguish missing prerequisites/network access from a code failure. Record the exact command and error. Continue checks that can still run. Do not repeatedly rerun an unchanged failure.
3. Parse actual TRX totals, including skipped tests. Collect server coverage using its existing collector. Do not claim client coverage without installing/configuring and successfully running an appropriate collector in the isolated test project.
4. Examine failures and critical uncovered paths. Reproduce each suspected failure once with a focused command, retain evidence and file a defect. Record intermittent behavior instead of silently accepting a later pass.
5. Read relevant assertions, fixtures and mocks to determine what the tests actually establish. Map test methods to source and case variants. Mocks do not prove HTTP middleware, real SignalR transport or real Windows input.

Checkpoint: baseline report with commands, totals, coverage limitations and reproducible failures. Do not run the canonical packaging script first: it cleans generated output directories.

## Phase 3: close critical behavioral gaps

Execute P0 cases before lower-priority cases. Use TEST-CASES.md as the detailed oracle and inspect implementation where it specifies behavior. Log requirement/implementation conflicts as defects; do not invent a passing expectation.

1. Authorization: build a controller action and hub method matrix across anonymous/Admin/active Teacher/inactive Teacher/Student and target identities. Test actual HTTP/cookie/filter behavior where possible. Preserve intended lab-wide Teacher monitoring/operational permissions while testing scoped historical/session/alert actions and Admin-only surfaces.
2. Session and identity integrity: simultaneous login/workstation allocation, pause/resume/expiry, duplicate requests, reconnect and restart. Assert final database state, not only response codes.
3. Database correctness: real file-backed SQLite migrations, relational constraints, maintenance backup/restore, retention and standalone cleaner report/apply against disposable fixtures. Never use a production database.
4. Messaging and telemetry: authenticated real SignalR connections, forged target/result identifiers, stale disconnect, interrupted batches, bounded durable queue and recovery.
5. Trust/deployment boundaries: invalid certificates rejected, private key material excluded, downloads authorized and manifest hashes validated.

Add small deterministic tests and harnesses when missing coverage can be implemented safely. Keep test-only changes separate from production fixes, list added files and exact run commands, and rerun the affected suite after harness changes. If an integration seam requires production changes, document the proposed seam and run a process-based isolated test where practical. Do not weaken security checks, disable assertions or rewrite expectations merely to turn failures green.

Checkpoint: each critical case has direct execution evidence or a precise blocker. Continue feasible lower-priority work even if one case is blocked.

## Phase 4: verify UI and live classroom behavior

1. Launch only an isolated configured server. Inspect its startup side effects first: base-directory database/certificates, seeding, port binding and browser launch. Keep test processes and paths recorded for cleanup.
2. Exercise every Razor view and authored JavaScript flow, role-specific forms, validation, navigation, exports, live state and console/network errors. Test public portal fallback and HTTP loading separately.
3. Follow the detailed classroom walkthrough with two disposable clients. Capture screen delivery, input targeting, policies, alerts, session lifecycle and reconnect evidence.
4. Test lock/logout/restart/shutdown only on identified disposable clients and record the actual targeted machine. Do not execute those operations on the user's working machine.
5. For unavailable UI/native capabilities, record the exact blocked variants, required VM/browser/session and the next command or manual step needed. Do not replace live validation with a source-reading PASS.

Checkpoint: browser/Windows environment matrix and actual observed case outcomes.

## Phase 5: verify packaging, recovery and capacity

1. Run build-everything.ps1 inside the isolated snapshot with prerequisites installed; retain the log and candidate hashes. Then test the resulting installers on snapshot-backed VMs without a separate .NET runtime.
2. Exercise clean install, upgrade with populated fixtures, uninstall/reinstall and rollback rehearsal. Artifact validation alone cannot close INS-01/INS-02.
3. Test planned LAN outages, manual discovery fallback, server address change and certificate reconciliation. Make network changes only in the disposable environment.
4. Measure the capacity/soak scenarios in TEST-PLAN.md if an appropriate lab exists. Record hardware and actual client counts. Set numeric deployment acceptance limits before execution; if no approved limits exist, report measurements and acceptance as NOT ASSESSED, not an invented pass.
5. Reconcile documentation, version files, workflows and installer behavior. Do not publish or invoke a release deployment as part of testing.

Checkpoint: artifact identity, recovery proof, performance measurements and explicitly unvalidated deployment environments.

## Phase 6: reconcile results and deliver

1. Reconcile every source-inventory row and every case family. Split case families by input, role and environment; record all variants. Each row must link to evidence or explain an exclusion/blocker. Check newly added/changed source since the original inventory.
2. Write DEFECTS.md: ID, severity, affected source, exact reproduction, expected/actual, evidence, environment, impact and suggested follow-up. Do not modify production code unless the user separately requests fixes; proposed fixes may be documented.
3. Write SUMMARY.md with automated totals, manual/integration case totals, verified environments, coverage limits, critical/high defects, blockers, artifacts and release decision using the template.
4. Write NEXT-STEPS.md only for unfinished work: exact case IDs, missing prerequisites, fixture state, commands, evidence to collect and completion condition. Clearly distinguish test execution finished from product acceptance achieved.
5. Validate report links, CSV structure and git diff --check. Verify only intended test/documentation changes were made. Stop only the test processes you started and restore test VM/network/trust state as applicable.
6. Final user response: outcome first, link SUMMARY.md, state passed/failed/blocked counts, list highest-impact findings and any remaining required validation. A required blocked test prevents release acceptance, even when all available automated tests passed.

## Required deliverables

- docs/testing/runs/<run-id>/SUMMARY.md
- docs/testing/runs/<run-id>/CASE-RESULTS.csv
- docs/testing/runs/<run-id>/SOURCE-COVERAGE.csv
- docs/testing/runs/<run-id>/DEFECTS.md (explicitly state none found if applicable)
- docs/testing/runs/<run-id>/NEXT-STEPS.md (explicitly state none if complete)
- Linked raw execution logs, TRX, coverage, installer hashes and sanitized screenshots/DB assertions as applicable.
- Any new automated tests/harnesses, with their invocation and results documented.

Never fill result tables with assumed successes. NOT RUN is work remaining; BLOCKED requires a specific missing prerequisite; N/A requires a scope rationale; PASS requires observed evidence. Do not conceal incomplete whole-system testing behind a successful build.
