# Blocking correction — 2026-09-07

Source changes only. No tests, builds, process termination or live tab closure were run, at the user's request. Existing sidebar scroll changes were preserved.

- Normalize application targets so executable names/paths such as notepad.exe match Windows ProcessName values without extensions.
- Normalize website URLs to domains while retaining wildcard patterns.
- Replace mutable rule lists with an atomically published snapshot so background enforcement cannot enumerate a partially refreshed policy.
- Enforce application closure on every pass; only alerts/popups use the 30-second cooldown. Report Windows permission failures without claiming closure.
- Add blocked-tab closure through the local DevTools endpoint only for browsers launched and tracked by CAMS, with process/metadata checks. Preserve current rule specificity and Allow tie precedence. Invalid/inactive or out-of-scope rules are not made active by this change.
- Read current foreground website observations for ordinary-browser warnings instead of using the last telemetry sample. Managed-browser enforcement scans tabs directly and does not depend on telemetry succeeding.

Selected behavior: close the blocked tab in the CAMS-managed browser. Ordinary browser windows are warning-only. Enforcement polls approximately every four seconds plus processing time; it is not a network filter or a guarantee that page content never loads. OS privileges can prevent application closure. Existing managed-browser identity checks remain; unsupported browser metadata or unavailable managed endpoints prevent tab enforcement.

Required follow-up, not executed: build/update the client; verify executable-name/URL rules, blocked-tab closure without affecting allowed tabs, reopened app/tab enforcement during cooldown, policy refresh, managed-browser availability, and failed-closure reporting. Confirm both managed Chrome and Brave identities in the deployment. No installers or installed binaries were replaced.

DevTools close endpoint: https://chromedevtools.github.io/devtools-protocol/#get-jsonclosetargetid
