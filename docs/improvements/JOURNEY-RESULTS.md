# Journey results

Phase 0 of [the professionalization plan](PROFESSIONALIZATION-PLAN.md) asks for five
workflows to be observed — install/connect, create class and roster, start lesson,
handle alert, recover connection — with steps, backtracking, confusing labels and
completion time recorded, so later claims of improvement can be checked against
something rather than asserted.

**Read this file with its limitation in mind.** What follows is an implementation
agent driving the interface, not an operator using it. That gives a reliable count of
the interactions a journey requires and a reliable record of where a journey breaks,
and it gives nothing at all about hesitation, misreading or time — the things the
plan actually wants from this phase. Every timing and comprehension question below is
therefore open, and LIVE-03 in [PROGRESS.md](PROGRESS.md) stays BLOCKED until real
Admin, Teacher and Student operators walk these through.

**Environment:** Windows 11, Chrome headless (Puppeteer), 1440×900, local Debug build
on `https://localhost:5000`, seeded development database. Revision at time of
observation: `395abe9`.

---

## J1 — Install and connect a client

**Status: BLOCKED.** Needs a second disposable Windows machine and a snapshot-backed
VM. Recorded at LIVE-04 and LIVE-05.

Partial evidence exists: the installer was built and installed once on the
development machine, recorded in
`docs/testing/runs/20260905-125626-b5441f1/ADDENDUM-install-and-DEF-001.md`. That is
one install on one already-configured machine, so it is not the clean-install,
upgrade, uninstall and restore rehearsal the plan asks for, and it says nothing about
what a school technician meets on a fresh machine.

This is the journey FLOW-01 (setup checklist and guided connection states) is meant
to improve, so its baseline is the one most worth having and the one hardest to get.

---

## J2 — Create a class and its roster

**Observed on the Teacher role.** Minimum interactions on the happy path, counting a
click, a filled field group and a submit as one step each.

| Path | Steps | Notes |
| --- | --- | --- |
| Create one class | Sidebar → Classes → Create → name → submit | The primary action is where the pattern in other lists puts it |
| Add students one at a time | Class Details → Add student → 5 fields → submit, repeated per student | Linear in roster size; a 40-student class is 40 passes |
| Add students in bulk | Class Details → Add bulk students → "Add more rows" per extra row → submit once | The row-per-student form removed the repeat, and is the reason that control exists |

**Backtracking observed:** none on the happy path.

**Open for operator observation:** whether "Class Details" reads as the place a roster
lives, and whether a teacher finds the bulk control before starting to type the
roster one student at a time. An agent that already knows the control exists cannot
answer either.

---

## J3 — Start a lesson

**Observed on the Teacher role.**

- A session starts from either the Sessions page or the global session control, and
  the two are separate paths to the same outcome. Which one a teacher reaches for is
  an operator question.
- Elapsed time, pause and resume were wrong before this work: a session on UTC+8 was
  measured roughly 480 minutes old within a minute of starting and expired
  immediately. Recorded as CODE-09 in [PROGRESS.md](PROGRESS.md), fixed and covered
  by tests. **This is the clearest evidence in the file that a baseline was worth
  capturing** — the journey did not merely feel awkward, it did not work.

---

## J4 — Handle an alert

**Observed on the Teacher role, before and after the FLOW-02 change.**

Before, working down a filtered queue — say critical open alerts for one station:

| # | Step | Result |
| --- | --- | --- |
| 1 | Filter to Critical + station | Correct list |
| 2 | Acknowledge the first group | **Returns to an unfiltered list including already-handled alerts** |
| 3 | Rebuild the filter | Back to step 1 |

So handling *n* alerts cost roughly *3n* steps instead of *n*, and the rebuild was
pure backtracking — the single worst ratio measured anywhere in this file. Two
related failures sat next to it: "All statuses" in the filter form did nothing, and
the CSV export ignored the filter's status and exported every status regardless of
what was on screen.

After, the same journey is one step per alert: the list returns exactly as it was,
with a message naming what happened. Verified in a headless browser (9/9 checks) and
covered by 25 tests.

**Open for operator observation:** whether the acknowledged row disappearing from an
open-only list reads as success or as the row having been lost. The confirmation
message exists to answer that, and only an operator can say whether it does.

---

## J5 — Recover a connection

**Status: BLOCKED.** Needs a second client machine and a disposable network to
interrupt. Recorded at LIVE-04 and LIVE-06.

What can be stated without that environment: the interface currently does not
separate transport connection, session lifecycle and remote-control state, which is
FLOW-03's premise. Until the outage can actually be staged, whether a stale frame can
be shown as current is **untested, not disproved**.

---

## Summary

| Journey | Baseline captured | Improvement measured | Blocking need |
| --- | --- | --- | --- |
| J1 Install and connect | Partial (one install, one machine) | No | Second machine, snapshot VM |
| J2 Class and roster | Yes (step counts) | Bulk entry removes the per-student repeat | Operator comprehension |
| J3 Start a lesson | Yes | Session timing corrected (CODE-09) | Operator preference between two paths |
| J4 Handle an alert | Yes (step counts, before and after) | ~3n steps → n steps | Operator reading of the empty row |
| J5 Recover a connection | No | No | Second machine, disposable network |

Three of the five journeys have a usable baseline. Two cannot be observed on one
machine at all, and both of those are the ones the plan names as release gates.
