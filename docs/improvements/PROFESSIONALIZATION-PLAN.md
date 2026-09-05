# CAMS professional finish, cleanup and seamless workflow plan

Prepared 2026-09-05 against source HEAD `e477560`, version 2.11.6. This is an implementation plan; no product code was changed or live usability tests performed to produce it.

The desired result is a classroom tool that looks consistent, makes the next step obvious, reports its true state and is straightforward to maintain. Build on the existing ASP.NET Core/Razor, WinForms, SignalR and SQLite architecture. Deliver small verified improvements to working journeys.

## Evidence and starting point

- The web CSS already defines a green/cream palette, typography and common controls. The Windows client mirrors that palette. Preserve this foundation and consolidate conflicting or repeated rules after checking computed styles.
- Admin, Teacher and Student layouts repeat the application shell. Two Teacher layout files exist; check references and runtime view resolution before removing either. Teacher pages and shared management pages use different shell labels/navigation; evaluate continuity across that transition.
- `Views/Teacher/_TeacherLayout.cshtml` queries SQLite and contains alert-scope logic inside the layout. Move data preparation into an appropriate server component while preserving the exact scope.
- `Views/Teacher/Monitoring.cshtml` contains substantial inline script. Its reconnect callbacks update status text; check full state reconciliation, stale frames and command availability before deciding which fixes are needed.
- `AdminController.cs`, `TeacherController.cs` and `Client/MainForm.cs` are large files combining multiple responsibilities. File size is a maintenance signal, not proof of a defect; extract coherent responsibilities where that makes behavior easier to test.
- The repository now contains a [test run summary](../testing/runs/20260905-125626-b5441f1/SUMMARY.md) and [later addendum](../testing/runs/20260905-125626-b5441f1/ADDENDUM-install-and-DEF-001.md). The original run recorded 319 server and 33 client tests passing and server branch coverage of 33.86%. These are historical measurements, not results for every subsequent commit.
- The addendum records the font/CSP defect fixed and verified in v2.11.6. Keep fonts self-hosted; do not reopen the resolved defect. It also records a missing favicon and partial installation evidence. Recheck current asset behavior before implementing a fix.
- Live two-client operation, upgrade/uninstall, network recovery and capacity still need the environments identified in the test reports. Visual improvements do not close those gaps.

Implementation references below are relative to `Monitoring And Remote Access/` unless a path starts with `portal/`, `docs/` or names a root build file.

## Product direction

Keep the forest-green identity, warm neutral background and restrained surfaces. Use consistent spacing, clear headings and readable tables. One primary action should be obvious within each task area. Use status colors for meaning; include text and an icon so color is not the only cue. Prefer clear labels such as “Workstations”, “Sessions”, “Alerts” and “Reports” across all roles.

Separate product identity from school identity: CAMS is the application; the current school name and logo describe the deployment. Centralize these values first. A configurable branding feature can follow if multiple deployments require it; do not add a configuration screen solely to remove duplicated text.

The Admin experience should prioritize setup and lab health. Teacher pages should prioritize running the current lesson, monitoring workstations and resolving alerts. Student pages should focus on connection, workstation assignment, remaining session time and actionable notices. Shared administration should retain the Teacher's identity and navigation context.

## What a seamless journey should look like

| Journey | Proposed experience | Completion check |
| --- | --- | --- |
| First server setup | Install, provision Administrator, open a setup checklist, create Teacher/class/roster, prepare a trusted client bundle, verify first connected workstation. Show completed and remaining steps. | A new operator can complete setup using in-product guidance and the Deployment guide; no dead end at login or certificate setup. |
| Student connection | Show Finding server, Checking connection, Sign in and Connected as distinct stages. Put retry and manual address in the same flow; show certificate remediation with optional diagnostic detail. | Offline, untrusted certificate, invalid credentials and workstation conflicts have different actionable messages; retries preserve safe input but never cache passwords. |
| Begin a lesson | Teacher lands on lab overview, sees available/connected workstations, starts a session and opens monitoring while retaining selected class/filter context. | A task walkthrough needs no backtracking to rediscover selection; active session and connected state agree across pages. |
| Monitor and assist | Stable workstation cards, search/filter, visible last-update state, selected student/workstation identity and a clear remote-support mode with an obvious Stop control action. | Switching targets never leaves ambiguous control ownership; disconnected/stale feeds cannot appear live. |
| Send a bulk command | Show selected target count and names where practical, confirm disruptive actions, show pending and per-target results, retain failed targets for explicit retry. | Partial success is visible; accepted/sent is distinct from confirmed completion; retries do not silently replay disruptive commands. |
| Handle an alert | Open an alert in context, inspect history, take action, acknowledge/dismiss and return to the same filtered list. | Counts, status and history agree; no duplicate success notice or loss of filter/scroll context. |
| Recover from outage | Persistent connection banner, automatic bounded retry and state refresh when connected. Mark old frames and uncertain command outcomes. | Recovery restores the actual roster/session without requiring routine refresh; unknown command outcomes are never called successful. |
| Finish and report | End lesson, see session outcome, export using visible filters and return to the summary. | Export scope and date range match the view; history remains accessible after archival. |

