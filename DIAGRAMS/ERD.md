# Entity Relationship Diagram (ERD)

```mermaid
erDiagram
    ADMIN {
        int AdminID PK
        string FirstName
        string LastName
        string Email
        string PassWord
        string ContactNumber
        string Status
    }

    TEACHER {
        int TeacherID PK
        string FirstName
        string LastName
        string Email
        string PassWord
        string ContactNumber
        string Status
    }

    STUDENT {
        int StudentID PK
        string FirstName
        string LastName
        string ContactNumber
        string GradeSection
        string Status
        int TeacherID FK
    }

    COMPUTER {
        int ComputerID PK
        string LaboratoryStation
        string Status
    }

    ADMIN ||--o{ TEACHER : manages
    ADMIN ||--o{ STUDENT : manages
    ADMIN ||--o{ COMPUTER : manages
    TEACHER ||--o{ STUDENT : manages
    TEACHER ||--o{ COMPUTER : manages
```

## Relationships

| Relationship | Cardinality | Meaning |
| --- | --- | --- |
| ADMIN → TEACHER | 1 : N | An admin manages many teacher accounts. |
| ADMIN → STUDENT | 1 : N | An admin manages many student profiles. |
| ADMIN → COMPUTER | 1 : N | An admin manages many computer profiles. |
| TEACHER → STUDENT | 1 : N | A teacher manages many student profiles. |
| TEACHER → COMPUTER | 1 : N | A teacher manages many computer profiles. |

## Notes

Reconstructed from the draw.io ERD image via OCR. "LaboratyStation" was corrected to `LaboratoryStation`.
