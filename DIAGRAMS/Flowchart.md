# CAMS System Flowchart

This flow separates public release discovery, authenticated local deployment, browser portal login, and strict Windows CLIENT login.

```mermaid
flowchart TD
    P[Public GitHub Pages portal] --> R[GitHub release installers, hashes, release manifest]
    R --> S[Admin installs self-contained CAMS Server]
    S --> F[Configure first Admin; no default password]
    F --> TLS[Start server on target LAN and establish TLS trust]
    TLS --> H[Admin signs in to local /Admin/Deployment]
    H --> V{Assets, versions, hash, certificate, endpoint valid?}
    V -- No --> X[Show warnings; correct package, version, trust, or network]
    X --> V
    V -- Yes --> B[Download installer or create offline bundle]
    B --> I[Install client for intended Windows user]

    I --> L{Login path}
    L -- Browser /Account/Login --> WP[Student web portal only]
    L -- WinForms CLIENT --> C[Send credentials and PC name over HTTPS]
    C --> A{Active student assigned to this station?}
    A -- No --> D[Reject login and audit denial]
    A -- Yes --> LS[Create or resume one active LabSession]
    LS --> SR[Open authenticated SignalR connection]
    SR --> M[Teacher receives authorized screen/status events]
    M --> Q{Authorized classroom action?}
    Q -- Continue --> CAP[Capture loop targets 50 ms; actual rate varies]
    CAP --> M
    Q -- Warning or policy --> W[CAMS topmost dialog or application enforcement]
    W --> M
    Q -- Remote command --> OS[Client asks Windows to perform command]
    OS --> M
    Q -- Pause/resume --> PS[Persist state and notify client]
    PS --> M
    Q -- End/logout/expire --> E[End session, release station, notify and log out client]

    SR --> N{Temporary network loss?}
    N -- Yes --> RE[Remove live connection; retain persisted active session]
    RE --> SR
```

## Operational Notes

1. The public portal and GitHub release contain public packages only. A deployment root CER and offline bundle come from the authenticated local Deployment Hub.
2. Deployment Hub recomputes installer SHA-256, checks manifest/checksum/version consistency, inspects certificate coverage, and reports connected clients.
3. Browser Student login does not create an assigned-workstation monitoring session. The CLIENT path requires the mapped station name.
4. Teacher views and commands are scoped to assigned classes, accessible students/computers, and owned sessions. Admin has global UI controls.
5. Website warnings are CAMS topmost dialogs, not browser popups. CAMS does not close the browser.
6. Unlock releases CAMS lock state but cannot unlock the Windows secure desktop.
7. The server sends UDP discovery to client port `5001`; operational HTTPS/SignalR uses server inbound Private TCP `5000`.
8. After a server network/address change, restart CAMS, verify a certificate-compatible endpoint, update saved client URLs when needed, and confirm reconnection in Deployment Hub and the monitoring grid.
