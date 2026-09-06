# Defects — run 20260906-054337-8407e72

Commit under test: `8407e72`. Every entry below was reproduced against a running
server, not inferred from reading code. Nothing here was fixed during this run;
the run changed test code only.

Severity uses the plan's scale: **Critical** blocks release, **High** blocks a
core classroom workflow, **Medium** degrades a workflow with a workaround,
**Low** is cosmetic or latent.

---

## DEF-0906-001 — The login throttle counts page views, so five sign-ins a minute exhausts an address

- **Severity:** Medium
- **Case:** AUTH-05
- **Source:** `Monitoring And Remote Access/Server/Program.cs:180-218`
- **Environment:** Windows 11, .NET 8.0.422, isolated server on `https://localhost:5100`, published Release build

**Expected.** A per-address ceiling on `/Account/Login` exists to slow credential
guessing. Guessing happens on the POST. Loading the form should not consume the
same budget.

**Actual.** The middleware keys on `{RemoteIpAddress}:{Path}` and increments on
*every* request to the path, GET included. The limit is 10 per address per
minute. A full sign-in costs two requests — one GET for the form, one POST to
submit — so an address gets five successful sign-ins per minute and is then
refused.

**Reproduction.**

```
# Eleven plain page loads, no credentials submitted at any point:
for i in $(seq 1 13); do curl -sk -o /dev/null -w "%{http_code}\n" \
  https://localhost:5100/Account/Login; done
```

Observed: ten `200`s, then `429` for the remainder of the minute.

With correct credentials, `scratchpad/ratelimit-probe.js` recorded:

```
  sign-in 1: page 200, submit accepted (302 to the portal)
  ...
  sign-in 5: page 200, submit accepted (302 to the portal)
  sign-in 6: refused at the page load (429)
```

**Impact.** In the intended LAN deployment each workstation holds its own
address, so this is a per-workstation ceiling rather than a lab-wide one. It
still bites on a shared or kiosk workstation at a period changeover, on a
browser that reloads or prefetches the login form, and on any deployment that
places clients behind a NAT — there the ceiling becomes lab-wide and five
mistyped passwords from any student close the door for everyone at that address.

It also distorts testing: the `Status` column of `authz-anonymous.csv` shows
`429` on many rows because the sweep's own redirects to `/Account/Login`
exhausted the window. Those rows still passed, on the redirect target.

**Suggested follow-up.** Count only the POST, or track failures rather than
requests. Not applied — this run does not change production code.

---

## DEF-0906-002 — One CSV export stamps its filename in local time while the rest use UTC

- **Severity:** Low
- **Case:** TIME-01, REP-01
- **Source:** `Monitoring And Remote Access/Server/Controllers/AdminController.cs:1783`
- **Environment:** as above; server timezone Asia/Manila (UTC+8)

**Expected.** Exports downloaded together carry consistent timestamps.

**Actual.** `ExportUsageCsv` builds its filename from `DateTime.Now`; every other
export uses `DateTime.UtcNow`:

| Export | Line | Clock |
| --- | --- | --- |
| `ExportAttendanceCsv` | `AdminController.cs:1747` | `DateTime.UtcNow` |
| `ExportRemoteCommandsCsv` | `AdminController.cs:1761` | `DateTime.UtcNow` |
| `ExportUsageCsv` | `AdminController.cs:1783` | `DateTime.Now` |
| `ExportAlertsCsv` | `TeacherController.cs:758` | `DateTime.UtcNow` |

**Reproduction.** Request the three admin exports in one pass and read the
`content-disposition` headers. Observed in a single run seconds apart:

```
CAMS-UsageLog-20260906-1520.csv
CAMS-Attendance-20260906-0720.csv
CAMS-RemoteCommands-20260906-0720.csv
```

An eight-hour spread between files produced at the same moment.

**Impact.** Files saved together sort apart and appear to come from different
sessions. No data is wrong inside the files; only the filename misleads.

**Suggested follow-up.** Use one clock for all four. Not applied.

---

## OBS-0906-003 — The deactivate control relies on a failed model bind rather than an explicit value

- **Severity:** Low (latent; no incorrect behaviour observed)
- **Case:** CRUD-01
- **Source:** `Monitoring And Remote Access/Server/Views/Admin/Teachers.cshtml:142` and the matching rows in `Students.cshtml`

