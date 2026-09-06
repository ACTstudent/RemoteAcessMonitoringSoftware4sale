# Repository inventory

OPS-02. What this repository tracks, sorted into source and release artifacts,
so the two stay distinguishable. Measured 2026-09-06 at `18f51bd`.

## The headline

**355 tracked files, 176.29 MB. Two of them are 97.4% of it.**

| | Bytes | Share |
| --- | ---: | ---: |
| `server-dist/CAMS-Server-Setup.exe` + `client-dist/CAMS-Client-Setup.exe` | 171,750,187 | 97.4% |
| Everything else — all source, assets, tests, docs | 4,543,669 | 2.6% |

Every release rewrites both binaries, so each one adds roughly its full size to
history permanently. This is the single fact that should drive the decision in
the last section.

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

## Release artifacts that are tracked

`server-dist/` and `client-dist/` hold the two installers, their `.sha256` files
and `release-manifest.json`. All five are build outputs, tracked by an explicit
`.gitignore` exception. Nothing else generated is tracked.

They are also, as of `b4134fd`, the same bytes as the published v2.12.0 release —
but only because that release was uploaded from this machine. A release cut by
`release.yml` rebuilds on a CI runner, and Inno output is not reproducible, so
the tracked copies and the published assets diverge. They did for v2.11.6:
tracked `D359E853…`, published `96015CF6…`. Two different binaries for one
version number, with the repository's checksum file describing neither.

## The decision this inventory does not make

The plan is explicit that moving the installers out is a policy change, not
cleanup: it "must update CI, download links and packaging checks together". So
this document records the position rather than changing it.

**Keeping them** means a fresh clone contains a runnable installer with no
network access, and 97.4% of the repository is binary that diverges from what
ships.

**Dropping them** means the release is the single source, the repository becomes
about 4.5 MB, and the divergence cannot recur — at the cost of updating
`.gitignore`, the `release.yml` upload path, `test-installer.ps1`, and any
documentation pointing at `server-dist/`. Existing history keeps its size either
way; only new releases stop adding to it.

Whichever is chosen, the checksum files should describe the artifact a user can
actually download. Today they describe a build that only exists on one machine.
