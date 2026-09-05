# Decision record

Decisions taken while implementing [the professionalization plan](PROFESSIONALIZATION-PLAN.md),
with the reasoning and the alternative that was rejected. Newest first. Each entry
names the work item it belongs to in [PROGRESS.md](PROGRESS.md).

---

## D-008 — Alert filter state lives in the URL, not the session

**Item:** FLOW-02. **Date:** 2026-09-05.

Something had to carry the alert list's filter across a POST and its redirect.

**Decision:** keep it in query parameters, posted through a hidden partial and
written back into the redirect's route values.

**Rejected:** remembering the last filter per teacher in session state. It would
have made the filter invisible, made two tabs fight each other, and made a
filtered list impossible to share or bookmark — and the plan asks for shareable
state and warns against persisting filter input.

**Consequence:** every form that posts an alert action must render
`_AlertFilterFields`. A new action that forgets it silently loses the filter, so
the tests assert the round trip for each action rather than only for one.

---

## D-007 — Acknowledging no longer widens the list to acknowledged alerts

**Item:** FLOW-02. **Date:** 2026-09-05.

The old redirect pinned `includeAcknowledged=true`, presumably so the teacher
could see the alert they had just handled. The cost was that a list narrowed to
open alerts silently widened after every action.

**Decision:** keep the teacher's filter exactly as it was, and confirm the
outcome with a message instead. In an open-only list the row disappears, which is
what a work queue should do, and the message says what happened so the
disappearance does not read as a failure.

**Rejected:** keeping the forced widening. It overrode a filter the teacher had
set deliberately, which is the defect FLOW-02 exists to fix.

---

## D-006 — Remove the nullable suppression outright rather than narrow it

**Item:** CODE-08. **Date:** 2026-09-05.

The plan says to "remove suppressions by feature". The inventory showed the
suppression was doing exactly one job: hiding 20 CS8602 warnings in a single file.
Narrowing it to that file would have left a permanent licence to dereference
`Context.User` unchecked, which is the actual defect.

**Decision:** delete the `NoWarn` line from `Server/Server.csproj` entirely and fix
the 17 dereferences behind a checked `Principal` property.

**Rejected:** scoping the suppression to `RemoteMonitoringHub.cs`, and blanket
null-forgiving (`!`) operators — the plan rules the latter out, and it would convert
a compile-time warning into a runtime `NullReferenceException`.

**Cost:** any future nullable warning anywhere in the server now breaks the clean
build. That is the intent.

---

## D-005 — `HubException` for an unauthenticated hub connection

**Item:** CODE-08. **Date:** 2026-09-05.

`Context.User` is nullable because a SignalR connection need not be authenticated.
Something must happen when it is null.

**Decision:** throw `HubException`, which SignalR delivers to the caller as a clean
authorization failure and which does not leak server internals to the client.

**Rejected:** returning early with a default identity — that would silently treat an
unauthenticated caller as a valid one, weakening a role boundary the plan requires
to stay intact.

---

## D-004 — Delete the dead teacher layout instead of merging it

**Item:** OPS-09, UX-02. **Date:** 2026-09-05.

`Server/Views/Teacher/_Layout.cshtml` looked like a shell worth folding into the
UX-02 shared layout. Reference checks showed nothing selected it: no view sets
`Layout = "_Layout"`, no path reference exists, and there is no `_ViewStart.cshtml`
that could pick it implicitly.

**Decision:** delete it, with the reference check recorded in the commit message.

**Rejected:** keeping it as a starting point for the shared shell. Merging a file no
running code has ever rendered would carry untested markup into a live layout.

**Reversal condition:** if the shared shell needs it, `6fcf499^` still has it.

---

## D-003 — Extract page scripts to static files, not to a bundler

**Item:** CODE-04. **Date:** 2026-09-05.

The monitoring view carried 385 lines of inline JavaScript.

**Decision:** move it to `wwwroot/js/teacher-monitoring.js` as a plain script,
versioned with `asp-append-version`.

**Rejected:** introducing npm, a bundler or a module pipeline. The plan says to avoid
adding frontend package tooling solely to organize a handful of local scripts, and the
product must build offline on a school network.

**Precondition that made this safe:** the block used no Razor interpolation, so
nothing needed encoding and no escaping behavior changed. Any view whose script does
interpolate server data must instead pass that data through an encoded configuration
element — it cannot simply be moved.

---

## D-002 — One scope implementation, reached through a view component

**Item:** CODE-02. **Date:** 2026-09-05.

The sidebar alert badge and the alerts page had drifted apart because the layout kept
its own copy of the student-scope predicate.

**Decision:** put the scope in `IAnalyticsService.GetOpenAlertGroupCountAsync` and
have the layout reach it through a view component, so the layout injects no database
context at all.

**Rejected:** setting the count in a base controller or a filter. That would have
required every teacher action to remember to populate it, which is the same
drift risk in a new place.

---

## D-001 — Global student access is a product decision, not a bug

**Item:** context for CODE-02. **Date:** before the plan was adopted.

Every teacher can monitor and manage every student, regardless of class assignment,
including roster CRUD. This was chosen explicitly ("fully flat — everything global")
and is implemented as `AccessibleStudents(teacherId) => _context.Students`.

**Consequence for this plan:** a test or review finding that a teacher can see a
student outside their class is **expected behavior**, not a defect. Role boundaries
that must still hold are Admin vs Teacher vs Student, not teacher-to-teacher.

---

## Standing constraints

These are not decisions to revisit; they were set by the repository owner and bound
all work under this plan.

- **Do not publish a release or alter real deployments** as part of implementing the plan.
- **Never use a production or live database as a test fixture.**
- **Do not send remote OS commands** (lock, logout, restart, shutdown) to the owner's
  working machine during testing.
- **Do not weaken a security check or rewrite an expectation to turn a test green.**
- The home directory `C:\Users\Jlard` is a separate git repository wired to a public
  remote and contains untracked personal data. Nothing from it is to be committed or
  pushed. It is unrelated to this repository.
