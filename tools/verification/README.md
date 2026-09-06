# Verification harnesses

Checks that drive a **running** CAMS server and report what it actually does.
They exist because the unit suite cannot answer some questions: whether an
unauthenticated socket is really refused, whether a teacher can open a page the
attribute says is theirs, whether a keyboard user can see where they are, whether
a dropped connection shows up anywhere.

These produced the evidence in `docs/testing/runs/`. Every figure quoted there
came from one of them.

> **Point these at a disposable server only.** They create accounts, start
> sessions, deactivate a teacher and put them back. Never run them against a
> classroom deployment or a database you care about.

## Setup

```powershell
# 1. Publish and start an isolated server on its own port with its own database.
dotnet publish "Monitoring And Remote Access\Server\Server.csproj" -c Release -o C:\temp\cams-test
cd C:\temp\cams-test
$env:Cams__HttpsPort = "5100"
$env:Cams__InitialAdminPassword = "<a password of at least 12 characters>"
$env:Cams__InitialAdminUsername = "testadmin"
.\Server.exe

# 2. In another shell, tell the harnesses where it is and who to sign in as.
$env:CAMS_TEST_URL = "https://localhost:5100"
$env:CAMS_TEST_ADMIN_USER = "testadmin"
$env:CAMS_TEST_ADMIN_PASSWORD = "<the same password>"
```

No credential is hard-coded. `config.js` reads them from the environment and
stops with a clear message if one is missing — a password committed to a
repository is a password on the internet, synthetic or not.

`node` and Chrome are required; `puppeteer-core` drives the browser checks.

## Order

```powershell
node enumerate-routes.js routes.json     # every controller action, read from source
node fixture.js R0906 fixture.json       # accounts, class and workstation, via the real forms
```

`fixture.json` holds the teacher and student passwords it generated and is
gitignored. The rest of the harnesses read it.

## What each one checks

| Harness | Question |
| --- | --- |
| `authz-anonymous.js` | Does any of the 153 actions answer a caller with no session? |
| `role-matrix.js` | Does every `AdminController` GET match its `[TeacherSharedAction]` attribute? |
| `scope-probe.js` | Can a teacher reach a class that is not theirs? |
| `inactive-teacher.js` | Does deactivating a teacher end access on the session already signed in? |
| `denied-check.js` | What does a refused request show someone who is already signed in? |
| `preserve-check.js` | Does a rejected form come back with what was typed, and without the password? |
| `submit-state.js` | Does a submitted form show it is working and refuse a second press? |
| `connection-status.js` | Does a dropped connection appear anywhere the user can see? |
| `ui-sweep.js` | Every page per role: HTTP status, console errors, accessibility. |
| `nav-check.js` | Is exactly one sidebar link marked, and is it the right one? |
| `focus-check.js` | Does every tab stop show a focus indicator? |
| `table-audit.js` | Is every table inside a responsive wrapper, at one density? |
| `csv-safety.js` | Are exports served as downloads, and refused to anonymous callers? |
| `udp-discovery.js` | Does the discovery broadcast reach the wire with a usable endpoint? |
| `stale-frame-check.js` | Does a screen that stopped updating say so? |
| `HubHarness/` | Real SignalR: refusal, identity, delivery isolation, reconnect, oversize payloads. |
| `cov.js` | Per-file line and branch coverage from a cobertura report. |
| `seed-rule.js`, `reactivate.js` | Seed a policy rule; put the fixture back afterwards. |
| `extracted-scripts-check.js` | Does the behaviour that moved out of Razor still work? |
| `browser-mode-labels-check.js` | Is a collector state ever shown as its enum name? |
| `css-audit.js` | Which class selectors does nothing reach? |
| `removed-css-check.js` | Does any page still carry a class whose rule was removed? |
| `state-separation-check.js` | Are transport, session and remote control shown apart? |
| `command-feedback-check.js` | Does a command report pending, done, refused and unknown distinctly? |
| `notification-noise-check.js` | Do repeated notices stack, and does the history survive them? |
| `setup-checklist-check.js` | Does a fresh install say what is left and where to do it? |

## The SignalR harness

```powershell
$env:CAMS_TEST_STUDENT_USER = "<from fixture.json>"
$env:CAMS_TEST_STUDENT_PASSWORD = "<from fixture.json>"
$env:CAMS_TEST_TEACHER_USER = "<from fixture.json>"
$env:CAMS_TEST_TEACHER_PASSWORD = "<from fixture.json>"
$env:CAMS_TEST_STUDENT2_USER = "<from fixture.json>"
$env:CAMS_TEST_STUDENT2_PASSWORD = "<from fixture.json>"

dotnet run --project HubHarness -c Release
```

It signs its agents out when it finishes. That matters: a student may hold one
workstation at a time, so a session left behind refuses the next run — correct
product behaviour that twice looked like a regression before the cause was
traced back here.

`dotnet run --project HubHarness -c Release stream-then-stop` connects, sends one
frame and goes quiet, which is what `stale-frame-check.js` watches for.

## Things worth knowing before you extend these

Written down because each one cost an hour.

- **Puppeteer pages in one browser share a cookie jar.** Signing in as a second
  role replaces the first. Give each role `browser.createBrowserContext()`.
- **The repeat-submit guard skips a submission that is already `preventDefault`ed.**
  A listener on the form runs before the document-level guard, so a test that
  cancels there sees the guard do nothing — correct behaviour, easily misread as
  a defect. Register on `document`.
- **`bin/Release` has no `wwwroot`.** Only `dotnet publish` copies it. Run the
  build output and every page reports a missing stylesheet.
- **The login ceiling is 10 POSTs per address per minute.** A harness that signs
  in repeatedly will trip it; space the sign-ins out.
- **Chrome asks for `/favicon.ico` when a response carries no `<link rel=icon>`,**
  which happens on JSON endpoints. Its console message does not name the URL.
- **A JSON endpoint is not a page.** Auditing one for landmarks and headings
  produces findings that are not real. Check the content type first.
- **Write these files with a file tool, never a shell heredoc.** Backslash
  escapes and template literals get mangled in transit. It has produced a
  word-boundary that was a backspace character (reporting all 236 C# types as
  dead), two spaces that arrived as null bytes (turning `site.js` into a file
  git treated as binary), and a syntactically broken harness. Each one looked
  like a finding until it was traced back here.
- **A check with no data to run against is not a pass.** `extracted-scripts-check`
  first reported six passes when three of them had nothing to exercise. Report
  SKIP separately, and fix the fixture.
- **Some `/Admin/` pages are meant to be reachable by a teacher.** They carry
  `[TeacherSharedAction]`; `role-matrix.js` is the oracle. Expecting a refusal
  produces confident false failures.
- **Do not hard-code a row id.** `scope-probe` asked about class 1, which on any
  database that had seen two fixture runs belongs to someone else — so a correct
  refusal was reported as a failure. Ids come from `fixture.json` now.
