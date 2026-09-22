# CAMS Diagrams

Design documentation for **CAMS — Computer Account Management System**: the laboratory monitoring and account system built for Pardo Elementary School.

Five draw.io drawings and six written documents. They describe the same system at four levels — what it *is*, what the **code** declares, what the **database** holds, and what a **person can do** with it — and every one of them is generated from the source rather than drawn by hand, so a change in the code shows up in the drawing instead of quietly drifting away from it.

Open any `.drawio` file at [app.diagrams.net](https://app.diagrams.net) with **File → Open From → Device**, or drag it onto the canvas.

---

## The drawings

| File | Notation | Contents |
| --- | --- | --- |
| [`CAMS-Use-Case-Diagram.drawio`](CAMS-Use-Case-Diagram.drawio) | UML use case | 37 module boxes, 197 use cases, 3 actors, one page |
| [`CAMS-Class-Model.drawio`](CAMS-Class-Model.drawio) | UML class | 23 classes, 119 attributes, 61 operations, 24 connectors |
| [`CAMS-Database-Schema.drawio`](CAMS-Database-Schema.drawio) | Crow's foot | All 28 tables, every column with its SQLite type |
| [`CAMS-Crowsfoot-ERD.drawio`](CAMS-Crowsfoot-ERD.drawio) | Crow's foot | The 15 tables the Chen diagram covers, 110 columns, plus a notes page |
| [`CAMS-Chen-ERD.drawio`](CAMS-Chen-ERD.drawio) | Chen | 11 entities, 15 relationships, conceptual |

### Which one answers which question

**“What things exist and how do they relate?”** → the **Chen diagram**. Entities as rectangles, relationships as diamonds, attributes as ovals, the identifier underlined. Cardinality and participation are taken from the database: a nullable foreign key is partial participation and a single line, a `NOT NULL` one is total and a double line — which is why *Attends* and *Logs status* are the only double lines in the drawing.

It deliberately names two or three attributes per entity. The full attribute list lives in the schema diagrams; a conceptual model that drew all 98 ovals would be unreadable.

**“What does the database actually store?”** → the **schema diagrams**. Real table names, every column with its storage type and nullability, `PK` / `FK` / `UK` / `UQ` marks. `CAMS-Database-Schema` covers all 28 tables; `CAMS-Crowsfoot-ERD` narrows to the 15 the Chen diagram describes so the two can be read side by side, and carries an *Integrity and Scope Notes* second page for the rules no connector can express.

**“What does the code declare?”** → the **class model**. Four layers left to right — the SignalR hub, the service interfaces, their implementations, the `DbContext`, then the domain classes — with attributes typed as C# declares them and associations taken from navigation properties.

**“What can a person do?”** → the **use case diagram**, and the written specifications beside it.

---

## The documents

| File | Contents |
| --- | --- |
| [`CAMS-Use-Case-Specifications.pdf`](CAMS-Use-Case-Specifications.pdf) | The written use cases, ready to read or print. 394 pages, US Letter, Times New Roman, double spaced, each module drawn with a numbered figure caption |
| [`CAMS-Use-Case-Specifications.docx`](CAMS-Use-Case-Specifications.docx) | The same document in Word, for pasting into a manuscript |
| [`Use-Case-Specifications.md`](Use-Case-Specifications.md) | The same content in Markdown, so it renders on GitHub and diffs cleanly |
| [`usecase-images/`](usecase-images) | The 37 module drawings the documents embed, one PNG per module |
| [`ERD.md`](ERD.md) | The entity model in Mermaid, and the notes explaining both ERDs |
| [`Use-Case-Diagram.md`](Use-Case-Diagram.md) | Scope, actor boundaries, and how the diagram was derived |
| [`SignalR-Message-Flow.md`](SignalR-Message-Flow.md) | How the server and the workstation clients talk to each other |
| [`Flowchart.md`](Flowchart.md) | The system flow end to end |
| [`Menu-Structure-Diagram.md`](Menu-Structure-Diagram.md) | The navigation tree behind each role |

Each specification follows the ten fields the course handout sets out: **use case name, purpose, actors, input parameters, output parameters, pre-condition, post-condition, successful scenario, exception scenario, additional remarks.** All three files carry the same 197 written use cases, laid out the way the course handout lays out its worked example: no tables, a bold field label ending in a colon, the value on the same line where it is short and an indented list where it is not, in Times New Roman twelve point, double spaced and justified on US Letter, which is how a thesis chapter is set. Each module is drawn first, with an italic numbered caption beneath the drawing - *Figure 3.1: System Use Case for Process Log In* - and the use cases inside that module follow. Actors are listed and marked primary or secondary.

The parts that can be got wrong are read out of the source rather than written from memory. Input parameters come from the action signature and its `[Bind]` list, so a specification cannot name a field the form does not submit. The pre-condition comes from the `[Authorize]` attribute, including the `[TeacherSharedAction]` marker. The antiforgery step appears only where the controller validates one, and a hub method is described as a SignalR relay rather than a form post.

---

## Three actors, and where the line between them falls

**Administrator**, **Teacher** and **Student**. Nothing else — the workstation client and the hosted background workers are parts of the system, not actors, so what they do is drawn as included behaviour of the case a person actually starts.

The division between the administrator and the teacher is read off the code rather than assumed. `AdminController` is `[Authorize(Roles = AdminOrTeacher)]`, and its authorization filter admits a teacher only to actions marked `[TeacherSharedAction]`. Fifty-six actions carry that attribute, so the teacher band repeats the whole shared administration surface — peer teacher accounts, student accounts, workstations, classes, rosters, restriction rules, lists, categories, session rules, and laboratory-wide pause, resume and end.

The administrator keeps what is not shared: administrator accounts, roles, LAN configuration, reports, audit and system logs, and everything in `AdminDatabaseController` and `AdminDeploymentController`.

> **One asymmetry looks like a mistake and is not.** The administrator can pause, resume and end a laboratory-wide session but cannot start one. `AdminController` exposes `PauseAllSessions`, `ResumeAllSessions` and `EndAllSessions` and no start; `GlobalStartSession` lives on `TeacherController`. The diagram follows the code.

---

## Reading the notation

**Use case diagram.** A plain line from an actor is an association. A dashed arrow with an open head marked `<<include>>` runs **from** the base case to behaviour it always performs. One marked `<<extend>>` runs the other way — **from** the optional case back **at** the base — because the base is complete and meaningful without it, and the extension is what only sometimes happens. Locking an account extends verifying credentials; it does not replace it.

Every use case is strict verb-noun: an imperative verb first, then the noun it acts on. Each one still maps to the function that implements it, and the module caption names the declaring type - an ellipse reading `CREATE STUDENT` is `AdminController.CreateStudent` - but where the identifier is a bare noun, a noun phrase, or puts its modifier in front of the verb, the caption supplies the word order a reader expects: `AdminController.Teachers` reads `VIEW TEACHERS`, `LabUtilization` reads `VIEW LAB UTILIZATION`, and `GlobalEndSession` reads `END LAB SESSIONS`. Where the identifier and the behaviour disagree the behaviour wins: `AdminController.DeleteComputer` archives the workstation rather than deleting it, so it reads `ARCHIVE COMPUTER`, and `PermanentlyDeleteComputer` is the one that reads `DELETE COMPUTER`. Captions run from two to four words; none is a single word, and none names a threading convention - the `*Async` service methods that used to appear as `<<include>>` of their own callers have been removed.

**Crow's foot.** The parent end is a double bar when the child's foreign key is `NOT NULL` and a bar with a circle when it is nullable. The child end is always a crow's foot with a circle, because no foreign key can oblige a parent to have children — a teacher with no classes yet is perfectly valid. Six of the twenty-five relationships have a mandatory parent end.

**Class model.** Three compartments per class, `+` public, `-` private, `#` protected. A dashed line with a hollow triangle is a realization; a dashed open arrow is a dependency; a solid line with multiplicities is an association taken from a navigation property. A leading slash marks a derived attribute — `/FullName` is computed from `FirstName` and `LastName`.

---

## How these are kept honest

Nothing here is hand-placed. The drawings are emitted from the source by generators, and then checked:

| Check | What it asserts |
| --- | --- |
| Structural | The XML parses in a real browser, no connector points at a shape that does not exist, no two shapes overlap |
| Coverage | Every controller action and hub method appears in the use case diagram — 180 examined, four excluded as error pages or SignalR lifecycle callbacks |
| Naming | All 156 distinct use case captions resolve to a real function in the source, and none is a single word |
| Cardinality | Every relationship in both ERDs matches the nullability of the foreign key behind it |
| Attributes | Every column in the schema diagrams and every attribute in the class model exists in the model, and none is missing |
| Arrows | Every `<<extend>>` terminates on its base case, every `<<include>>` on the included behaviour |
| Render | Each file is rendered through diagrams.net itself, because geometry checks cannot see text overflowing a shape |

Two consequences worth knowing. The generator **refuses to emit** a single-word caption or a connector that runs backwards, so a mistake fails the build rather than reaching the drawing. And where a diagram and the code disagree, the code wins — several corrections here began as an audit finding, not as a request.

### What the drawings do **not** prove

They reflect the schema as the **EF Core model declares it**, not as a running `CAMS.db` file holds it. The two should agree, because the server applies migrations on start, but no drawing here has been diffed against a live database.

One further detail matters when reading types: SQLite has only `INTEGER`, `TEXT`, `REAL`, `BLOB` and `NUMERIC`. A column shown as `TEXT(100)` takes its length from `HasMaxLength(100)` in the model, and it is **Entity Framework** that enforces it, not the database engine.
