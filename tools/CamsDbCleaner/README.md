# CAMS database history cleaner

Clears the timestamped history tables from a CAMS database. Accounts, classes,
rosters, workstations and policy rules are left untouched.

## When this is needed

Before v2.11.2 the server wrote some timestamps in local time and others in UTC.
Rows created under the old build therefore hold a mix of both. New rows are
consistent, but the old ones render shifted by the machine's UTC offset and sort
against new rows as if they were newer. That history cannot be corrected
reliably, because the old writes were themselves mixed and no marker records
which convention a given row used. Clearing it is the dependable fix.

## Usage

The tool reports by default and changes nothing without `--apply`.

```
# See exactly what would be removed
dotnet run --project tools/CamsDbCleaner -- "C:\path\to\CAMS.db"

# Remove it
dotnet run --project tools/CamsDbCleaner -- "C:\path\to\CAMS.db" --apply
```

## Before running against a live server

1. **Stop the CAMS server.** SQLite will not allow the writes while it holds the
   database open.
2. **Copy `CAMS.db` somewhere safe.** The deletion is irreversible.

## What it does

Cleared: activity events, audit logs, system logs, usage and website usage logs,
browser monitoring records, monitoring alerts, remote command logs, remote
control sessions, connection logs, idle intervals, computer status history, lab
sessions and notifications.

Preserved: admins, teachers, students, classes, class rosters, computers,
session rules, roles, restriction rules, blacklist and category tables, and the
LAN configuration.

Deletes run in one transaction. Any workstation left marked "In Use" by a
cleared lab session is returned to "Available" or "Assigned", and the database
is vacuumed afterwards.
