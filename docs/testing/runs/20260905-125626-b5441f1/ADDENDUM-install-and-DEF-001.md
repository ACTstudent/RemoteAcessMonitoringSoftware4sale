# Addendum — installation performed and DEF-001 closed

Recorded after the original run. Two things changed since `SUMMARY.md` was
written: the defect was fixed, and the server installer was actually run.

## DEF-001 fixed and verified on the installed product

The fix is commit `b9c0e63`, released in v2.11.6. The two font families are now
served by the application from `wwwroot/fonts` rather than imported from
`fonts.googleapis.com`.

Evidence, in order:

1. Re-running the sweep that found the defect took it from **34 of 34 pages
   reporting the violation to none**. See `ui-sweep-after-DEF-001-fix.csv`.
2. On the **installed** product, `document.fonts` reports Plus Jakarta Sans
   loaded at weights 400, 500, 600, 700 and 800, and the computed body font is
   `"Plus Jakarta Sans", Inter, ...`. Before the fix no face loaded at all and
   the browser fell through to the local stack.

Only latin and latin-ext subsets ship. Because both families are variable fonts
the same file serves every weight, so eighteen faces resolve to four files
totalling 188 KB.

| Case | Was | Now |
| --- | --- | --- |
| UI-01 admin views | FAIL | PASS |
| UI-02 teacher views | FAIL | PASS |
| UI-03 student views | FAIL | PASS |

## INS-01 partially closed

`CAMS-Server-Setup.exe` v2.11.6 was installed on the working machine and the
result inspected. This is **not** the disposable VM the plan asks for, so it
closes clean install only. Upgrade and uninstall remain blocked, because
rehearsing them safely needs a snapshot to roll back to.

| Case | Status | Note |
| --- | --- | --- |
| INS-01 clean install | PASS (with caveat) | Installed on the working machine, not a disposable VM |
| INS-02 upgrade over populated data | BLOCKED | Needs a VM snapshot to roll back |
| INS-03 uninstall and reinstall | BLOCKED | Needs a VM snapshot to roll back |

### What the installation did

- Installed to `%LOCALAPPDATA%\CAMS Server`, 365 files, 179 MB. Exit code 0.
- Generated `CAMS-Server-Root.cer` and `CAMS-Server.cer`.
- Added an inbound firewall rule, **TCP 5000, private profiles only**, plus a
  discovery rule. Elevation is required for exactly this reason.
- Created a fresh database and launched the server once.

### What it did not do, which is worth recording

- **No database is shipped.** The installer excludes `CAMS.db`, `CAMS.db-shm`
  and `CAMS.db-wal`, and the published payload contains none. The database found
  after installation was created on first run.
- **No test data reached the install.** Inspecting it showed 1 class, 1 session
  rule and 3 roles, all defaults, with no students, teachers or sessions.
- **No administrator is created by default.** `InitialAdminPassword` ships
  empty, so the seeder does nothing. An initial account is only created when
  `/AdminUsername` and `/AdminPassword` are passed, and the installer rejects a
  password shorter than twelve characters. A default installation therefore has
  no usable login until an administrator is provisioned deliberately, which is
  the safe behaviour.

The `admin1` account present afterwards was seeded manually for testing and is
not something the installer creates.

### Verified on the installed product

- Signs in and lands on `/Admin/Index`.
- Serves `/fonts/inter-latin.woff2` as `font/woff2`.
- The stylesheet makes no request to `fonts.googleapis.com`.
- The only failing request on a page load is `/favicon.ico`, which is cosmetic;
  browsers request it automatically and none is shipped.

## Effect on the release decision

Unchanged: **still blocked**. Clean install now has evidence, but upgrade,
uninstall, the two-client classroom walkthrough and network recovery remain
untested, and capacity is still unassessed for want of approved limits.
