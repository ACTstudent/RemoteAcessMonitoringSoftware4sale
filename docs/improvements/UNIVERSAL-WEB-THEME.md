# Universal web theme

The Admin, Teacher and Student portals use `_AppLayout.cshtml`. Login and the standalone error page load the same shared palette and component theme without depending on a database-backed layout.

## Ownership
- `site.css`: palette, typography, spacing and radius tokens; base components.
- `crud.css`: compact directory layout, sizes, filters and pagination. Do not redefine the palette on `body.crud-page`.
- `warm-theme.css`: shared component appearance, Bootstrap integration, login and deployment layouts, sidebar groups and live monitoring layout.
- `motion.css`: transitions and reduced-motion support.

Use semantic Bootstrap classes for success, warnings and destructive actions. New pages should reuse the shared layout and tokens instead of adding inline colors or page-local style blocks. Keep screen-stream imagery and status semantics intact.

## Verification and scope
Server build succeeded with zero warnings/errors. Rendered representative directory components at desktop and mobile sizes, plus the monitoring layout, form/status components and a static login layout. These are layout previews, not authenticated end-to-end verification. No test suite was run and no workstation commands were sent. Changes have not been deployed or pushed.

## Dashboard page coverage

All portal layouts route through `_AppLayout`, which unconditionally loads the theme. The Windows client and public download site are outside this dashboard-only scope. This is a source-layout audit, not a claim that every page has been exercised in a browser.

| View | Layout |
| --- | --- |
| `Admin/_Layout.cshtml` | `~/Views/Shared/_AppLayout.cshtml` |
| `Admin/AuditLogs.cshtml` | `_Layout` |
| `Admin/Blacklists.cshtml` | `_Layout` |
| `Admin/ClassDetails.cshtml` | `_Layout` |
| `Admin/Classes.cshtml` | `_Layout` |
| `Admin/ComputerHistory.cshtml` | `_Layout` |
| `Admin/Computers.cshtml` | `_Layout` |
| `Admin/Index.cshtml` | `_Layout` |
| `Admin/LanConfig.cshtml` | `_Layout` |
| `Admin/Reports.cshtml` | `_Layout` |
| `Admin/Restrictions.cshtml` | `_Layout` |
| `Admin/Roles.cshtml` | `_Layout` |
| `Admin/SessionRules.cshtml` | `_Layout` |
| `Admin/Settings.cshtml` | `_Layout` |
| `Admin/Students.cshtml` | `_Layout` |
| `Admin/SystemLogs.cshtml` | `_Layout` |
| `Admin/Teachers.cshtml` | `_Layout` |
| `Admin/Whitelists.cshtml` | `_Layout` |
| `AdminDatabase/Index.cshtml` | `~/Views/Admin/_Layout.cshtml` |
| `AdminDeployment/Index.cshtml` | `~/Views/Admin/_Layout.cshtml` |
| `Student/_StudentLayout.cshtml` | `~/Views/Shared/_AppLayout.cshtml` |
| `Student/Alerts.cshtml` | `_StudentLayout` |
| `Student/Index.cshtml` | `_StudentLayout` |
| `Student/Settings.cshtml` | `_StudentLayout` |
| `Teacher/_TeacherLayout.cshtml` | `~/Views/Shared/_AppLayout.cshtml` |
| `Teacher/AlertHistory.cshtml` | `_TeacherLayout` |
| `Teacher/Alerts.cshtml` | `_TeacherLayout` |
| `Teacher/BrowserMonitoringHistory.cshtml` | `_TeacherLayout` |
| `Teacher/ClassAnalytics.cshtml` | `_TeacherLayout` |
| `Teacher/ClassDetails.cshtml` | `_TeacherLayout` |
| `Teacher/Classes.cshtml` | `_TeacherLayout` |
| `Teacher/Computers.cshtml` | `_TeacherLayout` |
| `Teacher/Dashboard.cshtml` | `_TeacherLayout` |
| `Teacher/LabUtilization.cshtml` | `_TeacherLayout` |
| `Teacher/Monitoring.cshtml` | `_TeacherLayout` |
| `Teacher/Records.cshtml` | `_TeacherLayout` |
| `Teacher/RemoteHistory.cshtml` | `_TeacherLayout` |
| `Teacher/Restrictions.cshtml` | `_TeacherLayout` |
| `Teacher/Sessions.cshtml` | `_TeacherLayout` |
| `Teacher/Settings.cshtml` | `_TeacherLayout` |
| `Teacher/StudentDetails.cshtml` | `_TeacherLayout` |
| `Teacher/Students.cshtml` | `_TeacherLayout` |
| `Teacher/UnifiedTimeline.cshtml` | `_TeacherLayout` |
