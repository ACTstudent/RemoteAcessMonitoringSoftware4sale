# Website and application enforcement — 2026-09-08

Update (2026-09-09): the implementation is included in the local 2.17.0 build artifacts, and this computer's installed server and client program files have been updated. Other student PCs still need the updated client installer. No live blocking tests or Windows proxy changes were performed, honoring the user's request to skip testing. See [local update details](LOCAL-UPDATE-2026-09-09.md).

## Problem and resulting behavior

The old website path showed alerts in ordinary browsers and polled CAMS-managed tabs for closure after loading. Dismissing an alert did not stop access. The student client now starts a loopback request filter before enabling browsing for the session. Blocked HTTP requests and HTTPS CONNECT destinations are rejected before contacting the destination. An HTTPS rejection appears as a browser tunnel/connection error; HTTP receives a 403 school-policy message. Alert cooldowns do not affect the filter.

Both browser paths use the filter:

- CAMS-launched Chrome and Brave receive an explicit proxy argument with no direct fallback; QUIC is disabled in these managed processes.
- Windows' current-user LAN proxy is temporarily set to the same listener, covering ordinary browsers that use those settings. The monitoring server hostname remains exempt so classroom controls stay reachable.
- Rule refresh publishes a complete snapshot and drops filtered connections to newly blocked destinations, including background downloads/tunnels. More-specific rules take priority and Allow wins specificity ties. Update (2026-09-16): a website Allow rule no longer switches the session into allowlist mode. Allow rules are exceptions, as the Whitelist page states; a destination no rule mentions is not blocked. The previous behaviour blocked the rest of the web as soon as one site was whitelisted, and the application half of it closed every unlisted window on the desktop. Rules still apply to normalized domains, not URL paths.
- Update (2026-09-10): CAMS no longer launches its own managed Chrome, Brave or Edge, because doing so opened browser windows nobody asked for. There is therefore no DevTools tab closure: a page loaded before a rule arrives stays on screen until it is reloaded, though the foreground address-bar check still reports it as a violation.
- The proxy stores only bounded blocked-domain notifications, not page bodies, paths, credentials, or decrypted HTTPS contents. It uses raw outbound sockets, so it does not recursively inherit its own system proxy.

The proxy approach follows [Chromium's HTTP/HTTPS proxy and CONNECT behavior](https://chromium.googlesource.com/chromium/src/+/HEAD/net/docs/proxy.md). Windows configuration uses [WinINet connection options](https://learn.microsoft.com/en-us/windows/win32/wininet/setting-and-retrieving-internet-options), with configured flags read using [FLAGS_UI](https://learn.microsoft.com/en-us/windows/win32/api/wininet/ns-wininet-internet_per_conn_optiona).

## Session lifecycle and recovery

`WindowsSessionProxy` saves the previous manual proxy, bypass list, auto-configuration URL, and configured flags to `%LOCALAPPDATA%\CAMS\session-proxy-backup.json` before changing Windows settings. Logout and normal exit restore that snapshot before stopping the filter. A startup recovery pass restores an interrupted session's settings if Windows still points to CAMS's previous listener. An administrator's subsequent replacement proxy is preserved. A single-instance guard prevents two clients in the same Windows session from taking ownership simultaneously.

Proxy configuration failures stop sign-in instead of silently falling back to warnings. Runtime enforcement errors are displayed in the client status. While a session runs, the enforcement loop reapplies changed Windows proxy settings. If the client is forcibly terminated, ordinary browsing can remain disconnected until CAMS is restarted and recovery runs.

## Deployment requirements and limits

1. Rebuild/package the student client, deploy it to each student workstation, and restart CAMS. Updating the web server alone does not replace the client executable.
2. Use the student Windows account's default LAN/system proxy configuration in ordinary Chrome, Edge, Brave, or other compatible browsers. Restart existing ordinary browser windows after sign-in to avoid old direct connections and stale proxy settings.
3. Browser-specific proxy configurations, extensions that override proxies, VPN/RAS connections, other Windows accounts, and applications that bypass Windows proxy settings require separate workstation/network policy. This is not an OS firewall or tamper-proof kiosk.
4. A connection filter cannot erase content already cached or rendered in ordinary browser windows. Managed tabs are additionally closed on detection. Browser cache policy or a managed kiosk is needed if offline/cached content must also be inaccessible.
5. This implementation opens allowed destinations directly and does not chain an existing upstream corporate proxy/PAC. Networks requiring such an upstream proxy need integration before rollout. All proxy-aware traffic for the signed-in Windows user is subject to the temporary settings, not just browser windows.
6. The client must remain running during the session. If restoration fails, the recovery file is retained and the UI directs the operator to restart CAMS.

## Pending validation for the next authorized test pass

- Build the Windows client and verify HTTP, HTTPS, WebSocket, and download forwarding to allowed sites.
- In Chrome, Brave and Edge, block a domain and its subdomains; verify no new destination connection occurs and reopening during the alert cooldown stays denied.
- Add a block while a filtered download/tunnel is active; verify disconnection. Remove/deactivate the rule and verify access returns. Check wildcard, full-URL normalization, Allow exceptions, and class/session scope. With only Allow rules present, confirm unlisted sites still load and unlisted applications stay open.
- Verify successful login, failed setup rollback, logout, teacher-forced session end, normal exit, crash recovery, and duplicate-instance handling. Compare the complete original/restored proxy settings, including PAC and auto-detect flags.
- Verify monitoring-server connectivity, policy updates during reconnect, and a clear client status when Windows policy refuses the proxy setting. Check cached pages, custom proxies/VPNs, and upstream-proxy networks against the limits above.

## Earlier application corrections retained

Update (2026-09-16): application enforcement is gone from the client. CAMS no longer closes processes, and application rules produce no violation or alert from the workstation; the desktop is left alone and only websites are enforced. Active-application monitoring is unchanged, so teachers still see what is in the foreground. Immutable rule snapshots still avoid concurrent mutation for website rules.
