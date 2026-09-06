// AUTH-02, anonymous row: every controller action at this commit is called with
// no session at all. Nothing may answer with portal content, and no POST may
// change anything. Runs against the isolated server, not the user's own.
const fs = require('fs');
require('./config').acceptSelfSignedCertificate();

const BASE = process.argv[2] || 'https://localhost:5100';
const routes = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
const outCsv = process.argv[4];

// A response that lands on the login page, or refuses outright, is correct.
// A 200 carrying a portal layout is the failure this sweep is looking for.
const LOGIN_MARKERS = [/name="?Username"?/i, /Account\/Login/i, /<form[^>]*login/i];
const PORTAL_MARKERS = [/Teacher\/Dashboard/i, /Admin\/Students/i, /sidebar/i, /nav-link/i];

function classify(status, finalUrl, body) {
  if (status === 401 || status === 403) return { ok: true, note: 'refused ' + status };
  if (status === 400) return { ok: true, note: 'rejected 400 (validation/antiforgery)' };
  if (status === 405) return { ok: true, note: 'method not allowed' };
  if (status === 404) return { ok: true, note: 'not routed' };
  // A JSON API refusing a form-encoded body never reached its handler at all.
  if (status === 415) return { ok: true, note: 'media type refused before the handler' };
  if (/\/Account\/Login/i.test(finalUrl)) return { ok: true, note: 'redirected to login' };
  // Logout sends an anonymous caller to "/", which renders that same login view.
  if (/<title>\s*Login\b/i.test(body)) return { ok: true, note: 'login page returned' };
  if (status >= 500) return { ok: false, note: 'server error ' + status };
  if (status === 200) {
    if (LOGIN_MARKERS.some(r => r.test(body)) && !PORTAL_MARKERS.some(r => r.test(body)))
      return { ok: true, note: 'login page returned' };
    if (body.trim() === '' || body.trim() === '{}') return { ok: true, note: 'empty payload' };
    return { ok: false, note: '200 with ' + body.length + ' bytes of content' };
  }
  return { ok: false, note: 'unexpected ' + status + ' -> ' + finalUrl };
}

(async () => {
  const rows = [['Controller', 'Action', 'Method', 'Url', 'Status', 'FinalUrl', 'Verdict', 'Note']];
  let pass = 0, fail = 0;
  const failures = [];

  for (const r of routes) {
    let status = 0, finalUrl = '', body = '';
    try {
      const res = await fetch(BASE + r.url, {
        method: r.method,
        redirect: 'follow',
        headers: r.method === 'POST' ? { 'content-type': 'application/x-www-form-urlencoded' } : {},
        body: r.method === 'POST' ? 'id=1&probe=anonymous' : undefined
      });
      status = res.status;
      finalUrl = res.url;
      body = await res.text();
    } catch (e) {
      status = -1; finalUrl = ''; body = 'transport error: ' + e.message;
    }
    const verdict = classify(status, finalUrl, body);
    if (verdict.ok) pass++; else { fail++; failures.push(`${r.method} ${r.url} -> ${verdict.note}`); }
    rows.push([r.controller, r.action, r.method, r.url, status, finalUrl, verdict.ok ? 'PASS' : 'FAIL', verdict.note]);
  }

  fs.writeFileSync(outCsv, rows.map(r => r.map(c => {
    const s = String(c);
    return /[",]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
  }).join(',')).join('\n') + '\n');

  console.log(`anonymous sweep: ${pass} refused correctly, ${fail} did not, across ${routes.length} actions`);
  if (failures.length) {
    console.log('\nactions that answered an anonymous caller:');
    failures.slice(0, 40).forEach(f => console.log('  ' + f));
    if (failures.length > 40) console.log('  ... and ' + (failures.length - 40) + ' more');
  }
  console.log('\nwrote ' + outCsv);
  process.exit(fail ? 1 : 0);
})();
