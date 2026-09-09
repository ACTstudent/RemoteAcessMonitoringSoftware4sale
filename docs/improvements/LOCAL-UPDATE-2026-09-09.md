# Local CAMS update — 2026-09-09

## Standard green tables

- Shared CRUD table headers now use the existing forest green (`#185c3c`) regardless of whether the table is inside a CRUD panel or a regular card. This includes Browser Monitoring History and grouped Alerts.
- Checkbox checked, mixed-selection, and focus states use green. The Alerts “Clear the filters” link uses green as well.
- Expanded event tables retain their existing lighter green header to distinguish nested details. Grouping, filtering, and pagination behavior is unchanged.

## Build and local file update

- Published the server successfully in Release mode for self-contained Windows x64, version 2.17.0. Rebuilt the local server installer and updated its SHA-256 and release manifest.
- Reused the existing 2.17.0 student client build and installer; this styling change requires no client source change. The client installer matches the copy and manifest in the server's DeploymentAssets folder.
- Copied published server and client program files into `%LOCALAPPDATA%\CAMS Server` and `%LOCALAPPDATA%\CAMS Student Client` while neither application was running. These are file updates; Windows installer registration was not changed.
- Existing settings, database, and certificates were preserved. Previous overwritten application files are backed up in `%LOCALAPPDATA%\CAMS Update Backups\20260909-221346`.
- Source, published, and installed copies of `crud.css` have matching SHA-256 hashes.

At the initial file update, applications were not launched and no tests were run. After the user requested screenshots, the installed server was started. The later compact-layout update below was built and applied, then the installed server was restarted. No automated tests or live session, reboot, or browser-blocking tests were run.

The refreshed installer is `server-dist/CAMS-Server-Setup.exe`. The existing matching client installer is `client-dist/CAMS-Client-Setup.exe`, also staged in the installed server's DeploymentAssets folder. No public release was uploaded, and no other classroom PC was updated during this work.

## Compact card layout and screenshots

The user approved matching the Teacher Management table card. History and Alerts now put their filters, inset green table, and compact pager inside one shared CRUD panel. The footer remains visible for one page and empty results, showing the record range and disabled previous/next controls where appropriate. Pagination keeps the current filters and page size.

Alerts places date and student filters under More filters; that section opens automatically when those filters are active. Bulk controls appear when the result contains alerts. Existing grouping and alert action forms remain in place.

Because the user did not know the existing login password, screenshots were captured using a separate demo database and Teacher account under the gitignored `session/screenshot-demo` folder. This demo binds only to localhost on port 5105 and disables LAN discovery. It uses the actual updated views and styles; the installed account credentials were not reset. The demo was left available in the browser for the user.

Captured images: `session/screenshots-green/browser-history-compact.png` and `session/screenshots-green/monitoring-alerts-compact.png`. Both show empty demo data. The Release publish and installer compilation succeeded. Updated server files are installed locally; the previous files are backed up under `%LOCALAPPDATA%\CAMS Update Backups\20260909-223445`.
