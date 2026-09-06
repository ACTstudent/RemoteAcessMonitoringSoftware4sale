# Addendum, 2026-09-06 — the run's defects have been fixed and re-verified

The run report beside this file records the codebase as it stood at `8407e72`
and is left as written. Everything it raised has since been fixed and checked
again against a running server. This addendum records what changed, what the
re-verification showed, and two places where the original report was wrong.

## Status of each finding

| ID | Severity | Status |
| --- | --- | --- |
| DEF-0906-001 | Medium | **Fixed** — the login ceiling now counts submissions only |
| DEF-0906-002 | Low | **Fixed** — all four exports stamp their filename in UTC |
| DEF-0906-004 | Medium | **Fixed** — every control on both class pages has an accessible name |
| OBS-0906-003 | Low, latent | **Fixed** — the deactivate field renders an explicit value |
| OBS-0906-005 | Low | **Partly fixed, partly withdrawn** — see below |

## DEF-0906-001 — the login ceiling

`Program.cs` metered every request to `/Account/Login`. It now meters only the
POST, because that is where a guess happens:

```csharp
var isLoginAttempt = context.Request.Path.StartsWithSegments("/Account/Login") &&
    HttpMethods.IsPost(context.Request.Method);
```

The window itself moved out of the pipeline into `Server/Services/RequestThrottle.cs`.
It had been unreachable from a test — which is how the defect survived — and it
now takes an injected clock, so a test can cross a window boundary without
waiting a minute. Eleven cases in `RequestThrottleTests` pin the window
rollover, per-key isolation, the sweep, the key ceiling and concurrent counting.

Measured against the running server, on a clean window:

| | Before | After |
| --- | --- | --- |
| Plain loads of the login form before a refusal | 10, then `429` | 20 of 20 served; no ceiling |
| Correct sign-ins accepted in one minute | 5 of 8 | **8 of 8** |
| Wrong passwords before a refusal | 10 | **10** (unchanged) |

The protection is intact — the eleventh guess is still refused — and ordinary
use no longer spends the budget.

## DEF-0906-002 — export filename clock

`AdminController.ExportUsageCsv` used `DateTime.Now`; it now uses
`DateTime.UtcNow` like the other three. `ExportFilenameTests` asserts each
export's stamp against UTC and that four exports requested together agree to
within a minute.

The test was confirmed to fail without the fix, rather than being assumed to
work: reverting the one word produced

```
exports disagreed by 08:00:00: 20260906-1626, 20260906-0826, 20260906-0826, 20260906-0826
```

Note this pair of assertions can only tell the two clocks apart on a machine
that is not itself on UTC.

## DEF-0906-004 — unnamed controls

Both class pages now name every control. The original report named only
`/Admin/ClassDetails`; the sweep had missed `/Teacher/ClassDetails` because the
fixture teacher did not own the class at that point, so the page redirected
before it could be audited. Once the class was assigned, the teacher view turned
out to have **more** unnamed controls than the admin one — the earlier
accessibility pass had covered the admin twin and not this one.

| View | Controls given a name |
| --- | --- |
| `Admin/ClassDetails.cshtml` | 4 bulk fields, plus `for` on the registered-student select |
| `Teacher/ClassDetails.cshtml` | 4 bulk fields, 4 single add-student fields, the roster file input, the search box, plus `for` on the select |

The bulk rows are cloned with `cloneNode(true)` when "Add more rows" is used, so
those four use `aria-label` rather than an `id` that would be duplicated on
every added row.

## OBS-0906-003 — the deactivate field

`<input type="hidden" name="isActive" value="@(!isActive)" />` rendered with no
`value` at all when the expression was `false`, because Razor drops an attribute
whose value is boolean `false`. Deactivation worked only because a failed model
bind falls back to `false`. All three occurrences — `Teachers.cshtml`,
`Students.cshtml`, `Settings.cshtml` — now render `@((!isActive).ToString())`.

Re-checked by driving the real controls: an inactive student and an inactive
teacher were both reactivated through the page, and the database rows moved.

## OBS-0906-005 — partly fixed, partly withdrawn

**Fixed, and larger than reported.** `/Admin/ComputerHistory` was recorded as
missing a `main` landmark. The cause was worse than that: there is no
`_ViewStart.cshtml` in this project, so a view that names no layout renders with
none, and `ComputerHistory.cshtml` was the only view in the entire application
that named none. The page was coming back with no navigation and no styling at
all, not merely without a landmark. It now sets `Layout = "_Layout"` like every
other admin view. No other view has the problem.

**Fixed.** The alerts table on `/Teacher/StudentDetails` had no header cells; it
now has a `thead`, and its empty-state row spans the right number of columns.

**Withdrawn.** "Two images without `alt` on the 404 page" was wrong. All five
`<img>` tags in the application already carry `alt`, and no view emits a
`data:` image. Those two were part of Chrome's own built-in error page, rendered
because the URL 404'd — not CAMS markup. The same applies to the "no main
landmark" and "no page heading" findings on the three URLs that 404'd.

## Harness corrections

Three faults in the sweep were producing findings that were never real. They are
fixed so the evidence beside this file is trustworthy:

- JSON endpoints were audited as though they were pages, so `ActivityTimeline`,
  `LiveState`, `GlobalSessionState`, `OpenAlertCount` and `_SessionStatusJson`
  each reported a missing landmark and heading. The sweep now audits only
  `text/html` responses.
- `/Admin/Deployment` was probed at `/Admin/Deployment/Index`, which does not
  exist — that controller routes from an attribute. `ClassAnalytics` was probed
  without the id it requires.
- A 404 on an action that takes an id was classified as a failure rather than as
  a case the fixture cannot reach.

## Re-verification

All against the isolated published server on `https://localhost:5100`, after the
fixes:

| Check | Result | Evidence |
| --- | --- | --- |
| Server.Tests | 442 passed, 0 failed, 0 skipped (was 426) | 16 new tests |
| Client.Tests | 33 passed, 0 failed, 0 skipped | unchanged |
| Solution build | 0 warnings, 0 errors | |
| Anonymous over all 153 actions | 153 of 153 refused | `authz-anonymous-after-fixes.csv` |
| Teacher against `[TeacherSharedAction]` | 23 of 23 matched | `authz-teacher-matrix-after-fixes.csv` |
| Page sweep | 44 pass, 0 fail, 15 not applicable | `ui-sweep-after-fixes.csv` |
| Accessibility | **0 problems across every page visited** (was 9) | same file |
| Stored script payload | still not executed | same file |
| Login throttle | 10 guesses then refused; 8 of 8 correct sign-ins accepted | above |

The 15 not-applicable rows are 14 download endpoints, which are covered
separately, and `/Teacher/AlertHistory/{id}`, for which the fixture holds no
alert.

## What this addendum does not change

The release decision stands. Twelve cases remain BLOCKED on hardware — two
Windows clients, snapshot VMs, a disposable network — and nothing here touches
them. No agent connected, no screen was delivered, no installer ran. See
`NEXT-STEPS.md`, whose defect section is now closed but whose hardware section
is unchanged.