Existing role permissions remain the source of truth. Navigation grouping must not change authorization. Teacher lab-wide operational access and scoped historical/individual actions must remain distinct at the server.

## Phase 0 — establish a current baseline

Priority: first. Dependencies: none. Suggested owner: implementation agent with QA review.

- Read the latest testing report and addendum, reconcile HEAD/version and preserve current edits. Follow [the testing handoff](../testing/AGENT-HANDOFF.md) for isolated execution.
- Capture Admin overview, Teacher monitoring, shared management, Student session, login, Deployment Hub, Windows connection/login and public portal screens. Record browser, viewport and dataset for comparison.
- Observe five workflows: install/connect, create class/roster, start lesson, handle alert and recover connection. Count steps/backtracking, note confusing labels and measure completion time. Treat improvement claims as hypotheses until observed.
- Create a route/view/component inventory and list active layouts, inline scripts, duplicated styles, direct view queries and shared helpers. Identify changes already completed by other agents.
- Open a progress ledger: item ID, affected files, baseline evidence, implementation commit, test evidence, status and remaining risks.

Exit: reproducible baseline, a prioritized issue list and a stable test snapshot. Do not begin a sweeping rename or deletion during this phase.

## Phase 1 — make the interface consistent

Priority: high. Dependencies: baseline. Suggested owner: UI implementation agent.

| ID | Change | Source targets | Acceptance |
| --- | --- | --- | --- |
| UX-01 | Consolidate semantic color, spacing, font, border, radius and focus tokens. Document variants for buttons, forms, tables, status badges, toasts and dialogs. | `Server/wwwroot/css/site.css`, `Server/Views/Shared/`, `Client/MainForm.cs` palette | Equivalent controls match across roles; no CSS specificity patch added without documenting the cause; offline fonts load. |
| UX-02 | Extract a shared shell with role-aware navigation, page title, account area and responsive sidebar; preserve contextual navigation for Teachers entering shared management. | `Server/Views/Admin/_Layout.cshtml`, `Teacher/_TeacherLayout.cshtml`, `Teacher/_Layout.cshtml`, `Student/_StudentLayout.cshtml` | Active link is correct for controller and action; browser back and sidebar state work; no lost role-specific actions. |
| UX-03 | Standardize page headers, one primary action per task area, table density, row actions, search/filter/reset and pagination placement. | Admin/Teacher CRUD, histories, reports and alerts views | A user can transfer the same interaction pattern between lists; long names and empty results remain readable. |
| UX-04 | Standardize loading, empty, no-results, error, forbidden, offline and success states. Keep user input after validation failure; disable repeated submission while pending. | Shared partials, `Server/wwwroot/js/site.js`, form views | Every async flow has a terminal outcome and recovery path; no indefinite spinner or duplicate submit. |
| UX-05 | Finish branding, favicon/page titles, button labels, helper text and error copy. Keep technical details expandable when relevant to support. | Role layouts, login, Deployment Hub, `portal/`, WinForms dialogs | No broken asset requests in validated pages; consistent terminology; school branding remains intact. |
| UX-06 | Verify keyboard focus, modal focus return, labels, contrast, zoom, narrow layouts and Windows display scaling. | All shared controls and client forms | Core tasks work by keyboard; no clipped essential controls at 200% zoom/scaling; status is understandable without color. |

Deliver a pilot slice first: login, shared shell, Teacher monitoring and Student session. Validate these before migrating all pages. Capture before/after screenshots with identical fixtures and dimensions.

## Phase 2 — remove workflow friction

Priority: high. Dependencies: shared control patterns.

| ID | Change | Implementation notes | Acceptance |
| --- | --- | --- | --- |
| FLOW-01 | Add an Administrator setup checklist and guided client connection states. | Reuse deployment readiness/endpoint data; inspect installer provisioning first. Provide a clear required provisioning step if no usable Admin exists. Keep trusted certificate validation. | Clean setup can be completed without manual source/config edits; failures identify the next action. |
| FLOW-02 | Keep list filters, page and return location across details, edits and alert actions. | Prefer URL query parameters for shareable state; avoid persisting sensitive input. Explicitly handle invalid/stale filters. | Browser refresh/back restores context and exports use the displayed scope. |
| FLOW-03 | Separate transport connection, session lifecycle and remote-control state in the UI. | Distinct labels for Reconnecting, Session paused and Control active. Reconcile full live state after reconnect; mark last frame age and guard commands. | Simulated outage cannot display a frozen frame as current or a session pause as disconnection. |
| FLOW-04 | Make remote command progress and partial failure explicit. | Use existing command IDs/results/audit records where possible. Display pending, acknowledged, failed and outcome unknown; correlate target identity. | Every result refers to the intended target; no automatic replay of shutdown/restart after uncertainty. |
| FLOW-05 | Reduce interruption while keeping important events visible. | Build on current toast/notification behavior; deduplicate repeated notices, provide persistent history, reserve blocking dialogs for decisions requiring action. | Repeated events do not stack obstructive dialogs; notices stay accessible and critical failures remain visible. |
| FLOW-06 | Clarify unavailable browser monitoring and recovery guidance. | Translate managed/fallback/unavailable collector states into plain labels; show useful details on demand. | Limited collection is never presented as “no activity”; reconnect guidance reflects the actual failure. |

