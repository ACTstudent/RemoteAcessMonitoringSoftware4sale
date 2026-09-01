# CAMS Deployment Guide

This guide deploys CAMS Computer Account Management System for supervised classroom monitoring on a trusted Windows LAN. The server and client installers are self-contained Windows x64 packages; no separate .NET runtime is required on target machines.

## Deployment Surfaces

| Surface | Audience | Contents |
| --- | --- | --- |
| [Public portal source](portal/) and expected GitHub Pages deployment | Anyone | Product information and links to public GitHub release files. No deployment certificate or credentials. Verify the Pages deployment before publishing its expected URL. |
| [GitHub Releases](https://github.com/ACTstudent/RemoteAcessMonitoringSoftware4sale/releases/latest) | Administrators | Versioned server/client installers, checksum files, release notes, and `release-manifest.json`. |
| Local `/Admin/Deployment` | Authenticated Admin only | Validated local client package, endpoint/certificate status, public root CER, offline bundle generation, and connected-client count. |

Do not obtain `CAMS-Server-Root.cer` from a public website. It is unique to a local CAMS server.

## 1. Prepare The Server

1. Choose a Windows x64 control PC and connect it to the final trusted, non-guest classroom network.
2. Enter the Admin username and a password of at least 12 characters. On a fresh database this creates the account. On an existing database, entering a password resets the matching Administrator, activates it, and clears lockout state. Leave the password blank during an upgrade to keep existing credentials unchanged.
3. Download `CAMS-Server-Setup.exe` and its `.sha256` from the same GitHub release. Also download `release-manifest.json` when that release was produced by the current release workflow; older releases may not contain it.
4. Compare `Get-FileHash .\CAMS-Server-Setup.exe -Algorithm SHA256` with the checksum file and, when present, the server artifact entry in the release manifest.
5. Run the installer. It installs under the installing user's `%LOCALAPPDATA%\CAMS Server`, refreshes only the inbound Private-profile TCP `5000` rule, and can configure current-user startup. CAMS is not installed as a Windows Service.
6. Start CAMS. It creates/migrates local `CAMS.db`; in local CA mode it also creates the certificate files described below.

There are no default passwords. Interactive server setup collects only Administrator credentials. Silent first-time setup requires `/AdminUsername=... /AdminPassword=...`; the password must contain at least 12 characters. Setup verifies database initialization before completing. Teacher and Student accounts are created after login from the Admin portal.

## 2. Bootstrap HTTPS Trust

### Local CAMS CA mode

First start creates:

| File | Purpose | Distribution |
| --- | --- | --- |
| `%LOCALAPPDATA%\CAMS Server\CAMS-Server-Root.cer` | Public local root CA | May be copied to PCs that must trust this CAMS server. |
| `%LOCALAPPDATA%\CAMS Server\CAMS-Server.cer` | Public leaf/server certificate for inspection | Optional inspection only. |
| `%LOCALAPPDATA%\CAMS Server\certificates\CAMS-Server-Root.pfx` | Root CA private key | Never copy or distribute. |
| `%LOCALAPPDATA%\CAMS Server\certificates\CAMS-Server.pfx` | Server private key | Never copy or distribute. |

For the first Admin connection, work on the server PC: inspect the public root CER and its fingerprint from the local installation directory, install that CER into the trust store for the Windows account running the Admin browser, and then open `https://localhost:5000/Account/Login`. Do not bypass a certificate warning. After Admin login, `/Admin/Deployment` shows the active certificate fingerprint/SHA-256 and is the authenticated source for distributing the public root and bundle.

The interactive client installer imports a selected root CER into the current Windows user's Root store and installs the client/settings under that user's `%LOCALAPPDATA%`. Install and run CAMS under the intended workstation user. A different Windows user does not automatically inherit that per-user client installation or trust.

The generated offline bundle script is a separate elevated path: it imports the validated public root into `LocalMachine\Root`, invokes the client installer, and performs a TLS ping. Because the client installer itself is per-user, run the bundle while signed in as the intended workstation user and verify the resulting client location and launch context. Use local policy to decide whether machine-wide root trust is acceptable.

### Production/public certificate mode

Set `Cams__CertificatePath` and `Cams__CertificatePassword` to a protected server certificate whose SAN covers every hostname or IP used by clients. A publicly trusted chain does not need the local root bootstrap. In this mode Deployment Hub does not offer `CAMS-Server-Root.cer`.

## 3. Use CAMS Deployment Hub

Sign in as Admin and open `https://<server-ip>:5000/Admin/Deployment`.

Before enabling downloads, CAMS validates that:

- `DeploymentAssets` contains the client installer, `.sha256`, and `deployment-manifest.json`.
- Installer name, size, and computed SHA-256 match the deployment manifest and checksum file.
- The installer product version matches the client version in the manifest.
- The manifest server version matches the running server version.
- The active HTTPS certificate is valid and covers the selected detected LAN IPv4 endpoint.
- The local root CER, when offered, is a public CA certificate without a private key.

The page displays warnings instead of claiming readiness when validation fails. It also displays currently connected clients. After each rollout, confirm that count increases and verify the same client in the teacher's authorized monitoring grid; the count alone is not proof of complete classroom behavior.

### Offline bundle

Choose a certificate-compatible endpoint and create `CAMS-Client-<version>-Deployment.zip`. The bundle contains:

- `CAMS-Client-Setup.exe`
- `CAMS-Client-Setup.exe.sha256`
- `deployment-manifest.json`
- `CAMS-Server-Root.cer` only in local CA mode
- `README.txt`
- `Install-CAMS-Client.ps1`
- `Install-CAMS-Client.cmd`

No account credentials or PFX private keys are included. Keep all files together, transfer the ZIP through approved offline media, extract it on the intended workstation, and run `Install-CAMS-Client.cmd` as Administrator. The script verifies the installer SHA-256, validates the root CA when present, passes the exact endpoint to Setup, and calls `/api/deployment/ping` over TLS. A successful ping validates that endpoint at that moment; still confirm student login and connected-client/monitoring behavior.

## 4. Interactive Or Unattended Client Install

For interactive installation, run the client installer as the Windows user who will run CAMS, enter the exact URL shown by Deployment Hub, and select only that server's `CAMS-Server-Root.cer` when local CA mode is used.

For unattended configuration:

```powershell
.\CAMS-Client-Setup.exe /ServerUrl=https://192.168.1.100:5000/remoteMonitoringHub /ServerRootCert=C:\Deploy\CAMS-Server-Root.cer
```

`/ServerUrl` is the preferred switch. Legacy `/ServerIP` remains an alias and still expects the complete HTTPS hub URL, not a bare IP. If both are supplied, they must have the same value. `/ServerRootCert` must point to an existing `.cer`. The endpoint must use HTTPS and end exactly in `/remoteMonitoringHub` with no query or fragment.

The client saves the normalized URL in its per-user installation settings. UDP discovery can find a server, but a manual saved URL remains the fallback.

## 5. Firewall And Discovery

Required paths:

| Host | Direction | Protocol/port | Purpose |
| --- | --- | --- | --- |
| Server | Inbound | TCP `5000`, Private profile | HTTPS browser, API, and SignalR traffic. |
| Server | Outbound | UDP to destination `5001` | Periodic discovery broadcasts. |
| Student client | Inbound | UDP `5001` when discovery is used | Receives discovery broadcasts. |
| Student client | Outbound | TCP `5000` | Connects to the server. |

The server does not listen on UDP `5001` and needs no inbound UDP `5001` rule. If discovery is blocked by VLANs, hotspot isolation, guest Wi-Fi, endpoint security, or firewall policy, use the manual HTTPS URL. Device-to-device TCP `5000` must still be reachable.

LAN Status is a detected, read-only page. It does not set DHCP, DNS, gateways, adapter addresses, firewall policy, or server binding.

## Network Changes And Restart Behavior

The generated root CA remains stable while its private root PFX remains in the server installation. On every server start, local CA mode issues a leaf certificate for the machine name and LAN addresses detected at that start; discovery advertises current viable addresses while the process runs.

After joining another LAN, changing an address, or switching hotspots:

1. Restart CAMS Server after the network is active.
2. Open LAN Status and Deployment Hub.
3. Confirm the intended endpoint is detected and certificate-compatible.
4. Update clients whose saved `/ServerUrl` address changed; trusting the same root alone does not rewrite the saved endpoint.
5. Confirm TLS connection, strict assigned-workstation login, Deployment Hub connected-client count, and the teacher monitoring card.

Do not claim seamless reconnection across an address change until it has been tested on that LAN. Discovery, certificate coverage, saved URLs, firewall policy, and client isolation can each affect recovery.

## Manual Folder Publish

Build from the repository root:

```powershell
dotnet publish "Monitoring And Remote Access\Server\Server.csproj" -c Release -r win-x64 --self-contained true -o server-publish
dotnet publish "Monitoring And Remote Access\Client\Client.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o client-publish
```

The canonical `build-everything.ps1` path is preferred because it tests, creates installers and hashes, stages exact Deployment Hub assets, enforces blank packaged secrets, rejects packaged PFX/database files, aligns versions, and runs installer validation. A manually copied server folder must include the correct `DeploymentAssets` if Deployment Hub downloads are required.

## Release And Public Portal Publishing

1. Set the three-part version in `version.json` and build with `build-everything.ps1`.
2. Verify both installer checksum files and `server-dist\release-manifest.json`.
3. Push a matching `vMAJOR.MINOR.PATCH` tag, or manually dispatch the release workflow with that tag.
4. `.github/workflows/release.yml` rejects mismatched tags, rebuilds/validates on Windows, and publishes both installers, both checksums, and `release-manifest.json`.
5. Changes under `portal/**` pushed to `main`, or a manual Pages dispatch, are configured to publish the static public site through `.github/workflows/pages.yml`. Enable GitHub Pages and verify the workflow's reported URL before advertising the site.
6. Keep the public flow limited to public artifacts. Never add a local root CER, PFX, database, settings with secrets, credentials, or an offline deployment bundle to the public portal or release.

## Windows And LAN Validation Checklist

- Verify installer and manifest versions/hashes before execution.
- Verify first trust using the expected certificate SHA-256 without bypassing TLS warnings.
- Verify interactive per-user install/trust or explicitly approve the bundle's machine-wide root import.
- Verify browser student portal login separately from strict assigned-workstation CLIENT login.
- Verify server inbound Private TCP `5000`; verify client inbound UDP `5001` only if discovery is required.
- Verify connected-client count and the correct teacher-scoped monitoring card.
- Verify screen updates under realistic load; 50 ms is a capture-loop target, not guaranteed FPS.
- Verify CAMS topmost warning dialogs, application/domain policies, telemetry reconnect, and session pause/resume/end.
- Verify lock behavior; CAMS cannot programmatically unlock the Windows secure desktop.
- Verify restart/shutdown and remote input under local Windows privilege, endpoint-security, and classroom policy.
- Verify a server network/address change using the restart and client URL update procedure above.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Browser/client reports untrusted TLS | Confirm the expected public root CER is trusted for the relevant Windows user, or the approved machine store for bundle deployment. Do not use a PFX or bypass the warning. |
| Certificate name mismatch | Restart the server on the target network and select an endpoint marked compatible by Deployment Hub. |
| Discovery fails | Allow client inbound UDP `5001`, remove client isolation, or use `/ServerUrl`. Do not add server inbound UDP `5001`. |
| TCP connection fails | Confirm server is running, network is Private, inbound TCP `5000` is allowed, and devices can communicate. |
| CLIENT rejects valid student credentials | Confirm that student is active and mapped to the workstation name reported by the client. Browser portal login does not perform this station check. |
| Client remains absent | Confirm TLS ping, saved endpoint, client process/user, SignalR login, Deployment Hub count, and teacher scope. |
| Server moved networks | Restart server, verify certificate-compatible endpoint, update saved client URLs, and retest. |
