# Improvement progress ledger

Tracks [the professionalization plan](PROFESSIONALIZATION-PLAN.md). One row per
work item; the detail sections below carry the fields the plan requires (observed
problem, user outcome, source files, dependencies, change, before/after evidence,
test results, rollout/rollback, owner, status).

**Status values:** NOT STARTED / IN PROGRESS / VERIFIED / BLOCKED.
VERIFIED requires evidence recorded here — a passing test run, a browser check or
a reference check. "It compiles" is not evidence.

**ID scheme:** Phase 1 and 2 use the plan's own IDs (UX-nn, FLOW-nn). Phases 3-5
are numbered lists and bullets in the plan with no IDs, so this ledger assigns
CODE-nn (Phase 3, following the plan's list order), OPS-nn (Phase 4 bullets, in
order) and LIVE-nn (Phase 5 bullets, in order).

Repository: `ACTstudent/RemoteAcessMonitoringSoftware4sale`, branch `main`.
Owner for every item below: implementation agent, unless a person is named.

## Summary

**37 work items. 14 VERIFIED (38%), 4 IN PROGRESS, 5 BLOCKED, 14 NOT STARTED.**

Counting only what can be finished on this machine — that is, setting aside the
5 items blocked on a second Windows client, a disposable network and snapshot
VMs — it is 14 of 32, or 44%.

Two caveats on reading that number. It counts items, not effort: the 14 not-started
items include the largest single pieces of work in the plan (`AdminController` at
1072 lines, `TeacherController` at 847, `MainForm` at 1178), so the share of
*effort* completed is lower than the share of items. And VERIFIED here means
evidence is recorded below, not that the item is beyond further improvement —
UX-06 was VERIFIED and a later, wider sweep still found four more problems.

| ID | Item | Status | Commit |
| --- | --- | --- | --- |
| P0 | Baseline capture and reconciliation | VERIFIED | `acc8761`, `e477560` |
| CODE-02 | Move layout database queries into a view component | VERIFIED | `45b07d9` |
| CODE-04 | Extract page JavaScript from Razor into local files | IN PROGRESS | `265fd4e` |
| CODE-08 | Remove the global nullable suppression | VERIFIED | `fb590c7`, `3b06603` |
| CODE-09 | Session lifecycle duration and expiry correctness | VERIFIED | pre-plan, see below |
| OPS-01 | `.editorconfig` and line-ending rules | VERIFIED | `35cd09e` |
| OPS-04 | Make the test entry point obvious | VERIFIED | `37e91b5` |
| OPS-06 | Upload test results and failure logs in CI | BLOCKED | branch `ci-test-results` |
| OPS-08 | One documentation index | VERIFIED | `ab6fe95` |
| OPS-09 | Delete proven-unused code after reference checks | IN PROGRESS | `6fcf499` |
| UX-03 | Standardize table density, row actions and pagination | VERIFIED | `8d1328c`, `860e200`, `0769992` |
| UX-01 | Consolidate design tokens | VERIFIED | `f15fece`, `3d2e552`, `8a7f000` |
| UX-02 | Shared shell with role-aware navigation | VERIFIED | `0485327` |
| UX-04 | Standardize states; no duplicate submit | VERIFIED | `c00cc27`, `90fc671`, `abecf7b`, `860e200`, `dcd6d72`, `0a82e92`, `0769992` |
| UX-05 | Branding, favicon, titles, copy, offline assets | VERIFIED | `b9c0e63`, `3d2e552`, `a83a15e` |
| UX-06 | Accessibility pass (names, ids, landmarks) | VERIFIED (web) | `f7b97f4`, `1d46c9c` |
| FLOW-02 | Keep list filters, page and return location across actions | VERIFIED | `395abe9`, `8d1328c`, `dea6dae` |
| FLOW-01, 03, 04, 05, 06 | Setup checklist, connection state, command feedback, interruption, collector states | NOT STARTED | — |
| CODE-01, 03, 05, 06, 07 | Controller/client/constant/CSS/diagnostics work | NOT STARTED | — |
| OPS-02, 03, 05, 07 | Generated-file inventory, version input, prerequisite checks, evidence retention | NOT STARTED | — |
| LIVE-01, LIVE-02 | Test suites and decision-branch coverage | IN PROGRESS | see below |
| LIVE-03…06 | Operator journeys, two-client LAN, install rehearsal, capacity | BLOCKED | needs environment |

## Verified items

### P0 — Baseline capture and reconciliation

