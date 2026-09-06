# Run summary — 20260906-054337-8407e72

> **2026-09-06:** every defect below has since been fixed and re-verified, and two
> findings were withdrawn as harness faults. This report is left as written; see
> [ADDENDUM-defect-fixes.md](ADDENDUM-defect-fixes.md) for what changed and the
> re-verification results. The release decision is unchanged — the blocked
> hardware cases still block acceptance.

**Outcome: not ready for release acceptance.** Every automated test passes and
the authorization surface came back clean, but 12 required cases remain BLOCKED
on hardware and three defects are open — two Medium, one Low — alongside two
low-severity observations. A blocked required case blocks acceptance however
many automated tests pass.

| | |
| --- | --- |
| Commit | `8407e72` |
| Started / finished (UTC) | 2026-09-06 05:43 / 07:25 |
| Working tree | clean at start; only four new test files added during the run |
| Platform | Windows 11 26200, x64, .NET SDK 8.0.422, Node v24.18.0, Chrome (system), Inno Setup 6 present |
| Snapshot under test | `git archive HEAD` → 290 tracked files, hashed in `snapshot-manifest.sha256` |
| Live server | published Release build on `https://localhost:5100`, its own SQLite database and generated certificate |
| Production code changed | none |

## Totals

| Status | Count |
| --- | --- |
| PASS | 65 |
| FAIL | 5 (4 distinct defects) |
| BLOCKED | 12 |
| NOT RUN | 8 |
| NOT ASSESSED | 1 |
| **Total case variants** | **91** |

Automated suites, Release, from the isolated snapshot:

| Suite | Result |
| --- | --- |
| Server.Tests | 426 passed, 0 failed, 0 skipped |
| Client.Tests | 33 passed, 0 failed, 0 skipped |
| Solution build | 0 warnings, 0 errors, 6 projects |
| CamsDbCleaner | 0 warnings, 0 errors |

## What this run establishes

**The authorization surface holds.** Three independent passes, all against a
running server rather than mocks:

- **Anonymous, every action.** All 153 controller actions at this commit were
  enumerated from source and called with no session. **153 of 153 refused** —
  148 redirected to login, 1 returned `401`, 1 refused the media type before the
  handler, 2 are not routed at the probed path. No action returned portal
  content. Evidence: `authz-anonymous.csv`.
- **Teacher against the real oracle.** `AdminController` is shared: an active
  teacher may use the 56 actions marked `[TeacherSharedAction]` and nothing
  else. Reading that attribute out of the source and testing every GET action
  against it, **23 of 23 matched** — 11 shared actions reachable, 12 admin-only
  refused. Evidence: `authz-teacher-matrix.csv`.
- **Deactivation takes effect immediately.** A teacher was deactivated through
  the real control while signed in. The live session lost both the shared admin
  pages and the teacher portal on the next request, and the account could not
  sign in again. No retained access.

**Three controllers had no coverage at all, and now do.** `TEST-PLAN.md` flagged
that no test files were found for `StudentController`, `ClientAuthController` or
`MonitoringController`, and asked whether indirect coverage existed. Measured
answer: it did not — all three were at **0.0%**. 81 tests were added, taking
them to **96.0%, 100.0% and 100.0%**, and overall server line coverage from
69.3% to 71.3% (branches 51.3% → 53.6%).

The most valuable of these is the student agent's login endpoint, which is the
outermost door into the server for an unauthenticated machine on the lab
network. It now has tests proving a **teacher or admin credential cannot bring
up a student agent**, that field limits reject before the auth service is
reached, and that the attempt ceiling is per address. The live server returned
exactly what the unit tests predict — `400` blank, `400` oversized, `401` bad
credentials, `415` wrong media type — so the two layers corroborate each other.

**Pages render cleanly, with four accessibility gaps.** 45 pages were visited
across admin, teacher and student; 42 passed and **34 were entirely clean**. No
duplicate ids, no dangling `label[for]`, and no link or button without text
anywhere. The three non-passes are explained: two URLs the probe built wrongly,
and one alert id with no fixture row behind it.

Four genuine accessibility findings remain, on pages the earlier 28-page label
pass did not reach:

- `/Admin/ClassDetails` — **five controls with no accessible name**, including a
  password field, on the bulk add-students form (DEF-0906-004)
- `/Admin/ComputerHistory` — no `main` landmark
- `/Teacher/StudentDetails` — a table with no header cells
- the shared 404 page — two images without `alt`

A further five findings were audit artefacts: `ActivityTimeline`, `LiveState`,
`GlobalSessionState`, `OpenAlertCount` and `_SessionStatusJson` all return JSON,
so "no landmark" and "no heading" do not apply to them.

**Security posture checks out.** Antiforgery rejects a token-less login POST with
correct credentials (`400`). Auth and session cookies are `Secure`, `HttpOnly`,
`SameSite=Lax`. All five security headers including a Content-Security-Policy are
applied. A student name stored as `<img src=x onerror=…>` is stored raw and
rendered HTML-escaped; the browser recorded no execution. No administrator exists
unless a 12-character password is configured — there is no default account.
Migrations applied cleanly to a new file-backed SQLite database, and the
generated certificate wrote only its public `.cer`.

