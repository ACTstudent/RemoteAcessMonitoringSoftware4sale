# CAMS Major Entity Relationship Diagram

This readable ERD shows the principal persisted entities and enforced EF Core relationships in CAMS Computer Account Management System. It intentionally omits many scalar telemetry fields and supporting category/legacy tables. Passwords are stored only as `PasswordHash` values.

```mermaid
erDiagram
    ADMIN {
        int Id PK
        string Username UK
        string PasswordHash
        string FullName
        bool IsActive
        datetime LockoutEndUtc
    }

    TEACHER {
        int TeacherId PK
        string Username
        string PasswordHash
        string Status
        datetime LockoutEndUtc
    }

    STUDENT {
        int Id PK
        string StudentNumber UK
        string Username UK
        string PasswordHash
        string Status
        int ClassId FK
        int AdviserId FK
    }

    CLASS {
        int ClassId PK
        string ClassName
        string AcademicYear
        string Status
        bool IsArchived
        int TeacherId FK
    }

    CLASS_STUDENT {
        int ClassStudentId PK
        int ClassId FK
        int StudentId FK
        datetime EnrolledAt
    }

    COMPUTER {
        int ComputerId PK
        string LaboratoryStation UK
        string Status
        string AssignedTo UK
    }

    COMPUTER_STATUS_HISTORY {
        int ComputerStatusHistoryId PK
        int ComputerId FK
        string Status
        datetime ChangedAt
    }

    SESSION_RULE {
        int SessionRuleId PK
        string Name
        int MaxDurationMinutes
        bool AllowPause
        bool AllowRemoteControl
        bool IsDefault
    }

    LAB_SESSION {
        int Id PK
        int StudentId FK
        int TeacherId FK
        int ComputerId FK
        int SessionRuleId FK
        string Status
        datetime StartTime
        datetime EndTime
        bool IsActive
    }

    RESTRICTION_RULE {
        int RestrictionRuleId PK
        int TeacherId FK
        string RuleType
        string Target
        string Mode
        bool IsGlobal
        bool IsActive
    }

    USAGE_LOG {
        int UsageLogId PK
        int StudentId FK
        string PcName
        string AppName
        datetime Timestamp
    }

    ROLE {
        int RoleId PK
        string Name
        string Description
    }

    PERMISSION {
        int PermissionId PK
        string Name
    }

    TEACHER o|--o{ CLASS : assigned_to
    TEACHER o|--o{ STUDENT : advises
    CLASS o|--o{ STUDENT : primary_class
    CLASS ||--o{ CLASS_STUDENT : has_membership
    STUDENT ||--o{ CLASS_STUDENT : enrolled_through
    STUDENT ||--o{ LAB_SESSION : attends
    TEACHER o|--o{ LAB_SESSION : owns
    COMPUTER o|--o{ LAB_SESSION : hosts
    SESSION_RULE o|--o{ LAB_SESSION : governs
    COMPUTER ||--o{ COMPUTER_STATUS_HISTORY : records
    TEACHER o|--o{ RESTRICTION_RULE : owns_scoped_rule
    STUDENT o|--o{ USAGE_LOG : produces
    ROLE }o--o{ PERMISSION : metadata_links
```

## Integrity And Scope Notes

- `Admin`, `Teacher`, and `Student` are separate account tables and all store `PasswordHash`, failed-attempt, and lockout state. No plain `Password` field exists in the current account models.
- Application roles are fixed. `Role`, `Permission`, and `RolePermissions` retain seeded metadata for display; they do not provide configurable runtime RBAC.
- `Student.ClassId` is the primary class association. `ClassStudent` preserves explicit roster membership and has a unique `(ClassId, StudentId)` pair.
- `Computer.AssignedTo` stores the assigned student ID as text in the current schema. It is unique when present but is not an EF foreign key to `Student`; assignment integrity is enforced by application logic.
- Unique filtered indexes permit at most one active `LabSession` per student and at most one active `LabSession` per computer.
- `Computer.LaboratoryStation` is case-insensitively unique. `Computer.AssignedTo` is unique when non-null.
- A global `RestrictionRule` has no teacher owner. A teacher-owned rule has `TeacherId` and is applied only in that teacher's active-session scope.
- Monitoring alerts, browser records, activity events, idle intervals, remote-control sessions, and remote-command logs correlate to students, PCs, teachers, or connections through scalar identifiers in the current models. They are intentionally not drawn as enforced foreign-key relationships.
- Audit logs, system logs, notifications, website usage, blacklist/category records, and detected LAN configuration records are supporting entities omitted to keep the diagram readable.