- **Observed problem:** the plan cannot be measured without a recorded starting point.
- **Source:** `docs/testing/runs/20260905-125626-b5441f1/` (SUMMARY.md, CASE-RESULTS.csv
  with 66 cases, SOURCE-COVERAGE.csv, DEFECTS.md, NEXT-STEPS.md,
  ADDENDUM-install-and-DEF-001.md).
- **Evidence:** the run's own artifacts. The addendum reconciles the summary with the
  later install and the DEF-001 font fix, so the historical report and its correction
  are both preserved rather than the summary being edited in place.
- **Result:** baseline exists and is dated. DEF-001 (interface fonts not loading) is closed.
- **Status:** VERIFIED.
- **Not covered:** the plan also asks for timed observations of five workflows with
  representative operators. That is recorded in [JOURNEY-RESULTS.md](JOURNEY-RESULTS.md)
  and is BLOCKED — see LIVE-03.

### CODE-02 — Move layout database queries into a view component

- **Observed problem:** `Teacher/_TeacherLayout.cshtml` injected `ApplicationDbContext`
  and ran its own copy of the student-scope predicate for the sidebar alert badge.
  When the scope changed to global access, the layout's copy was not updated, so the
  badge and the alerts page reported different totals.
- **User outcome:** a teacher sees the same open-alert number in the sidebar and on
  the alerts page.
- **Source files:** `Server/Views/Teacher/_TeacherLayout.cshtml`,
  `Server/ViewComponents/OpenAlertCountViewComponent.cs` (new),
  `Server/Views/Shared/Components/OpenAlertCount/Default.cshtml` (new),
  `Server/Services/AnalyticsService.cs`, `Server/Controllers/TeacherController.cs`.
- **Dependencies:** none.
- **Change:** added `IAnalyticsService.GetOpenAlertGroupCountAsync`, which owns the
  scope. The layout, the badge and the controller's `OpenAlertCount` endpoint all call
  it. The layout no longer injects a database context.
- **Evidence before:** two independent queries, one of them stale.
  **After:** one implementation with three callers; the layout holds no data access.
- **Test results:** server 320/320, client 33/33.
- **Rollout/rollback:** no migration, no contract change. Revert `45b07d9`; the view
  component is additive and nothing else depends on it.
- **Status:** VERIFIED.

### CODE-08 — Remove the global nullable suppression

- **Observed problem:** `Server/Server.csproj` carried a project-wide `NoWarn` list
  that hid every nullable warning in the server, so a real null dereference anywhere
  in the project would have compiled silently.
- **User outcome:** an unauthenticated hub connection produces a handled authorization
  failure instead of a `NullReferenceException`.
- **Inventory (the plan asks for one before removal):** the suppression was covering
  **20 warnings, all CS8602, all in `Server/Hubs/RemoteMonitoringHub.cs`**, all
  dereferencing `Context.User` — which SignalR types as nullable because a connection
  need not be authenticated. No other file in the project relied on the suppression.
- **Change:** a checked `Principal` property throws `HubException` when `Context.User`
  is null; the 17 dereferences go through it. The `NoWarn` line was removed entirely
  rather than narrowed, and a comment records why it should not return. No
  null-forgiving operators were introduced.
- **Evidence after:** the Release build reports **0 Warning(s), 0 Error(s)** with the
  suppression gone. That build result is the plan's "no-new-warning check" for this area.
- **Test results:** server 320/320, client 33/33.
- **Rollout/rollback:** revert `fb590c7`.
- **Status:** VERIFIED.

### FLOW-02 — Keep list filters, page and return location (alerts list)

- **Observed problem:** every alert action redirected to a bare `Alerts` URL with
  `includeAcknowledged=true` pinned on. Four defects came out of that one missing piece:
  1. Acknowledge, reopen and the three bulk actions discarded severity, dates, student,
     station, status and page.
  2. Those redirects forced acknowledged alerts back into a list the teacher had
     deliberately narrowed to open ones.
  3. `ExportAlertsCsv` ignored `includeAcknowledged` and defaulted to every status, so a
     list filtered to open alerts exported handled ones too — the plan's "exports use the
     displayed scope" acceptance was failing.
  4. "All statuses" in the filter form did nothing: it submitted a blank status with no
     `includeAcknowledged`, which resolved straight back to open alerts only.
- **User outcome:** a teacher can work down a filtered alert queue without rebuilding the
  filter after every action, and the exported file matches the screen.
- **Source files:** `Server/Models/AlertListModels.cs` (new),
  `Server/Controllers/TeacherController.cs`, `Server/Views/Teacher/Alerts.cshtml`,
  `Server/Views/Teacher/_AlertFilterFields.cshtml` (new),
  `Server.Tests/Controllers/TeacherAlertFilterTests.cs` (new).
