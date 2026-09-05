# Defects — run 20260905-125626-b5441f1

One defect was found. It is reproducible and affects every authenticated page.

No production code was changed to record it. Suggested fixes are described but
not applied.

---

## DEF-001 — The content security policy blocks the interface font on every page

| Field | Value |
| --- | --- |
| Severity | Medium |
| Affected source | `Server/wwwroot/css/site.css` line 1, `Server/Program.cs` response-header middleware |
| Environment | Chrome (headless, 1440x900) against the Release server on `https://localhost:5000` |
| Cases | UI-01, UI-02, UI-03 |
| Evidence | `ui-sweep.csv` — 34 of 34 pages report the violation |

### What happens

`site.css` opens with a remote import:

```css
@import url('https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@400;500;600;700;800&family=Inter:wght@400;500;600;700&display=swap');
```

The response header sent by the server is:

```
Content-Security-Policy: default-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data: ...
```

`style-src` does not permit `fonts.googleapis.com`, and `font-src` does not
permit `fonts.gstatic.com`, so the browser refuses the stylesheet and logs:

```
Loading the stylesheet 'https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans...'
violates the following Content Security Policy directive: "style-src 'self' 'unsafe-inline'"
```

### Reproduction

1. Start the server and sign in as any role.
2. Open any page and inspect the browser console.
3. Observe the violation on every page load.

Or run the sweep used here:

```
node ui-sweep.js out.csv
```

### Expected versus actual

Expected: the interface renders in Plus Jakarta Sans, with a clean console.

Actual: the font request is blocked before it is made. The design token
`--font-family: 'Plus Jakarta Sans', 'Inter', -apple-system, ...` falls through
to the local fallback, so the product never displays its intended typeface, and
every page logs a console error.

### Impact

Cosmetic rather than functional: the interface still renders and remains usable
because the fallback stack resolves. The practical effects are that the chosen
typography never appears, and that a genuine console error is present on every
page, which makes real errors harder to notice during support.

Worth noting for this product specifically: CAMS is deployed on a school LAN
that may have no route to the internet. Even with the policy relaxed, a remote
font would fail to load in that environment, so widening the policy alone would
not reliably deliver the font.

### Suggested fix, not applied

Preferred: download the two families, place the files under
`wwwroot/fonts/`, replace the `@import` with local `@font-face` rules, and leave
the policy as it is. This suits a LAN deployment, removes the third-party
request, and keeps the strict policy intact.

Alternative, only if the deployment is known to have internet access: add
`https://fonts.googleapis.com` to `style-src` and `https://fonts.gstatic.com` to
`font-src`. This weakens the policy and still fails when the lab is offline.

Supporting observation: `portal/index.html` already contains no remote font
references, and its README states the portal ships no CDN assets. The
application stylesheet is the sole remaining external dependency, so self
hosting would make the two consistent.
