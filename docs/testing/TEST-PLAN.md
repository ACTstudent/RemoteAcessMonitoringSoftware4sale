# CAMS whole-codebase test plan

Prepared: 2026-09-05. Inspected baseline: `b6920e4ebe581a961cf9a8236f45b6e8155584e9`.
Status: planning and source inspection only; no application tests, installers, or live remote commands were executed for this document. All planned cases begin as NOT RUN.

## Documents and traceability

- [Source inventory](SOURCE-INVENTORY.md): every tracked file assigned to a verification area.
- [Test cases](TEST-CASES.md): procedures, expected outcomes, priorities, and evidence.
- [Run report template](RUN-REPORT-TEMPLATE.md): execution record, defects, and release decision.
- Existing requirements: [README](../../README.md), [CAMS Guide](../../CAMS-Guide.md), [Deployment](../../DEPLOYMENT.md), and [diagrams](../../DIAGRAMS/).

For every change, map affected source files to case IDs, map cases to automated test names where applicable, and attach results to a run report. A filename match is a starting point, not proof of behavioral coverage. Generated code and vendor files receive build/integration checks; they do not need tests duplicating their implementation. Any exclusion requires a reason, owner, and review date.

## Observed baseline

CAMS includes an ASP.NET Core .NET 8 server, SQLite persistence and migrations, a Windows WinForms client, shared SignalR contracts, Razor/JavaScript interfaces, a static public portal, PowerShell/Inno Setup packaging, and a separate database cleaner.

There are 28 server test files and 6 client test files using xUnit. Server tests include Moq, EF InMemory and SQLite and have coverlet.collector configured; the client project has no coverage collector configured. Test-file counts are not test-case counts or coverage percentages. The solution includes Server, Client and Shared, but not the test projects or database cleaner. Consequently, testing the solution alone is insufficient.

`.github/workflows/ci-full.yml` runs `build-everything.ps1` on Windows. That script explicitly tests both test projects, builds, publishes and packages installers, then invokes `test-installer.ps1`. Artifact validation does not establish that interactive installation, upgrade, uninstall or real remote control works.

Existing suites cover accounts, classes, workstation registration, session lifecycle, telemetry, analytics, policies, certificates, deployment, database maintenance and mocked hub security. The input simulator test checks coordinate scaling, not actual Windows input delivery. Full HTTP middleware, real SignalR transport, browser workflows, interactive Windows behavior and installer lifecycle require additional validation. Directly named test files were not found for StudentController, ClientAuthController or MonitoringController; indirect coverage must be checked before declaring those behaviors absent.

## Environments and fixture data

1. Automated lane: disposable Windows x64 checkout, .NET 8 SDK, pinned restored dependencies; Inno Setup 6 for packaging. Record SDK, OS build, architecture and dependency resolution.
2. Integration lane: temporary file-backed SQLite databases, isolated server output directory and dedicated HTTPS port. Never point tests at an installed CAMS database. Startup uses AppContext.BaseDirectory, creates certificates, migrates/seeds data and launches a browser; account for these side effects before introducing an HTTP host fixture.
3. Interactive lane: one server and at least two Windows client VMs on a Private LAN, plus separate browser sessions for each role. Snapshot before installer, certificate, restart/shutdown and restore tests. Use synthetic accounts and disposable client sessions.
4. Compatibility lane: each Windows version and browser version intended for deployment; include Edge/Chrome, standard Windows user and administrator, 100/150/200% display scaling, multi-monitor, wired/Wi-Fi and offline installation.
5. Capacity lane: synthetic SignalR clients for connection/telemetry scale plus real clients for representative screenshot and input load. Synthetic traffic alone cannot measure capture performance.

Fixture: Admin A; Teachers T1/T2 and inactive T3; Students S1/S2 in T1's class, S3 in T2's class, S4 unassigned and S5 inactive; active and archived classes; available, assigned, in-use, archived and maintenance workstations. Seed running, paused, expired and ended sessions; global and teacher rules; alerts of each supported lifecycle state; timestamps around midnight and retention boundaries. Use unique run suffixes. Record generated IDs without passwords.

Before each suite restore its fixture. Afterward stop test processes, remove only that run's temporary data, revert VM snapshots and any test trust/firewall changes, and retain sanitized evidence. Remote restart/shutdown executes only on the disposable clients.

## Execution sequence and ownership

| Phase | Work | Suggested owner | Completion evidence |
| --- | --- | --- | --- |
| 1 | Freeze commit, review inventory and requirements, prepare fixtures | QA + maintainer | Run manifest and source-to-case mapping |
| 2 | Restore/build; run existing server/client suites; build cleaner | Developer | Logs, TRX, server coverage |
| 3 | Add missing unit boundaries and real SQLite/HTTP/SignalR integration | Developer | New tests mapped to cases; reproducible fixtures |
| 4 | Run role portals and two-client classroom workflows | QA | Case results, screenshots, logs and DB assertions |
| 5 | Network recovery, Windows compatibility, security and capacity | QA + deployment owner | Matrix and measurements |
| 6 | Canonical package, clean install, upgrade, restore and rollback rehearsal | Release owner | Hashes, installer logs, recovery evidence |
| 7 | Retest defects and approve release report | QA + maintainer | Signed decision and explicit residual risks |