- **Dependencies:** none.
- **Change:** `AlertListFilter` is the single place that decides which alerts a filter
  selects and how it is written back to a URL, so the list, the paging links and the
  export cannot disagree. It travels with each action through a hidden partial and comes
  back out of the redirect. Filter state stays in URL query parameters, as the plan
  prefers, so a filtered list is shareable and survives refresh and back.
- **Invalid and stale filters, which the plan asks be handled explicitly:** a backwards
  date range — which a teacher can produce from the form — keeps the entered dates and
  explains itself instead of returning a bare 400; out-of-range paging from a stale or
  hand-edited link is pulled into range, and the address bar is corrected to the page
  actually shown so a wrong URL is not bookmarked; an unknown status name is still rejected.
- **Evidence after:** headless browser, 9/9 checks — query values bind and render, the
  bulk form carries the filter, "All statuses" holds, a backwards range warns while
  keeping input, out-of-range paging returns 200, the export link carries the scope, and
  acknowledging a row under `?severity=Critical&station=LAB-07` lands back on that same URL.
- **Test results:** 25 new tests; server **344/344**, client **33/33**.
- **Rollout/rollback:** no migration, no route or action-name change; existing alert URLs
  still bind. Revert `395abe9`.
- **Status:** VERIFIED for the alerts list. The same retention is not yet applied to the
  other lists (students, computers, records, remote history), so FLOW-02 as a whole is
  not finished.

### CODE-09 — Session lifecycle duration and expiry correctness

Completed before the plan was adopted; mapped here because the plan lists it as a
consolidation opportunity and describes it as "not a confirmed lifecycle bug". It was
in fact a bug.

- **Observed problem:** `LabSession` timestamps round-trip through SQLite as
  `DateTimeKind.Unspecified`. Subtracting them against `DateTime.UtcNow` added the
  local offset, so on UTC+8 a 43-second-old session measured as roughly 480 minutes old
  and was expired immediately. This is the most likely cause of the original
  "lab session breaking everything" report.
- **Source files:** `Server/Services/LabSessionLifecycleService.cs`,
  `Server/Extensions/DateTimeDisplayExtensions.cs` (new), `Server/Views/_ViewImports.cshtml`.
- **Change:** a single `ToUtc` normalizer treats Unspecified as UTC and converts Local;
  elapsed time is computed from normalized values and clamped at zero, and a paused
  session measures to its pause time. A `ToDisplayLocal()` extension does the reverse
  for display only, so storage stays UTC.
- **Test results:** covered by the server suite (320/320), including pause, resume and end.
- **Status:** VERIFIED.

### OPS-01 — Formatting and line-ending rules

- **Observed problem:** line endings had no stated rule. The convention lived only in
  one machine's `core.autocrlf`, so every commit written by a tool that emits LF
  produced a renormalization warning and nothing recorded what was intended.
- **Change:** `.gitattributes` is the binding rule; `.editorconfig` describes the style
  already in use (four spaces, Allman, UTF-8). Windows batch, PowerShell and Inno Setup
  scripts check out CRLF. Tracked installers and fonts are marked binary, because a
  line-ending conversion applied to a release artifact would corrupt it.
- **Evidence:** every text file in the repository was already stored with LF — 247 of
  them, no exceptions — so `text=auto` records the existing state. `git status` after
  adding the file showed only the new files themselves: **zero renormalization**.
- **Deliberately not done:** no file was reformatted. The plan asks that mass formatting
  stay separate, and a whitespace diff is exactly where a behavioral change hides.
- **Status:** VERIFIED.

### OPS-04 — Make the test entry point obvious

- **Observed problem:** `dotnet test` on `RemoteMonitoring.sln` ran nothing and said so
  quietly. The solution held Server, Client and Shared and neither test project, so a
  fresh checkout produced a green, empty result and the 378 existing tests were
  reachable only by naming their `.csproj` files.
- **Change:** `Server.Tests`, `Client.Tests` and the `CamsDbCleaner` tool are in the
  solution. The canonical build writes TRX into `test-results/`, added to
  `build-everything.ps1`'s directory allowlist rather than loosening that guard.
- **Evidence after:** `dotnet test "Monitoring And Remote Access/RemoteMonitoring.sln"`
  runs **378/378** (server 345, client 33). TRX written. `build-everything.ps1` parses.
- **Not run:** the packaging steps, because they rebuild the tracked installers.
- **Status:** VERIFIED.

### OPS-08 — One documentation index

