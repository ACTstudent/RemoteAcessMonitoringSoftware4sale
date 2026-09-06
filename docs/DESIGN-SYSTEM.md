# CAMS design system

What the interface is made of, and which variant to reach for. Written from what
the code actually does — every value here was read out of
`Server/wwwroot/css/site.css` or measured in the running page, not proposed.

Contrast figures are against the surface the element is actually used on. WCAG AA
for body text is 4.5:1.

## Tokens

All in `:root` in `site.css`. Use the token, not the literal — that is the whole
point of having them.

### Colour

| Token | Value | Used for |
| --- | --- | --- |
| `--sidebar-bg` | `#16401F` | The dark brand bar and any surface sharing it (`.surface-brand`) |
| `--sidebar-active-bg` | `#1D5C2E` | The current sidebar link |
| `--primary-color` | `#16401F` | Brand |
| `--primary-hover` | `#1D5C2E` | Brand, hovered |
| `--accent-emerald` | `#17803A` | The primary action colour |
| `--accent-emerald-hover` | `#136B31` | Primary action, hovered |
| `--body-bg` | `#FAF8F3` | Page background |
| `--card-bg` | `#FFFFFF` | Cards and tables |
| `--card-border` | `#E7E2D9` | Card and table edges |
| `--text-main` | `#1C1917` | Body text |
| `--text-muted` | `#6F6861` | Secondary text |

Status colours, all measured on white:

| Token | Value | Contrast | Meaning |
| --- | --- | --- | --- |
| `--cams-primary` | `#1D5C2E` | 8.01:1 | Brand, sessions |
| `--cams-success` | `#17803A` | 5.02:1 | Active, healthy |
| `--cams-info` | `#8A6117` | — | Informational. A warm ochre deliberately unlike the danger red |
| `--cams-warning` | `#B45309` | 5.02:1 | Needs attention |
| `--cams-danger` | `#B91C1C` | 6.47:1 | Failed, destructive |

Status is never carried by colour alone — each badge and pill also carries text.

### Radius

Four steps and a pill. The stylesheet previously held ten ad-hoc values chosen a
rule at a time.

| Token | Value | Used for |
| --- | --- | --- |
| `--radius-sm` | `8px` | Inputs, small chips |
| `--radius-md` | `12px` | Buttons, inner panels |
| `--radius-lg` | `16px` | Cards. `--card-border-radius` is the same value |
| `--radius-xl` | `20px` | Large surfaces, modals |
| `--radius-pill` | `999px` | Pill buttons and badges |

### Spacing

`--space-1` `0.25rem` · `--space-2` `0.5rem` · `--space-3` `0.75rem` ·
`--space-4` `1rem` · `--space-5` `1.5rem` · `--space-6` `2rem`.

### Focus

| Token | Value |
| --- | --- |
| `--focus-ring-color` | `#1D5C2E` |
| `--focus-ring-width` | `3px` |
| `--focus-ring-offset` | `2px` |

One ring on everything focusable, via `:focus-visible` so a mouse click is left
alone. On the dark sidebar it switches to `#BBF3C6`, because forest green on
forest green is not an indicator. The outline is excluded from the sidebar's
transition so it appears immediately rather than fading in.

## Buttons

One primary action per task area. If two things on a page are emerald, one of
them is wrong.

| Class | Use | Count in views |
| --- | --- | --- |
| `btn-emerald` | **The** primary action of the page or dialog | 78 |
| `btn-outline-emerald` | A secondary action that is still on the happy path | 34 |
| `btn-outline-secondary` | Neutral: cancel, reset, back, export | 51 |
| `btn-light` | Low-emphasis, usually inside a table row | 35 |
| `btn-outline-danger` | Destructive, needing confirmation | 26 |
| `btn-outline-warning` | Interrupting but reversible, e.g. unlock | 9 |
| `btn-outline-success` / `btn-success` | Confirming a positive state change | 12 |

Destructive buttons — restart, shutdown, force logout — carry
`data-confirm` so `window.camsConfirm` asks first. Lock and unlock deliberately
do not: they are immediately reversible.

Buttons are pill-shaped (`rounded-pill`) except inside compact table rows.

## Forms

- Every control has an accessible name, by `label[for]`/`id`. Rows that repeat or
  are cloned by script use `aria-label` instead, because an `id` would be
  duplicated the moment a row is added.
- A placeholder is never the only name — it disappears as soon as the field has
  content, which is exactly when someone checking their work needs it.
- `.form-inline-action` is for a form that exists only to carry a button and must
  add no layout of its own.
- State-changing forms are guarded against a second submission by the repeat
  submit handler in `site.js`. GET forms are filters and are left alone.

## Tables

- `.custom-table` inside `.table-responsive`, so a wide table scrolls rather than
  pushing the page sideways.
- Every table has header cells. A table without `th` is a defect, not a style.
- Empty tables use `.empty-state`: an icon above the sentence, so nothing-to-show
  reads as deliberate rather than as a page that failed to load. Pick the icon
  for what is missing — a mortarboard for students, a workstation for computers.
- Paged lists use the shared `_Pager` partial. An unavailable direction renders
  as a `span`, never a link with `.disabled` — that class stops a mouse and not
  the keyboard.

## Status and feedback

| Pattern | Where | Behaviour |
| --- | --- | --- |
| `.badge-active` / `.badge-emerald` | Session and workstation state | Colour plus text |
| `window.CamsToast` | Anything that happened and needs no answer | Fades on its own; pauses while hovered |
| `window.camsConfirm` | Anything destructive | Modal; the action does not run until confirmed |
| `#connectionStatus` | The shared header | Hidden while healthy. "Reconnecting…" while retrying, "Offline — refresh to reconnect" once SignalR gives up. `role="status"`, `aria-live="polite"` |

The connection indicator is the one piece of state that is *absent* when things
are working. Everything else appears on the event it describes.

## Page shell

One layout, `Views/Shared/_AppLayout.cshtml`, for all three portals. What differs
is the navigation model — see `Services/NavigationBuilder.cs`. Adding a portal
link is a line there, not a change to markup.

Every page therefore gets the skip link, the `main` landmark, the same identity
block and the connection indicator without doing anything.

A sidebar link is current when **controller and action** both match. Matching on
the action alone is what made `/Admin/Students` highlight the teacher's own
Students link — both actions carry that name.

## What this document does not cover

- The WinForms client's own controls. Its palette mirrors these tokens and names
  the correspondence in `Client/MainForm.cs`, but its layout and control set are
  its own.
- The public portal under `portal/`, which is a static page and does not use
  `site.css`.
- Narrow-viewport and 200%-zoom behaviour, which is UX-06's remaining half and
  has not been verified.
