"""Draws the low-fidelity wireframes of seven CAMS screens (viewBox 400x260).

    python draw_wireframes.py            prints the <symbol>s pasted into portal/index.html
    python draw_wireframes.py --svg DIR  writes one standalone SVG per screen into DIR

Inside the portal the shapes are styled by the .wf-* rules in portal/assets/styles.css, so they
follow light and dark mode. The standalone files carry the light palette in their own <style>.

Classes: wf-bg (canvas), wf-panel (sidebar/cards), wf-line (strokes), wf-bar (text placeholders),
wf-ink (strong text/solid), wf-acc (single accent per screen), wf-t / wf-tb (labels).
"""

import os
import sys

W, H = 400, 260
out = []


def r(x, y, w, h, cls, rx=4):
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" class="{cls}"/>'


def bar(x, y, w, cls="wf-bar", h=4):
    return r(x, y, w, h, cls, rx=h / 2)


def t(x, y, s, cls="wf-t", anchor="start"):
    a = '' if anchor == "start" else f' text-anchor="{anchor}"'
    return f'<text x="{x}" y="{y}" class="{cls}"{a}>{s}</text>'


def circle(cx, cy, rad, cls):
    return f'<circle cx="{cx}" cy="{cy}" r="{rad}" class="{cls}"/>'


def frame():
    return r(0.75, 0.75, W - 1.5, H - 1.5, "wf-bg wf-line", rx=10)


def shell(active=1, items=7):
    """Sidebar with logo and nav, plus a top bar with the signed-in user."""
    s = [f'<path d="M10.75 0.75 H84 V259.25 H10.75 A10 10 0 0 1 0.75 249.25 V10.75 A10 10 0 0 1 10.75 0.75 Z" class="wf-panel"/>',
         '<line x1="84" y1="0.75" x2="84" y2="259.25" class="wf-line"/>',
         circle(42, 22, 9, "wf-line wf-bg"), bar(24, 38, 36, "wf-ink", 3), bar(28, 45, 28)]
    y = 62
    for i in range(items):
        if i == active:
            s.append(r(8, y - 5, 68, 14, "wf-acc", rx=4))
            s.append(bar(26, y, 34, "wf-on-acc", 4))
            s.append(r(14, y - 1, 6, 6, "wf-on-acc", rx=1.5))
        else:
            s.append(r(14, y - 1, 6, 6, "wf-bar", rx=1.5))
            s.append(bar(26, y, 30 + (i * 7) % 14))
        y += 19
    s += ['<line x1="84" y1="24" x2="399.25" y2="24" class="wf-line"/>',
          bar(98, 10, 10, "wf-ink", 2), bar(98, 14, 10, "wf-ink", 2),
          bar(300, 9, 36, "wf-ink", 3), bar(306, 15, 24), circle(350, 12, 6, "wf-line wf-bg"),
          r(362, 6, 28, 12, "wf-line wf-bg", rx=6)]
    return s


def card(x, y, w, h):
    return r(x, y, w, h, "wf-panel wf-line", rx=6)


def stat(x, y, w, label, value="0"):
    return [card(x, y, w, 40), t(x + 8, y + 13, label, "wf-t"), t(x + 8, y + 32, value, "wf-tb wf-big"),
            circle(x + w - 13, y + 13, 6, "wf-line wf-bg")]


def pill(x, y, w, label, cls="wf-line wf-bg", tcls="wf-t"):
    return [r(x, y, w, 14, cls, rx=7), t(x + w / 2, y + 10, label, tcls, "middle")]


screens = []


def symbol(sid, parts):
    screens.append((sid, parts))
    out.append(f'    <symbol id="{sid}" viewBox="0 0 {W} {H}">' + "".join(parts) + "</symbol>")