- **Observed problem:** seventeen documents existed; the README linked nine and omitted
  the test cases, the agent handoff, the source inventory, every run report and all three
  improvement ledgers. Separately, the 20260905 run summary described a defect as
  affecting every page and never mentioned the addendum that closed it, so the first
  thing a reader met was out of date.
- **Change:** `docs/README.md` is the index, grouped as the plan asks — using CAMS,
  deploying and troubleshooting, architecture, running the tests, improvement ledger —
  and ends with what is deliberately undocumented, so those gaps read as known rather
  than missed. The run summary now opens with a dated notice pointing at its addendum,
  with the original outcome left exactly as written.
- **Evidence after:** all **32 links and anchors resolve**, checked programmatically.
- **Status:** VERIFIED.

## In-progress items

### UX-03 — Table and pagination patterns

- **Observed problem:** `RemoteHistory` accepted a page number and its query returned a
  paged result of 100 per page, but the view rendered no controls at all. **A command
  audit longer than one page was unreachable through the interface** — the data was
  there and there was no way to ask for it. The alerts list had a working pager written
  inline, so there was one implementation and one omission rather than a shared pattern.
- **User outcome:** every paged list has page controls in the same place behaving the
  same way, and paging never resets the filter that produced the list.
- **Source files:** `Server/Models/PagerViewModel.cs` (new),
  `Server/Views/Shared/_Pager.cshtml` (new), `Server/Views/Teacher/Alerts.cshtml`,
  `Server/Views/Teacher/RemoteHistory.cshtml`.
- **Accessibility:** an unavailable direction renders as a `span`, not a link carrying
  Bootstrap's `.disabled` class. That class stops a mouse and not the keyboard, so the
  old markup gave a keyboard user a focus stop leading to `page=0`.
- **Evidence after:** headless browser, 9/9 — the history pager reports "150 commands,
  Page 1 of 2", page 2 is reachable and shows rows, paging keeps `?command=`, alerts
  still page with severity, station and `pageSize` intact, a single page of results
  renders no pager, and the disabled direction is not focusable.
- **Test results:** 378/378 through the solution.
- **Remaining:** the rest of UX-03 — page headers, one primary action per task area,
  table density, row actions, and search/filter/reset placement — is not done. Only the
  pagination half of the item is.
- **Status:** IN PROGRESS.

### Observation not yet acted on

`RemoteHistory` shows all teachers' remote-support sessions in its upper table
(`RemoteControlSessions`, no teacher filter) while the command audit below it is scoped
to the signed-in teacher (`RemoteCommandLogs.TeacherId == teacherId`). Under the global
access decision ([D-001](DECISIONS.md)) neither is wrong, but the page presents two
different scopes under one heading that says "Your remote-support sessions". Changing
either scope is a product decision, not a refactor, so it is recorded here rather than
made.


### CODE-04 — Extract page JavaScript from Razor into local files

- **Done:** `Server/Views/Teacher/Monitoring.cshtml` 483 → 97 lines; 385 lines moved
  unchanged into `Server/wwwroot/js/teacher-monitoring.js`, loaded with
  `asp-append-version` so cache busting follows the file hash.
- **Preconditions checked before moving:** the block contained no Razor interpolation,
  so no server data needed encoding and no escaping behavior changed; `node --check`
  passed on the extracted file; script ordering was preserved.
- **Evidence after:** headless-browser load of `/Teacher/Monitoring` — the script is
  requested and returns **200**, the workstation grid renders, the live counter binds,
  and the page produces **zero console errors and zero failing requests**.
- **Test results:** server 320/320, client 33/33.
- **Remaining:** other views still hold inline script. The plan's "support cleanup and
  avoid duplicate handlers" requirement is not yet met — the extracted file still wires
  its handlers at load with no teardown path.
- **Status:** IN PROGRESS.

### OPS-09 — Delete proven-unused code after reference checks

- **Done:** removed `Server/Views/Teacher/_Layout.cshtml`, a second teacher layout
  sitting beside `_TeacherLayout.cshtml`.
- **Reference check performed before deletion:** no view sets `Layout = "_Layout"`;
  no path reference anywhere in the repository; no `_ViewStart.cshtml` exists that could
  select it implicitly; all 18 teacher views use `_TeacherLayout`.
- **Why it mattered:** UX-02 asks for one shared shell. Two candidate layouts, one of
  them dead, is the ambiguity that item exists to remove.
- **Status:** IN PROGRESS — this is one evidenced removal, not the full inventory the
  plan asks for. Migrations, backups, databases, certificate stores and archived test
  evidence are explicitly out of scope for deletion.

### Phase 1 and 2 items are complete

