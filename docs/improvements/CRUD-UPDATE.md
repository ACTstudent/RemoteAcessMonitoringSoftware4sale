# CRUD interface update — 2026-09-06

Applied the supplied management-screen references to Admin/Teacher account, class, roster, workstation, session-rule and policy CRUD screens, plus role/session listings and account settings. Shared assets: css/crud.css and js/crud-list.js; the shared application shell opts in using ViewData["CrudPage"].

Directories use summary cards where relevant, green table headers, local search, explicit status/grade filters, six-item pagination and masked account password inputs. Class directories use cards. Desktop navigation defaults to an expandable icon rail; mobile navigation retains its drawer. Existing mutation routes, forms and anti-forgery tokens remain intact.

Validation: final Release publish passed. Eleven Admin routes and seven Teacher routes returned HTTP 200 with the expected CRUD shell using synthetic accounts in an isolated SQLite database. Teacher routes correctly denied the Admin-only test session before being retested as Teacher. Browser checks passed for name search, inactive/archived filtering, pagination and refresh retention, no-results state, create-dialog opening, password masking, mobile drawer/Escape, and no document overflow at 390px. No browser script errors were observed. Final compact/expanded navigation was checked after rebuilding. JavaScript syntax and git diff whitespace checks passed.

This pass did not rerun the full unit suite or submit every create/update/delete operation. No installed deployment or release installers were changed. The temporary test server was stopped. Local pagination operates on records returned by existing controllers; it does not introduce server-side pagination.
