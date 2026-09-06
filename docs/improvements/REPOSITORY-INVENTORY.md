# Repository inventory

OPS-02. What this repository tracks, sorted into source and release artifacts,
so the two stay distinguishable. Measured 2026-09-06 at `18f51bd`.

## The headline

**383 tracked files, 4.46 MB — all of it source, assets, tests and documentation.**

When this inventory was first taken it read: *355 tracked files, 176.29 MB, two
of them 97.4% of it.* The two installers were 171,750,187 bytes against
4,543,669 for everything else, and every release rewrote both.

They are no longer tracked. See the last section for what that changed and what
it did not.

## Generated files — ignored, correctly

`.gitignore` covers `bin/`, `obj/`, `publish/`, `client-publish/`,
`server-publish/`, `session/`, `dist/`, `.vs/`, `*.user` and `test-results/`.
Spot checks confirm no `bin` or `obj` content is tracked.

## Runtime data — ignored, with one deliberate exception

`*.db`, `*.db-shm` and `*.db-wal` are ignored, so no SQLite database is tracked.
No `.pfx`, `.cer`, `.key` or `.pem` is tracked; the build refuses to package a
PFX and `test-installer.ps1` asserts it.

The exception is `Server/appsettings.json`, tracked on purpose as the blank-secret
base. The build empties `Cams:CertificatePassword` and `Cams:InitialAdminPassword`
before packaging and fails if either is populated, so the file cannot carry a
secret into an installer.

## Vendor assets

| Asset | Version | Licence notice present? |
| --- | --- | --- |
| Bootstrap CSS + JS bundle | 5.3.3 | Yes — MIT banner in both files |
| Bootstrap Icons CSS + fonts | 1.11.3 | Yes — MIT banner in the CSS |
| SignalR JavaScript client | not stated in the file | **No** |

## Self-hosted fonts — the gap

Four font files ship in `wwwroot/fonts/`: Inter (latin, latin-ext) and Plus
Jakarta Sans (latin, latin-ext), 182 KB together. They are self-hosted
deliberately — the deployment guide requires no CDN, and UX-05 verified it.

**Neither font has a licence file, a copyright notice, or any recorded
provenance.** Nothing in the repository says where they came from or under what
terms they may be redistributed, and they are redistributed: they go inside the
server installer to every school that installs CAMS.

The same applies to the SignalR client, which unlike the two Bootstrap files
carries no banner of its own.

This is a licensing gap, not a cosmetic one, and it is the one thing this
inventory turned up that needs action rather than a decision. Both typefaces are
widely published under the SIL Open Font License, which requires the licence
accompany the files — but that must be confirmed against the actual upstream
release each file came from, not assumed from the typeface name, and the
provenance was never recorded. **Do not write a licence file from memory.**
Re-obtain each file from its upstream project, record the version and source,
and commit the licence that came with it.

## Release artifacts are no longer tracked

`server-dist/` and `client-dist/` are build output and are gitignored. The
[GitHub release](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/latest)
is the only place an installer is published.

**Why this changed.** Size was the obvious reason and the weaker one. The real
problem was that committing them put a *second* copy of each installer in the
repository which was never the one anyone downloaded. Inno Setup does not
produce identical bytes twice, and `release.yml` rebuilds on a CI runner when a
tag is pushed, so the committed copy and the published asset always differed:

| Release | Committed | Published |
| --- | --- | --- |
| v2.11.6 | `D359E853…` | `96015CF6…` |
| v2.13.0 | `86DB1285…` | `8C03361E…` |

Two different binaries for one version number, and the repository's own
`.sha256` file described neither — it described a build that existed on one
machine. Anyone following the verification procedure in `DEPLOYMENT.md` against
the repository copy would get a mismatch and reasonably conclude something was
wrong.

**What still works.** `build-everything.ps1` recreates both folders, so
`test-installer.ps1` validates as before and `release.yml` and `ci-full.yml`
upload from them unchanged — all three run after the build that produces them.
The portal's download buttons already addressed the release rather than the
repository.

**What this did not do.** History keeps its size: the ~170 MB per release
already committed is still in the object store. Only new releases stop adding to
it. Shrinking what is already there means rewriting history, which invalidates
every existing clone and every commit hash quoted in these documents — not worth
it to reclaim space in a repository nobody is short of.

**The cost.** A fresh clone no longer contains a runnable installer; you need
either network access to the release or a local `build-everything.ps1` run.
That is the trade, and it is the right way round: the copy people actually
install is now the only copy, and its published checksum describes it.