UX-01 to UX-06 and FLOW-02 are verified; their entries are below. FLOW-01 and
FLOW-03 to FLOW-06 have not been started.

| Item | Delivered | Not yet done |
| --- | --- | --- |

### LIVE-01 / LIVE-02 — Test suites and decision-branch coverage

- Server **442/442** and client **33/33** pass at `ec2d3f8`, and were run after each
  behavioral change. The suite has grown from 320 to 442 over the plan.
- The plan asks for focused tests on the authorization, session expiry, reconnect and
  command acknowledgement branches. **Two of the four are now covered:** session expiry
  (CODE-09) and authorization. Reconnect and command acknowledgement are not, and both
  need a real SignalR client harness rather than the mocked hub tests that exist.
- **Authorization, added in run 20260906-054337-8407e72.** The test plan had flagged
  that no test file existed for `StudentController`, `ClientAuthController` or
  `MonitoringController` and asked whether indirect coverage made up for it. Measured:
  it did not — all three were at **0.0%**. They are now at 96%, 100% and 100%, and the
  authorization surface was checked three ways against a running server: all **153**
  controller actions refuse an anonymous caller, all **23** `AdminController` GET
  actions match their `[TeacherSharedAction]` attribute, and deactivating a teacher
  ends access on the session already signed in.
- **Coverage:** 69.3% → 71.3% of lines across 89 non-view, non-migration files
  (branches 51.3% → 53.6%). Largest remaining gaps are `Program.cs` (0%, exercised
  live but never unit tested), `ServerDiscoveryService` (0%, blocked on a disposable
  network) and `RemoteMonitoringHub` (56%, mocked only).
- **Evidence:** [run 20260906-054337-8407e72](../testing/runs/20260906-054337-8407e72/SUMMARY.md)
  and its [addendum](../testing/runs/20260906-054337-8407e72/ADDENDUM-defect-fixes.md).
- **Status:** IN PROGRESS. Closing this needs the SignalR harness, which is the same
  prerequisite as HUB-01 to HUB-03 in the test plan.

## Blocked items

### OPS-06 — Upload test results and failure logs in CI

**Written, verified locally, and not pushed.** The change lives on the local branch
`ci-test-results` (commit `9e9e641`).

- **Observed problem:** a failing CI run left a red step and nothing else, so the first
  move after any failure was to reproduce it locally.
- **Change:** both workflows write TRX and upload it with `if: always()`, which is the
  case that matters. The split is preserved and now stated: `test.yml` is the fast
  baseline on ubuntu and runs the server tests only, because `Client.Tests` targets
  `net8.0-windows` and cannot build there; `ci-full.yml` is the slow Windows build that
  packages and validates the installers and covers both suites.
- **Blocked on:** the GitHub token authorising pushes from this machine holds
  `gist, read:org, repo` and not `workflow`, so GitHub refuses any push that touches
  `.github/workflows/`. This is an authorisation limit, not a problem with the change.
- **To land it:** grant the scope, then merge the branch.

  ```
  gh auth refresh -h github.com -s workflow
  git merge ci-test-results && git push origin main
  ```

- **Status:** BLOCKED.

### Environment-blocked items

| ID | Item | Blocked on |
| --- | --- | --- |
| LIVE-03 | Operator walkthroughs of the five workflows | Representative Admin/Teacher/Student operators |
| LIVE-04 | Real capture, control, connection loss and recovery | A second disposable Windows client machine |
| LIVE-05 | Clean install, populated upgrade, uninstall/reinstall, restore | Snapshot-backed disposable VMs |
| LIVE-06 | Lab load, frame age, command latency, capacity | A disposable network, plus an agreed client count and numeric limits — none are approved |

These stay open by the plan's own rule: blocked live validation is recorded rather than
waived, and a polished interface alone does not establish release readiness.

---

## Later additions

### UX-06 — Accessibility pass across the browser portals

- **Observed problem:** an audit of the rendered pages found **44 problems on the 12
  teacher pages and 128 on the 16 admin pages**. Almost all were the same thing: text
  that reads as a label on screen but is connected to no control, so a screen reader
  announces an unnamed field and clicking the label does nothing. Some controls were
  named only by a placeholder, which disappears the moment the field has content — the
  name vanishes exactly when someone checking their work needs it. A few close buttons
  announced only as "button".
- **Change:** every label now carries a `for` and every control an `id`, with ids inside
  a loop suffixed by the row key the views already use for their modal ids. Controls
  that repeat per row or are cloned by script — the bulk student grid, the per-row class
  and workstation selects — take an `aria-label` instead, which cannot collide. The
  admin Restrictions field generator, which renders the same fields for both an add and
  an edit dialog, now takes a scope so the two cannot share ids.