# 1. Teacher dashboard
p = [frame()] + shell(active=0)
p += [t(98, 44, "Teacher Dashboard", "wf-tb wf-h"), bar(98, 50, 120)]
p += stat(98, 60, 92, "Running sessions") + stat(196, 60, 92, "Students online") + stat(294, 60, 92, "System status", "Active")
p += [t(98, 118, "Laboratory operations", "wf-tb")]
labels = ["Live monitoring", "Session control", "Student profiles", "Workstation grid", "Restrictions", "Records"]
for i, lab in enumerate(labels):
    x, y = 98 + (i % 3) * 98, 126 + (i // 3) * 64
    p += [card(x, y, 92, 58), r(x + 7, y + 7, 14, 14, "wf-bg wf-line", rx=3), t(x + 26, y + 14, lab, "wf-t"), bar(x + 26, y + 18, 40),
          r(x + 7, y + 38, 78, 12, "wf-acc" if i == 0 else "wf-ink", rx=6)]
symbol("wf-dashboard", p)

# 2. Live monitoring wall
p = [frame()] + shell(active=2)
p += [t(98, 44, "Laboratory Control Panel", "wf-tb wf-h"), bar(98, 50, 110)]
for i, lab in enumerate(["Units online", "Active", "Idle", "Alerts"]):
    p += [card(98 + i * 73, 58, 67, 26), t(104 + i * 73, 69, lab, "wf-t"), bar(104 + i * 73, 75, 16, "wf-ink", 4)]
p += [r(98, 92, 288, 160, "wf-bg wf-line", rx=6), t(106, 106, "Live station monitor", "wf-tb")]
p += [r(106, 112, 70, 13, "wf-panel wf-line", rx=6.5)]
x = 182
for lab, w in [("Warn all", 42), ("Broadcast", 46), ("Lock visible", 52), ("Log out", 40)]:
    p += pill(x, 111.5, w, lab)
    x += w + 5
for i in range(8):
    cx, cy = 106 + (i % 4) * 70, 132 + (i // 4) * 58
    sel = i == 1
    p += [r(cx, cy, 64, 52, "wf-panel " + ("wf-acc-line" if sel else "wf-line"), rx=5),
          r(cx + 5, cy + 5, 54, 30, "wf-fill", rx=3),
          f'<path d="M{cx + 5} {cy + 35} L{cx + 25} {cy + 17} L{cx + 38} {cy + 28} L{cx + 46} {cy + 22} L{cx + 59} {cy + 35}" class="wf-stroke"/>',
          t(cx + 5, cy + 46, f"PC-0{i + 1}", "wf-t"), circle(cx + 56, cy + 43, 3, "wf-acc" if i % 3 != 2 else "wf-bar")]
symbol("wf-monitoring", p)

# 3. Session control
p = [frame()] + shell(active=1)
p += [t(98, 44, "Session Control", "wf-tb wf-h"), bar(98, 50, 150)]
p += pill(98, 58, 66, "Resume all", "wf-acc", "wf-t wf-on-acc-t") + pill(170, 58, 56, "Pause all") + pill(232, 58, 76, "End &amp; restart")
p += stat(98, 80, 92, "Running") + stat(196, 80, 92, "Paused") + stat(294, 80, 92, "Command logs")
p += [r(98, 128, 288, 124, "wf-bg wf-line", rx=6), r(106, 136, 110, 12, "wf-panel wf-line", rx=6), r(320, 136, 58, 12, "wf-ink", rx=6)]
p += [r(106, 154, 272, 14, "wf-panel", rx=3)]
cols = [("Session", 110), ("Student", 160), ("Station", 210), ("Started", 258), ("Status", 306), ("", 350)]
for lab, x in cols:
    p.append(t(x, 164, lab, "wf-t"))
for row in range(4):
    y = 180 + row * 16
    p += [bar(110, y, 30), bar(160, y, 36), bar(210, y, 26), bar(258, y, 32),
          r(306, y - 3, 28, 10, "wf-acc" if row < 2 else "wf-fill", rx=5), bar(350, y, 18, "wf-ink")]
    p.append(f'<line x1="106" y1="{y + 9}" x2="378" y2="{y + 9}" class="wf-line"/>')
symbol("wf-sessions", p)

# 4. Deployment hub
p = [frame()] + shell(active=5)
p += [t(98, 44, "Deployment Hub", "wf-tb wf-h"), bar(98, 50, 130)]
p += [r(98, 58, 288, 18, "wf-warn", rx=5), r(105, 63, 8, 8, "wf-ink", rx=2), bar(118, 65, 160, "wf-ink")]
p += [card(98, 84, 168, 168), t(106, 98, "Client package", "wf-tb")]
for i, lab in enumerate(["Product", "Client version", "Server version", "Installer size", "SHA-256"]):
    y = 114 + i * 20
    p += [t(106, y, lab, "wf-t"), bar(186, y - 4, 50 if i != 4 else 70, "wf-ink" if i != 4 else "wf-mono"),
          f'<line x1="106" y1="{y + 7}" x2="258" y2="{y + 7}" class="wf-line"/>']
p += pill(106, 222, 44, "Installer", "wf-acc", "wf-t wf-on-acc-t") + pill(155, 222, 44, "Manifest") + pill(204, 222, 52, "Root CER")
p += [card(272, 84, 114, 168), t(280, 98, "HTTPS certificate", "wf-tb"), r(280, 104, 52, 11, "wf-bg wf-line", rx=5.5)]
for i, lab in enumerate(["Subject", "Thumbprint", "SHA-256", "Expires"]):
    y = 132 + i * 28
    p += [t(280, y, lab, "wf-t"), bar(280, y + 6, 92 if i in (1, 2) else 60, "wf-mono" if i in (1, 2) else "wf-ink")]
symbol("wf-deployment", p)

# 5. Sign-in
p = [frame(), r(0.75, 0.75, W - 1.5, H - 1.5, "wf-panel", rx=10)]
p += [r(130, 22, 140, 216, "wf-bg wf-line", rx=10), f'<path d="M140 22.5 H260 A9.5 9.5 0 0 1 269.5 32 V92 H130.5 V32 A9.5 9.5 0 0 1 140 22.5 Z" class="wf-panel"/>',
      '<line x1="130.5" y1="92" x2="269.5" y2="92" class="wf-line"/>',
      circle(200, 50, 15, "wf-bg wf-line"), circle(200, 50, 8, "wf-fill"), t(200, 78, "CAMS Portal", "wf-tb", "middle"), bar(170, 84, 60)]
p += [t(142, 108, "Username", "wf-t"), r(142, 113, 116, 18, "wf-bg wf-acc-line", rx=5), bar(149, 120, 70),
      t(142, 146, "Password", "wf-t"), r(142, 151, 116, 18, "wf-bg wf-line", rx=5), bar(149, 158, 50), circle(249, 160, 3, "wf-bar"),
      r(142, 180, 116, 18, "wf-acc", rx=9), t(200, 192, "Sign in", "wf-t wf-on-acc-t", "middle"), bar(152, 210, 96), bar(170, 218, 60)]
p += [bar(160, 246, 80)]
symbol("wf-signin", p)

# 6. Lab utilization
p = [frame()] + shell(active=3)
p += [t(98, 44, "Lab Utilization", "wf-tb wf-h"), bar(98, 50, 170)]
for i, (lab, w) in enumerate([("From", 50), ("To", 50), ("Station", 62), ("Class", 50)]):
    x = 98 + sum([50, 50, 62, 50][:i]) + i * 6
    p += [t(x, 66, lab, "wf-t"), r(x, 70, w, 13, "wf-bg wf-line", rx=4)]
p += [r(340, 70, 46, 13, "wf-acc", rx=6.5), t(363, 79.5, "Apply", "wf-t wf-on-acc-t", "middle")]
for i, lab in enumerate(["Utilization", "Active time", "Idle share", "Coverage"]):
    p += [card(98 + i * 73, 90, 67, 34), t(104 + i * 73, 102, lab, "wf-t"), bar(104 + i * 73, 109, 28, "wf-ink", 6)]
p += [card(98, 132, 186, 120), t(106, 146, "Workstation utilization", "wf-tb")]
heights = [52, 74, 38, 88, 60, 46, 80, 30]
for i, hgt in enumerate(heights):
    x = 110 + i * 21
    p += [r(x, 240 - hgt, 12, hgt, "wf-acc" if i == 3 else "wf-fill", rx=2)]
p += ['<line x1="106" y1="240.5" x2="276" y2="240.5" class="wf-line"/>']
p += [card(290, 132, 96, 120), t(298, 146, "Daily load", "wf-tb")]
for i in range(4):
    y = 160 + i * 23
    p += [bar(298, y, 40, "wf-ink"), r(358, y - 4, 20, 10, "wf-bg wf-line", rx=5), r(298, y + 8, 80, 3, "wf-fill", rx=1.5), r(298, y + 8, 20 + i * 12, 3, "wf-acc", rx=1.5)]
symbol("wf-utilization", p)

# 7. Admin setup checklist
p = [frame()] + shell(active=0)
p += [card(98, 34, 288, 112), t(108, 50, "Finish setting up CAMS", "wf-tb wf-h"), t(108, 62, "4 of 6 done", "wf-t")]
done = [True, True, True, False, True, False]
names = ["Add a teacher", "Create a class", "Register a workstation", "Stage the client installer", "Publish the root certificate", "Connect the first workstation"]
for i, (ok, lab) in enumerate(zip(done, names)):
    x, y = 108 + (i // 3) * 142, 80 + (i % 3) * 20
    if ok:
        p += [circle(x + 5, y - 3, 5, "wf-acc"), f'<path d="M{x + 2.5} {y - 3} l2 2 l3.5 -4" class="wf-check"/>']
    else:
        p += [circle(x + 5, y - 3, 5, "wf-bg wf-line")]
    p.append(t(x + 15, y, lab, "wf-t" if ok else "wf-tb"))
p += [t(98, 164, "Global operations dashboard", "wf-tb wf-h"), bar(98, 170, 140)]
p += [card(98, 180, 288, 30), t(106, 193, "Lab-wide session control", "wf-tb"), bar(106, 199, 160)]
p += stat(98, 216, 67, "Students") + stat(171, 216, 67, "Teachers") + stat(244, 216, 67, "Stations") + stat(317, 216, 69, "Sessions")
symbol("wf-admin", p)

# Light palette for the standalone files, matching the portal's light-mode --wf-* tokens.
STANDALONE_STYLE = """
.wf-bg{fill:#fdfdfc}.wf-panel{fill:#f4f3f0}.wf-line{stroke:#d6d3cd;stroke-width:1}.wf-bar{fill:#dedbd5}
.wf-fill{fill:#e9e7e2}.wf-ink{fill:#2b2a28}.wf-acc{fill:#17703a}.wf-on-acc{fill:#fdfdfc}
.wf-acc-line{stroke:#17703a;stroke-width:1.5}.wf-mono{fill:#17703a;opacity:.55}.wf-warn{fill:#f5e6bf}
.wf-stroke{fill:none;stroke:#d6d3cd;stroke-width:1.2}
.wf-check{fill:none;stroke:#fdfdfc;stroke-width:1.4;stroke-linecap:round;stroke-linejoin:round}
.wf-t{fill:#6f6c66;font-family:Manrope,'Segoe UI',system-ui,sans-serif;font-size:7px;font-weight:500}
.wf-tb{fill:#2b2a28;font-family:Manrope,'Segoe UI',system-ui,sans-serif;font-size:7.5px;font-weight:700}
.wf-h{font-size:11px;letter-spacing:-.02em}.wf-big{font-size:13px}.wf-on-acc-t{fill:#fdfdfc;font-weight:700}
"""


def write_standalone(directory):
    os.makedirs(directory, exist_ok=True)
    for sid, parts in screens:
        name = sid.removeprefix("wf-") + ".svg"
        svg = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W * 2}" height="{H * 2}">'
               f"<style>{STANDALONE_STYLE}</style>" + "".join(parts) + "</svg>\n")
        with open(os.path.join(directory, name), "w", encoding="utf-8") as f:
            f.write(svg)
        print("wrote", os.path.join(directory, name))


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--svg":
        write_standalone(sys.argv[2])
    else:
        print("\n".join(out))