Use the current services and API contracts first. If a new endpoint or schema field is necessary, document why, preserve compatibility and include migration/contract tests.

## Phase 3 — simplify code while preserving behavior

Priority: high for frequently changed paths; incremental for the rest. Dependencies: regression tests for affected paths.

1. Extract business operations from AdminController and TeacherController into existing or focused services and strongly typed request/view models. Start with duplicated class/roster/account workflows and shared exports. Keep controllers responsible for HTTP concerns and preserve existing route/action names during the first pass.
2. Move database queries and scope predicates out of Razor layouts into a view component/query service. Reuse the same scope implementation for alert counts and lists where semantics match. Validate positive and negative authorization examples before and after extraction.
3. Separate MainForm's visual construction from connection lifecycle, session state, policy enforcement and notifications in small steps. Build on existing Client/Services interfaces. Ensure event subscriptions, cancellation and disposal have an explicit owner.
4. Extract monitoring and page-specific JavaScript from Razor into focused local files. Pass server data through encoded, minimal configuration. Preserve script ordering, escaping, antiforgery and asset versioning. Each initialized feature should support cleanup and avoid duplicate handlers.
5. Consolidate shared domain/status constants only where meanings match. Session state, workstation status, connectivity and alert state remain separate concepts. Preserve serialized contract values and test old/new compatibility before renaming them.
6. Consolidate CSS after identifying active selectors and computed styles. Remove obsolete rules only after reference checks and representative visual comparisons. Audit dynamic Razor/JavaScript class construction before deleting apparently unused selectors.
7. Tighten exception handling and structured diagnostics around network, commands, startup and persistence. Log actionable failures and correlation identifiers without passwords, private certificates or real screen content. Client messages should remain understandable to teachers/students.
8. Inventory the nullable warnings globally suppressed in `Server/Server.csproj`. Remove suppressions by feature as null validation/model initialization is corrected; avoid blanket null-forgiving operators. Verify missing-account/session/workstation responses and establish no-new-warning checks for cleaned areas.
9. Characterize logout and session-end behavior in `AuthenticationService` and `LabSessionLifecycleService`, then consolidate repeated transitions where semantics match. Prove UTC duration, final state and workstation availability remain correct for pause/resume/end/expiry. This is a consolidation opportunity, not a confirmed lifecycle bug.

Use explicit dependency injection for extracted services; review AdminController's optional-dependency fallback construction so tests and production exercise the same dependencies. Diagnostics in capture/retry loops must be bounded and rate-limited: a failing frame must not produce an unbounded stream of logs. Test repeated login/reconnect/end cycles for duplicate handlers and capture loops.

Exit for each extraction: relevant tests pass, routes/roles/contracts and database results are unchanged, behavior is demonstrably easier to test, and unrelated files are not reformatted. Avoid introducing a generic framework for one call site.

## Phase 4 — clean the repository and delivery process

Priority: medium. Dependencies: inventory and build verification.

- Add agreed `.editorconfig` formatting and line-ending rules, then apply them to changed files. Keep mass formatting separate if it is ever needed.
- Inventory generated files, runtime data, vendor assets, fonts/licenses and checked-in installers. Keep source and release artifacts distinguishable. The current repository intentionally tracks installers: moving them to release storage is a separate policy change that must update CI, download links and packaging checks together.
- Centralize release version input and validate propagated application/installer/portal versions. Preserve the working offline fallback for the public portal.
- Make the test entry point obvious: add test projects and cleaner to an appropriate solution/build entry point or provide one documented runner that includes them. Preserve the canonical package script and explicit CI test execution.
- Add deterministic prerequisite/dependency checks where useful; choose SDK and restore policy based on the supported build environment. Avoid adding frontend package tooling solely to organize a handful of local scripts.
- Upload test results and useful failure logs in CI. Separate baseline checks from slow installer/interactive validation without letting either disappear from release acceptance.
- Preserve reusable test harnesses and sanitized evidence in stable locations. Historical reports point to temporary scratch directories; record retention-backed artifact links or a reproducible command and fixture when raw evidence cannot be retained.
- Give docs a single index: user guide, deployment/troubleshooting, architecture, test execution and improvement ledger. Reconcile stale report summaries with dated addenda while preserving historical evidence.
- Delete only proven unused code/assets after reference and runtime checks. Never delete migrations, backups, databases, certificate stores, vendored licenses or archived test evidence as cosmetic cleanup.