- **Evidence after:** the same audit reports **0 problems across all 28 pages**, with no
  duplicate ids and no label pointing at a missing control. The student portal was
  checked and needed no changes.
- **Regression checks, because markup moved under 90+ controls:** a restriction rule
  saves every field through the rewritten generator, the per-rule edit dialog saves,
  creating a class posts every field from both portals, and clicking a label moves focus
  into its field.
- **Test results:** 378/378.
- **Two mistakes worth recording**, both caught by the build and the audit rather than by
  reading the diff: the first automated pass took the nearest preceding `-@x.Id` as a row
  key when that variable belonged to an earlier, already-closed loop, and it lowercased
  whole ids including the Razor expression, renaming the property being read. Loop
  extents are now found by matching the `@foreach` header's parentheses and then the
  body's braces, and only the static half of an id is lowercased.
- **Second pass, run 20260906-054337-8407e72.** The first pass covered 28 pages. A
  wider sweep of 45 pages found four more problems on pages it had not reached, and one
  it could not reach: `/Teacher/ClassDetails` was skipped because the fixture teacher
  did not own the class, and once assigned it turned out to have **more** unnamed
  controls than its admin twin — the single add-student form, the roster file input and
  the search box, on top of the bulk grid. Also fixed: five unnamed controls on
  `/Admin/ClassDetails` including a password field, and a table with no header cells on
  `/Teacher/StudentDetails`. `/Admin/ComputerHistory` turned out to be worse than an
  accessibility problem — it was the only view in the application naming no layout, and
  with no `_ViewStart` it rendered with no navigation or styling at all.
- **Evidence after the second pass:** **0 problems across all 45 pages**, in
  [ui-sweep-after-fixes.csv](../testing/runs/20260906-054337-8407e72/ui-sweep-after-fixes.csv).
  Commit `ec2d3f8`.
- **One finding withdrawn:** "images without alt on the 404 page" was wrong. All five
  `<img>` tags in the application already carry `alt`; those images belonged to
  Chrome's own error page. The sweep now audits only `text/html` responses, so JSON
  endpoints no longer report a missing landmark either.
- **Status:** VERIFIED for the browser portals. The WinForms client has not been
  audited, and UX-06 also asks for zoom, scaling and narrow-layout checks, which
  neither pass covered.

### FLOW-02 — extended to the student roster

Searching the roster and then editing or removing a student returned the full
unfiltered list, so a teacher working through several matches re-typed the search each
time. Eight redirects now carry the search and the edit and remove forms post it back.
The fallback redirect in `StudentDetails` was deliberately left alone: it is reached
without a list behind it.

Verified: `?search=ana` shows one row, the edit form carries `search="ana"`, and saving
returns to `/Teacher/Students?search=ana`.

FLOW-02 is now complete, and the survey that settles it is worth recording: the only
server-side list filters in the product are the alert filter and the roster search,
both handled. Remote history is read-only and its filter travels with the pager.
Teacher Computers and Records take no filter parameters and render no filter form.
No admin list action takes a filter parameter either - admin filtering is done in the
browser - so there is no server-held filter state anywhere else to lose.

### UX-02 — One shared shell for the three portals

- **Observed problem:** Admin, Teacher and Student each carried a full copy of the page
  shell — sidebar, header, scripts — **472 lines of near-identical markup** across three
  files. Any change to the shell had to be made three times and stayed consistent only
  by care.
- **The bug that copy caused:** each layout decided which sidebar link was current by
  comparing the **action name alone**. Opening `/Admin/Students` from the teacher portal
  therefore highlighted the teacher's own "Student Profiles" link, because both actions
  are called `Students`. Detail pages with no sidebar entry of their own — class details,
  student details, alert history — highlighted nothing at all.
- **User outcome:** the sidebar always marks the page you are on, and the three portals
  behave identically because they are the same shell.
- **Source files:** `Server/Views/Shared/_AppLayout.cshtml` (new),
  `Server/Models/NavigationModel.cs` (new), `Server/Services/NavigationBuilder.cs` (new),
  `Server/Views/Shared/_StudentTopbar.cshtml`, `_StudentSessionScript.cshtml`,
  `_TeacherAlertBadgeScript.cshtml` (new), and the three old layouts, now 12 lines each.
- **Change:** what differs between the portals is a link list and some branding, so it is
  now data. Matching requires controller **and** action, and an item can name further
  actions it should stay current for.