Prioritize P0 cases first: authorization, session integrity, remote-control identity, telemetry identity, database preservation and trust. Then P1 core workflows and recovery; P2 presentation/accessibility and lower-risk compatibility. Do not estimate completion from file counts: size phases after the first baseline run and defect triage.

## Baseline commands

Run from repository root in a disposable checkout. These commands are instructions, not results. Check `$LASTEXITCODE` after each native command and stop on failure; a later successful command must not hide an earlier failure.

```powershell
$runPath = Join-Path $env:TEMP ('CAMS-test-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $runPath | Out-Null
git rev-parse HEAD | Set-Content (Join-Path $runPath 'commit.txt')
git status --short | Set-Content (Join-Path $runPath 'working-tree.txt')
dotnet --info | Out-File (Join-Path $runPath 'dotnet-info.txt')
dotnet restore 'Monitoring And Remote Access/RemoteMonitoring.sln'
dotnet build 'Monitoring And Remote Access/RemoteMonitoring.sln' -c Release --no-restore
dotnet test 'Monitoring And Remote Access/Server.Tests/Server.Tests.csproj' -c Release --logger 'trx;LogFileName=server.trx' --results-directory $runPath --collect 'XPlat Code Coverage'
dotnet test 'Monitoring And Remote Access/Client.Tests/Client.Tests.csproj' -c Release --logger 'trx;LogFileName=client.trx' --results-directory $runPath
dotnet build 'tools/CamsDbCleaner/CamsDbCleaner.csproj' -c Release
```

Test commands restore their own projects because they are outside the solution. Record discovered/executed/passed/failed/skipped totals from TRX. Preserve console logs as well. Client coverage is a future setup task: configure a compatible collector before requesting it. Use focused test filters only for debugging, then rerun the affected full suite.

Packaging, in a disposable checkout with Inno Setup installed:

```powershell
.\build-everything.ps1
# Revalidate already generated staging and artifacts when needed:
.\test-installer.ps1
```

The canonical build deletes and recreates client-publish, client-dist, server-publish and server-dist. Preserve release evidence outside those directories. Do not use packaging as the first baseline test command. Record any optional/skipped artifact inspection; a skip is not proof of contents. Test installed binaries matching the recorded SHA-256, not an unrelated development build.

## Automation to add

- HTTP integration harness for actual routing, cookies, antiforgery, authorization filters, rate limiting and security headers. Use real SQLite, isolated configuration and HTTPS; do not weaken production TLS checks to make tests pass.
- Real SignalR client harness for reconnect, connection replacement, role/object checks, malformed/oversize messages, command result attribution and broadcast lifecycle.
- Browser automation for login and every role page, CRUD validation, filter/export consistency, live monitoring rendering and escaping. Include inline Razor JavaScript as well as wwwroot assets.
- Windows UI/manual cases for MainForm, capture, input, dialogs, browser collectors and OS commands. Keep these on interactive agents, outside ordinary headless unit jobs.
- Integration tests for the standalone cleaner with process exit-code/output checks and before/after SQLite assertions.
- CI report uploads on success and failure: TRX, coverage, build logs and sanitized failure artifacts. Keep release packaging gated by both test projects. Add scheduled interactive/capacity runs only after reliable harnesses exist.

## Coverage and acceptance

First measure coverage; this plan does not claim a baseline percentage. Review uncovered authorization, state transitions and recovery branches before choosing numeric thresholds. For changed critical logic require positive, negative, boundary and failure-path tests. Preserve baseline coverage unless an exclusion is justified. Coverage alone cannot substitute for data integrity or live Windows validation.

Release requires: both automated suites pass with no unexplained skips; all P0/P1 cases pass on the release candidate; no unresolved critical/high defects; real SQLite migration/restore and installer upgrade pass; each intended environment has recorded results; all tracked source areas have a case mapping or reviewed exclusion. P2 deferrals need an owner and explicit rationale. A blocked required case blocks acceptance, rather than being marked passed.

Proposed capacity exercise: 1, 10, 30 and 50 clients, capped by the intended lab size, with login bursts, streaming, policy refresh and telemetry. Run a 2-hour soak and a 10-minute disconnection/recovery. Record p50/p95 page and command latency, actual FPS/frame age, CPU, memory, network throughput, SQLite write latency, queue depth and dropped records. Establish deployment-specific limits before judging performance; the 50 ms capture target is not a guaranteed frame rate. Require no cross-user delivery, corrupted data, stuck sessions or unexplained unbounded growth at any load. Compare subsequent runs with the same hardware/load and investigate regressions.

## Reporting and maintenance

Create one run report per commit/environment. Every case has PASS, FAIL, BLOCKED, NOT RUN or justified N/A, with executor, timestamp and evidence. Separate expected results from observed results. Attach defects with minimal reproduction and rerun evidence. Keep credentials, private PFX material, real student data and screen content out of committed reports. Store large/raw artifacts in restricted CI artifacts; commit sanitized summaries and links.

Review this plan when routes, hub contracts, schema, roles, client OS behavior, installer logic or deployment requirements change. Regenerate the inventory and map new files before closing the change.
