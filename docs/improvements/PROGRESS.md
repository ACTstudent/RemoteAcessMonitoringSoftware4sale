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

| ID | Item | Status | Commit |
| --- | --- | --- | --- |
| P0 | Baseline capture and reconciliation | VERIFIED | `acc8761`, `e477560` |
| CODE-02 | Move layout database queries into a view component | VERIFIED | `45b07d9` |
| CODE-04 | Extract page JavaScript from Razor into local files | IN PROGRESS | `265fd4e` |
| CODE-08 | Remove the global nullable suppression | VERIFIED | `fb590c7` |
| CODE-09 | Session lifecycle duration and expiry correctness | VERIFIED | pre-plan, see below |
| OPS-09 | Delete proven-unused code after reference checks | IN PROGRESS | `6fcf499` |
| UX-01 | Consolidate design tokens | IN PROGRESS | pre-plan, see below |
| UX-02 | Shared shell with role-aware navigation | IN PROGRESS | `76b782e` |
| UX-04 | Standardize loading/empty/error/success states | IN PROGRESS | `c00cc27`, `90fc671` |
| UX-05 | Branding, favicon, titles, copy, offline assets | IN PROGRESS | `b9c0e63` |
| UX-03, UX-06 | Page/table patterns; accessibility pass | NOT STARTED | — |
| FLOW-02 | Keep list filters, page and return location across actions | VERIFIED (alerts) | `395abe9` |
| FLOW-01, 03, 04, 05, 06 | Setup checklist, connection state, command feedback, interruption, collector states | NOT STARTED | — |
| CODE-01, 03, 05, 06, 07 | Controller/client/constant/CSS/diagnostics work | NOT STARTED | — |
| OPS-01…08 | Repository and delivery cleanup | NOT STARTED | — |
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

## In-progress items

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

### UX-01, UX-02, UX-04, UX-05 — partially delivered before the plan was adopted

Recorded for accuracy. None of these meet the plan's acceptance criteria yet.

| Item | Delivered | Not yet done |
| --- | --- | --- |
| UX-01 | Warm green palette as CSS custom properties in `site.css`; Bootstrap utilities rethemed to the palette; all 10 foreground/background pairs verified at WCAG AA 4.5:1 | `Client/MainForm` palette not reconciled with the web tokens; control variants not documented |
| UX-02 | Menu button collapses and restores the sidebar, persisted in `localStorage` (`76b782e`); dead layout removed (`6fcf499`) | Admin, Teacher and Student layouts remain three separate files; active-link correctness not audited per controller and action |
| UX-04 | Inline flash alerts replaced with auto-fading toasts (`c00cc27`, `90fc671`); shared `window.CamsToast` helper | Loading, empty, no-results, forbidden and offline states not standardized; repeat-submit guard not applied broadly |
| UX-05 | Fonts self-hosted as subsetted woff2 with CSP updated so they load offline (`b9c0e63`, closes DEF-001) | Titles, button labels, helper text and error copy not audited |

### LIVE-01 / LIVE-02 — Test suites and decision-branch coverage

- Server **320/320** and client **33/33** pass at `fb590c7`, and were run after each
  behavioral change in this batch.
- The plan asks for focused tests on the authorization, session expiry, reconnect and
  command acknowledgement branches. Session expiry now has direct coverage (CODE-09);
  reconnect and command acknowledgement do not.
- **Status:** IN PROGRESS.

## Blocked items

| ID | Item | Blocked on |
| --- | --- | --- |
| LIVE-03 | Operator walkthroughs of the five workflows | Representative Admin/Teacher/Student operators |
| LIVE-04 | Real capture, control, connection loss and recovery | A second disposable Windows client machine |
| LIVE-05 | Clean install, populated upgrade, uninstall/reinstall, restore | Snapshot-backed disposable VMs |
| LIVE-06 | Lab load, frame age, command latency, capacity | A disposable network, plus an agreed client count and numeric limits — none are approved |

These stay open by the plan's own rule: blocked live validation is recorded rather than
waived, and a polished interface alone does not establish release readiness.