- **Added for every portal rather than one:** a skip-to-content link (the student portal
  had no keyboard route past the sidebar), `aria-current="page"` on the current link, and
  the same identity block in the header.
- **Evidence after:** 45 pages swept — 44 render with no console errors, no failed
  sub-requests and no accessibility findings; the 15 not-applicable rows are file
  downloads and one id the fixture does not hold. A dedicated navigation check passes
  **19 of 19**, including that `/Admin/Students` no longer marks the teacher's link, that
  detail pages keep their parent marked, and that a teacher in the global portal is still
  not offered the administrator-only links the authorization filter would refuse.
- **Test results:** server 442/442, client 43/43.
- **Rollout/rollback:** revert `0485327`. No route, contract or schema change.
- **Status:** VERIFIED.
- **Not covered:** responsive behaviour at narrow widths and 200% zoom is UX-06's
  remaining half and was not re-checked here.

### UX-01 — Design tokens, control variants and the client palette

- **Observed problem:** the colour palette existed as tokens but nothing else did.
  Corner radii were **ten ad-hoc values** — 7, 8, 10, 11, 12, 14, 16, 18, 20 and 999px —
  picked a rule at a time; there was no spacing scale; and the stylesheet held **one
  `:focus-visible` rule in 1,400 lines**. Because the custom pill buttons override
  Bootstrap's shadow-based focus ring, a keyboard user had little or nothing showing
  them where they were across most of the interface. Fifty-seven inline style
  declarations repeated three rules across twenty views.
- **User outcome:** a keyboard user can always see where they are, and equivalent
  controls look the same in all three portals because they are drawn from one set of
  values.
- **Source files:** `Server/wwwroot/css/site.css`, twenty views,
  `Client/MainForm.cs`, `docs/DESIGN-SYSTEM.md` (new).
- **Change:** radius, spacing and focus scales added as tokens; 20 radius declarations
  snapped to the four-step scale, none moving by more than 2px; 57 inline styles
  replaced by `.page-title`, `.surface-brand` and `.form-inline-action`; one focus ring
  applied to everything focusable through `:focus-visible`, switching to `#BBF3C6` on
  the dark sidebar.
- **Evidence after:** tabbing three real pages, **22 of 22 stops on each** show a 3px
  ring; the sidebar ring resolves to `rgb(187, 243, 198)`; the skip link is still the
  first stop and still moves on screen. The computed styles behind the inline-style
  swap were read back from the running page — letter-spacing `-0.5px`, weight 800,
  colour `rgb(33,37,41)`, brand surface `#16401F` — all unchanged. 9 of 9 checks.
- **Client palette:** every entry now names the web token it corresponds to and carries
  its measured contrast. One real divergence closed: `StatusOk` was `#157A3D` where the
  web success token is `#17803A`; both pass AA on white, 5.41:1 against 5.02:1, so the
  alignment costs a little contrast and stays well clear of the 4.5:1 bar.
- **Variants documented:** [DESIGN-SYSTEM.md](../DESIGN-SYSTEM.md) covers buttons,
  forms, tables, status badges, toasts, dialogs and the shell, written from what the
  code does rather than from what it ought to. Every token name and file path in it was
  checked to resolve.
- **One thing found while measuring:** the sidebar links carry a transition on all
  properties, so the ring faded in rather than appearing. The outline is now excluded
  from that animation.
- **Test results:** server 442/442, client 43/43; 44 pages render clean.
- **Rollout/rollback:** revert `8a7f000`, `f15fece` and `3d2e552`. CSS and markup only.
- **Status:** VERIFIED.
- **Not covered:** narrow viewports and 200% zoom, which belong to UX-06's remaining
  half; and the WinForms client's own control set, whose palette matches but whose
  layout is its own.

### UX-03 — Page headers, primary actions, table density and filter placement

- **Observed problem:** the pagination half was done earlier. The rest was not. Three
  pages had **two emerald buttons competing** for the eye — Reports made an export as
  prominent as the filter that drives the page, and Students and Sessions gave a
  per-row action the weight of the page primary. Table density came in five spellings.
  **One table sat outside `.table-responsive`** and so pushed the whole page sideways
  on a narrow screen instead of scrolling inside its card. Four of seven filter forms
  had no way to clear the filter.
- **User outcome:** the same interaction pattern transfers between lists — one obvious
  primary action, the same row density, filters that can always be cleared.
- **Change:** the three competing primaries are now outline; Settings keeps its two
  because they sit in separate cards and are separate task areas, which is what the
  rule allows. All 41 tables are inside a responsive wrapper and share one density,
  except the three compact bulk-entry grids which are meant to be denser. Every filter
  form has the same labelled Reset. `ClassAnalytics`, the least finished page, gained
  the standard header treatment.