Exit: a fresh checkout has an unambiguous build/test path, documented artifact ownership and no accidental runtime secrets/data. Existing release and deployment flows still work.

## Phase 5 — prove the experience under real conditions

Priority: release gate. Dependencies: implemented slices and suitable test environment.

- Run relevant server/client tests after each behavioral change and the full suites for the final candidate. Map changes to [test case families](../testing/TEST-CASES.md).
- Target decision branches around authorization, session expiry, reconnect and command acknowledgement; the historical branch-coverage figure warrants focused tests, not a cosmetic coverage target.
- Walk through the redesigned journeys with representative Admin/Teacher/Student operators. Compare task completion, errors and backtracking to Phase 0. Rework points where operators need verbal guidance.
- Use two disposable Windows clients for real capture/control, connection loss and recovery. Validate keyboard input, scaling, app/domain policy and secure-desktop behavior.
- Rehearse clean install, populated upgrade, uninstall/reinstall and restore on snapshot-backed VMs. Validate the exact candidate hashes.
- Measure typical lab load, first usable dashboard, frame age, command latency, CPU/memory and queue growth. Agree client count and numeric limits before performance signoff. The 50 ms capture target remains an interval, not a promised frame rate.

Exit: all required high-priority tests and journeys have evidence; blocked live validation stays explicitly open. A polished interface alone does not establish release readiness.

## Delivery order and tracking

| Batch | Deliverable | Depends on | Test families |
| --- | --- | --- | --- |
| 0 | Current baseline, route/component inventory, task observations | None | BLD, UI, DOC |
| 1 | Shared visual/control patterns and four-screen pilot | 0 | UI, AUTH, WIN |
| 2 | Apply patterns to remaining pages and shared management navigation | 1 | UI, CRUD, ALT, REP |
| 3 | Connection recovery, command feedback and guided setup | 1; relevant regression tests | HUB, NET, TLS, SES, DEP, INS |
| 4 | Controller/layout/client/script extractions in small domain slices | Baseline tests for each slice | AUTH, CRUD, SES, TEL, DB, HUB |
| 5 | Repository/CI/docs cleanup and version checks | Inventory; build validation | BLD, DEP, INS, WEB, DOC |
| 6 | Complete journey, LAN, capacity and installer acceptance | 2–5 | Full applicable catalogue |

Code cleanup can accompany a related UX slice when it reduces risk; avoid making all cleanup a prerequisite for visible improvement. Estimate each batch after baseline inspection and environment discovery. Track completion by accepted deliverables rather than files changed.

Each work item records: ID, observed problem, user outcome, source files, dependencies, proposed change, evidence before/after, test results, rollout/rollback, owner and status. Use NOT STARTED / IN PROGRESS / VERIFIED / BLOCKED; VERIFIED requires evidence. Suggested outputs: `docs/improvements/PROGRESS.md`, `DECISIONS.md`, `JOURNEY-RESULTS.md` and sanitized screenshots with source revision.

## Definition of finished

- Shared controls and terminology are consistent across browser roles and Windows client; normal tasks have clear next steps.
- Connected, stale, paused, pending, failed and completed states accurately reflect server/client state.
- Users retain useful task context; errors offer a recovery action; routine interactions do not cause unnecessary interruption.
- Role boundaries, auditability, data preservation and TLS checks remain intact.
- Business logic is outside views, extracted code has clear responsibility, and cleanup removals have evidence of non-use.
- Build/test/package instructions reproduce the candidate and required live acceptance is recorded.
- The documentation states what changed, what was verified and what still needs an environment or decision.

## Copy-ready implementation handoff

> Implement docs/improvements/PROFESSIONALIZATION-PLAN.md in phased, reviewable changes. Begin by reconciling the current code and latest test addenda, preserving existing work, and capturing the baseline. Start with the shared visual/control patterns and the login, Teacher monitoring and Student session pilot. Improve setup, connection recovery, context retention and command feedback while preserving current roles, routes, contracts, data and certificate validation. Refactor only the responsibilities needed for each slice and prove unused assets before removal. Keep PROGRESS.md updated with item IDs, commits, screenshots and mapped test evidence. Use docs/testing/AGENT-HANDOFF.md for validation; record unavailable live tests as blocked. Do not publish a release or alter real deployments as part of implementation. Finish with completed outcomes, evidence and exact remaining work.
