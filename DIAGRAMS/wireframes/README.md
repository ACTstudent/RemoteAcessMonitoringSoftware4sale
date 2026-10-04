# CAMS wireframes

Low-fidelity wireframes of seven CAMS screens, drawn from the real server UI. The public portal (`portal/index.html`) shows them in its hero carousel, its role cards and its download cards.

| File | Screen |
| --- | --- |
| [`dashboard.svg`](dashboard.svg) | Teacher dashboard: session, student and status counts above six laboratory shortcuts |
| [`monitoring.svg`](monitoring.svg) | Live monitoring wall: bulk warn, broadcast, lock and log out over a grid of student stations |
| [`sessions.svg`](sessions.svg) | Session control: resume, pause and end actions, counts, and the session table |
| [`deployment.svg`](deployment.svg) | Deployment Hub: readiness banner, client package details, active HTTPS certificate |
| [`signin.svg`](signin.svg) | CAMS Portal sign-in |
| [`utilization.svg`](utilization.svg) | Lab utilization: filters, usage figures, workstation chart and daily load |
| [`admin.svg`](admin.svg) | Administrator setup checklist and global operations dashboard |

## Editing

All seven are drawn by [`draw_wireframes.py`](draw_wireframes.py), so change the script rather than the SVG files:

```bash
# Rewrite the standalone SVGs in this folder
python DIAGRAMS/wireframes/draw_wireframes.py --svg DIAGRAMS/wireframes

# Print the <symbol> block to paste into portal/index.html (between the
# "Low-fidelity wireframes" comment and the closing </svg> of the sprite)
python DIAGRAMS/wireframes/draw_wireframes.py
```

The standalone files use the light palette. In the portal the same shapes are coloured by the `.wf-*` rules in `portal/assets/styles.css`, which is how they follow light and dark mode.