- **Evidence after:** an audit script reports **0 tables outside a wrapper**, two
  densities where there were five, and every filter form carrying a reset. 44 pages
  render with no console errors or accessibility findings; the 19 navigation checks
  still pass.
- **Test results:** server 460/460, client 43/43.
- **Rollout/rollback:** revert `0769992` and `860e200`. Markup and CSS only.
- **Status:** VERIFIED.

### UX-04 — Loading, empty, error, forbidden and offline states

- **Observed problem:** the states were unevenly handled and two were missing outright.
  **A dropped connection was silent** — the teacher's alert badge stopped counting and
  a student stopped receiving warnings while the page looked live. **A refused request
  landed on the sign-in form**, telling someone already signed in to sign in, with a
  200 status and no way onward but the Back button. Twenty-eight empty tables were
  written six different ways. And **53 validation failures redirected without the
  submission**, so a duplicate username emptied the whole dialog.
- **User outcome:** every state says what happened and what to do next.
- **Change:** one connection indicator in the shared header, hidden while healthy;
  an AccessDenied page inside the user's own portal shell answering 403; one empty-state
  treatment; and `PreserveSubmissionFilter`, which carries a rejected submission back
  across the redirect and reopens the dialog it came from.
- **Evidence after:** connection state 12 of 12, cutting the page off at the browser and
  restoring it — including that detection fell from 25s to 11s once the SignalR
  keep-alive was tightened. Refused request 8 of 8. Preserved submission 9 of 9 plus 18
  unit tests. In-flight submit state 3 of 3: `aria-busy`, a disabled button, and release
  after 20s so a submission that never completes cannot lock the form.
- **Loading states:** there is no spinner anywhere and none is wanted. Every
  user-initiated action is a full navigation or a form post, and the only async fetches
  are three background polls that update in place. The in-flight form state above is the
  loading state.
- **Two mistakes worth recording.** The sensitive-field exclusion in the preserve filter
  matched by suffix, and the bulk roster form posts `bulkPasswords`, plural, which went
  straight through — passwords would have been written into the page. Found by writing
  the test, fixed to match by substring, and pinned. Separately, a first attempt at the
  in-flight test registered its listener on the form, whose `preventDefault` runs before
  the document-level guard and makes it skip: correct behaviour reported as a failure.
- **Test results:** server 460/460, client 43/43.
- **Rollout/rollback:** revert `0769992`, `0a82e92`, `dcd6d72`.
- **Status:** VERIFIED.
- **Not covered:** the WinForms client's own dialogs have not been reviewed for these
  states.

### UX-05 — Branding, titles, labels and copy

- **Observed problem:** CAMS had **no favicon at all**, so every tab showed a blank icon
  and every page load ended in a 404 for `/favicon.ico`. Action labels mixed two
  conventions — "Create Class" beside "Add more rows". Error copy capitalised **Teacher**
  mid-sentence while administrator and student stayed lowercase. And the desktop agent
  said "No Session" for the state the web calls "No session".
- **User outcome:** the product reads as one thing across the browser, the desktop agent
  and the public portal.
- **Change:** 16 action labels moved to sentence case, leaving page names and acronyms
  alone; 7 error messages made consistent on role words; the agent's two divergent
  labels aligned with the web.
- **One mark, not three.** The public portal already had a product mark. I had invented
  a second for the app favicon, which would have been a third design in the product.
  The app now serves the portal's mark verbatim — the drawing is byte-identical — so a
  browser showing the portal in one tab and the server in another sees one product.
  631 bytes, against the 825 KB school logo the tag would otherwise have pointed at.
- **Public portal:** already carries a title, meta description and its own icon. It uses
  a brighter palette than the application. Left alone deliberately: recolouring a public
  marketing page is a branding decision, not a consistency fix.
- **Windows dialog captions** keep their Title Case — that is the platform convention for
  a window title, and matching the web's sentence case there would look wrong on Windows.
- **Evidence after:** the mark serves at 200 as `image/svg+xml` and its drawing diffs
  clean against `portal/assets/cams-mark.svg`; no page reports a failed asset request in
  the 45-page sweep; server 460/460, client 43/43.
- **Rollout/rollback:** revert the copy commit. Text, one SVG and two label constants.
- **Status:** VERIFIED.
- **Not covered:** the agent's helper text was reviewed but not rewritten — it is clear
  and specific as it stands. The portal's palette divergence is recorded above as a
  decision rather than a defect.