**What was found.** The hidden field is written as
`<input type="hidden" name="isActive" value="@(!isActive)" />`. When the account
is active, `!isActive` is `false`, and Razor omits an attribute whose value is
boolean `false`. The rendered markup is:

```html
<input type="hidden" name="isActive" />
```

The browser therefore posts `isActive=` (empty). Binding an empty string to the
non-nullable `bool isActive` parameter fails, and the parameter falls back to its
default of `false` — which happens to be the value the form intended.

**Why it is reported anyway.** Deactivation works today by coincidence, not by
instruction. Making the parameter `bool?`, adding a `ModelState.IsValid` guard,
or moving the action onto an `[ApiController]` would each silently break
deactivation while leaving activation working, because the activation direction
renders `value="True"` and binds normally.

**Verification that current behaviour is correct.** `scratchpad/inactive-teacher.js`
deactivated a teacher through the real control and confirmed the database row
moved to `Inactive`, so no user-visible defect exists at this commit.

**Suggested follow-up.** Render the value explicitly, e.g.
`value="@((!isActive).ToString())"`. Not applied.

---

## DEF-0906-004 — Five controls on the class roster have no accessible name, including a password field

- **Severity:** Medium
- **Case:** UI-02
- **Source:** `Monitoring And Remote Access/Server/Views/Admin/ClassDetails.cshtml`
- **Environment:** Chrome headless, isolated server on `https://localhost:5100`

**Expected.** Every form control carries a name a screen reader can announce,
whether by `label[for]`, `aria-label` or `aria-labelledby`.

**Actual.** `/Admin/ClassDetails/{id}` returns `200` and renders five controls
with no accessible name at all:

```
input[type=text][name=bulkFirstNames]
input[type=text][name=bulkLastNames]
input[type=text][name=bulkUserNames]
input[type=password][name=bulkPasswords]
select[name=studentId]#existingStudentSelect
```

**Reproduction.** Sign in as an administrator, open `/Admin/ClassDetails/1`, and
query each control's accessible name. `ui-sweep.csv` records five findings on
that page and zero on the sibling `/Teacher/ClassDetails/1`.

**Impact.** This is the bulk add-students form. Someone using a screen reader is
given four adjacent text boxes and a password box with nothing to distinguish
them, on a form that creates accounts. An earlier accessibility pass connected
labels across 28 pages; `ClassDetails` was not one of them, which is why this
survived.

**Suggested follow-up.** Connect each control with `label[for]`/`id`, as the
other admin views already do. Not applied — this run changes test code only.

---

## OBS-0906-005 — Smaller accessibility gaps on three pages

- **Severity:** Low
- **Case:** UI-02
- **Evidence:** `ui-sweep.csv`

| Page | Gap |
| --- | --- |
| `/Admin/ComputerHistory/{id}` | no `main` landmark |
| `/Teacher/StudentDetails/{id}` | one table renders without header cells |
| the shared 404 page | two inline images without `alt`, and no `main` landmark |

Not applied.

**Audit false positives, recorded so they are not re-raised.** The sweep also
flagged "no main landmark" and "no page heading" on `/Teacher/ActivityTimeline`,
`/Teacher/LiveState`, `/Teacher/GlobalSessionState`, `/Teacher/OpenAlertCount`
and `/Student/_SessionStatusJson`. All five return `Json`, not a page, so those
findings are artefacts of auditing a JSON response as though it were markup.

---

## Considered and dismissed

These were flagged during the run and traced to the harness or to intended
behaviour. They are recorded so a later run does not re-raise them.