## Open defects

| ID | Severity | Summary |
| --- | --- | --- |
| DEF-0906-001 | Medium | The login throttle counts page views, not just submissions. Ten requests per address per minute, and a sign-in costs two — so an address gets **five correct sign-ins a minute**, then `429`. Eleven plain page loads with no credentials submitted is enough to lock it. |
| DEF-0906-002 | Low | `ExportUsageCsv` stamps its filename with `DateTime.Now` while the other three exports use `DateTime.UtcNow`; files produced seconds apart were eight hours apart. |
| DEF-0906-004 | Medium | `/Admin/ClassDetails` renders **five controls with no accessible name**, including a password field, on the bulk add-students form. |
| OBS-0906-003 | Low, latent | The deactivate form renders `<input name="isActive">` with **no value** — Razor drops an attribute whose value is boolean `false`. It works only because a failed bind falls back to `false`. No incorrect behaviour observed. |
| OBS-0906-005 | Low | Missing `main` landmark on `/Admin/ComputerHistory`, a table without header cells on `/Teacher/StudentDetails`, and two images without `alt` on the 404 page. |

Seven further observations were traced to the harness or to intended behaviour
and dismissed with reasons, in `DEFECTS.md`. Two are worth carrying forward as
lessons: the first UI sweep reported a stylesheet failure on every page because
it ran `bin/Release`, which has no `wwwroot` — that directory is populated by
`dotnet publish`; and a deactivation appeared to fail silently because two
Puppeteer pages shared one cookie jar, so the "admin" page had quietly been
re-authenticated as the teacher.

## Coverage

69.3% → 71.3% of lines across 89 non-view, non-migration C# files. The figure
excludes Razor views (0% from unit tests by nature — 42 of them were instead
rendered live) and migrations.

Largest remaining gaps, all with a stated reason:

| File | Lines | Coverage | Why |
| --- | --- | --- | --- |
| `Program.cs` | 235 | 0% | Startup and middleware; exercised live, never unit tested |
| `ServerDiscoveryService.cs` | 101 | 0% | UDP discovery; BLOCKED on a disposable network |
| `PolicyChangeBroadcastInterceptor.cs` | 26 | 0% | Needs a connected client |
| `RemoteMonitoringHub.cs` | 651 | 56% | Mocked only; real transport BLOCKED |
| `AdminController.cs` | 1072 | 60% | Role split fully matrix-tested; many action bodies are not |
| `TeacherController.cs` | 847 | 59% | Alert filtering covered in depth; monitoring and remote actions are not |

## What this run does not establish

Nothing here speaks to the classroom. No Windows agent connected, no screen was
delivered, no remote command was sent, no installer ran, no capacity was
measured. Hub coverage remains mocked, which cannot establish real SignalR
transport, reconnect or delivery isolation. See `NEXT-STEPS.md` for the exact
prerequisite behind each of the 12 blocked cases.

Packaging was deliberately not run: `build-everything.ps1` rebuilds the tracked
installers, and the brief forbids publishing a release.

## Release decision

**Do not accept.** Required cases SES-01, HUB-01/02/03, WIN-01/02, POL-01,
TEL-01, NET-01, INS-01/02 and the client half of TLS-01 are BLOCKED and none can
be closed by reading source. DEF-0906-001 should be decided before a classroom
deployment where several people sign in at one workstation in quick succession,
and DEF-0906-004 before any deployment with an accessibility obligation.

## Evidence in this directory

| File | Contents |
| --- | --- |
| `CASE-RESULTS.csv` | 90 case variants with expected, actual, status and evidence |
| `SOURCE-COVERAGE.csv` | Source areas mapped to cases, tests and review status |
| `DEFECTS.md` | Two defects and one latent observation, with reproductions; plus seven dismissed with reasons |
| `NEXT-STEPS.md` | Exact prerequisites and commands for everything unfinished |
| `authz-anonymous.csv` | All 153 actions called anonymously |
| `authz-teacher-matrix.csv` | 23 admin GET actions against the `[TeacherSharedAction]` oracle |
| `ui-sweep.csv` | 42 pages with status, console errors, failed requests, accessibility findings |
| `coverage-before.csv` / `coverage-after.csv` | Per-file line and branch coverage either side of the added tests |

Raw logs, TRX and Cobertura XML are outside the repository at
`…/scratchpad/testrun-20260906-054337-8407e72/`, since they contain absolute
paths and full console output. The isolated snapshot and its database are at
`…/scratchpad/snap-8407e72/`.

## Cleanup

The isolated server was stopped and no process remains on port 5100. The
operator's own repository was not modified beyond four added test files; no
production code, no database and no deployment was touched, and no remote OS
command was issued to any machine.