| Observation | Finding |
| --- | --- |
| `GET /Account/Logout` answered an anonymous caller with `200` | It redirects to `/`, which renders the login view. Correct; the sweep's classifier only looked for a `/Account/Login` URL. |
| `POST /api/client/login` answered `415` | The JSON API refused a form-encoded body before reaching the handler. Correct. With `application/json` it returns `400` for blank or oversized fields and `401` for bad credentials. |
| Teacher reached `/Admin/Index`, `/Admin/Students`, `/Admin/Teachers` | Intended. 56 `AdminController` actions carry `[TeacherSharedAction]`; all 23 GET actions were checked against that attribute and every one matched. |
| Deactivating the fixture teacher did nothing | The teacher owned an active class, and the server correctly refuses until classes are reassigned or archived (`AdminController.cs:228`). |
| `onerror=` present in the roster response | It appears inside the HTML-escaped text `&lt;img src=x onerror=…`, which is inert. The browser check recorded no execution. |
| Every page reported a stylesheet failure | The first sweep ran `bin/Release` output, which has no `wwwroot`; that directory is populated by `dotnet publish`. Re-run against a publish, all assets served `200`. |
| Only one deactivate control appeared on `/Admin/Teachers` | Two Puppeteer pages shared one cookie jar, so the "admin" page had been re-authenticated as the teacher, who sees "This is you" on their own row. Fixed with an isolated browser context per role. |
| Student dashboard showed another teacher's rule | `RestrictionRule.IsGlobal` defaults to `true`; the fixture set `TeacherId` without clearing it. Test fixture corrected. |

---

## DEF-0906-006 — Remote control aimed clicks at the wrong monitor

- **Severity:** High on any student machine with more than one display; no effect on a single display
- **Case:** WIN-02, HUB-02
- **Source:** `Client/Services/ScreenCaptureService.cs:9` and `Client/InputSimulator.cs:32`
- **Found by:** reading the two ends together, not by a test
- **Status:** **Fixed**, commit below

**Expected.** A teacher clicking a point on the streamed image moves the student's
cursor to that same point.

**Actual.** The two ends measured different rectangles:

| | Rectangle used |
| --- | --- |
| Capture | `Screen.PrimaryScreen.Bounds` — the primary monitor only |
| Input | `SystemInformation.VirtualScreen` — every monitor |

On one display these are the same rectangle, so it worked and the mismatch was
invisible. On two 1920-wide displays the teacher saw 1920 pixels while their
clicks were spread across 3840:

| Teacher clicks | Cursor lands at |
| --- | --- |
| Centre of the image | x=1920 — the first pixel of a monitor they cannot see |
| Right edge of the image | x=3839 — the far edge of that second monitor |

Roughly half of every click landed on a display absent from the teacher's view,
and the error grew with distance from the left edge. A second monitor was also
never streamed at all, so a student could keep anything on it unwatched.

If the primary monitor is not the leftmost, `VirtualScreen.Left` is negative and
the offset is wrong in the other direction.

**Why no test caught it.** `InputSimulatorTests` checks `ScaleCoordinate` in
isolation. The arithmetic was never wrong — the choice of rectangle was.

**Fix.** Both ends now take their rectangle from one place,
`Client/Services/CaptureGeometry.cs`. The capture takes the whole virtual
desktop, with the desktop origin as the source point so a negative origin is
handled, which also removes the unwatched-monitor blind spot. `CaptureGeometryTests`
adds seven cases including a negative-origin two-monitor layout, which a
single-display test machine would otherwise never exercise.

The lasting guard is structural rather than a test: there is now a single
definition of "the screen", so a future divergence would mean deliberately
introducing a second one.

**Observed while reading the same code, and since fixed:**

- **A screen that stopped updating looked exactly like one that was live.** The
  capture loop skips frames while the workstation is locked — correctly, because
  `SetLocked` calls `LockWorkStation` and Windows then shows the secure desktop,
  which `CopyFromScreen` cannot see. The same silence follows a UAC prompt or a
  capture error. The teacher was left looking at the last frame with nothing to
  say it was stale. The monitoring page now marks a card whose frames have
  stopped for more than five seconds, greys the image, and says how long ago the
  last frame arrived. It does not guess **why**: the agent cannot report from the
  secure desktop either, so inventing a reason would be worse than none.
- **Capture errors were swallowed by a bare `catch`.** A single dropped frame is
  normal; a run of them meant the stream had stopped with nobody told. Twenty
  consecutive failures now put "Screen sharing interrupted" on the status line of
  the agent, and it clears itself when frames resume.
- **The frame interval did not mean what it said.** Capture, JPEG encode, base64
  and the send were serial and *then* the loop waited 50 ms, so the real interval
  was the cost of a frame plus the delay. The loop now measures the work and
  waits only the remainder.

Verified live: an agent streamed one frame and went quiet, and the teacher's page
reported "Screen not updating - last frame 5s ago" within four seconds. 5 of 5.
