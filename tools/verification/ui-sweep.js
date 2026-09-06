// UI-01 and UI-02 against the isolated server. For each role: sign in once,
// visit every GET action that role owns, and record the HTTP status, any
// console error, any failed sub-request, and the accessibility problems on the
// rendered page. One sign-in per role keeps the run inside the login throttle.
const fs = require('fs'), puppeteer = require('puppeteer-core');
const chrome = require('./config').chromePath();

const config = require('./config');
const BASE = config.baseUrl;
const routes = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const fixture = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
const outDir = process.argv[4];

const ROLES = {
  admin: { user: config.admin.user, pass: config.admin.password, owns: ['Admin', 'AdminDatabase', 'AdminDeployment'] },
  teacher: { user: fixture.teacher.username, pass: fixture.teacher.password, owns: ['Teacher'] },
  // No student row: the web portal is for teachers and administrators. A
  // student signs in on the CAMS Student Client, which uses the hub and the
  // client API rather than these pages.
};

// Actions that need a real id; without one the controller legitimately bounces.
const NEEDS_ID = /Details$|History$|Edit$|^ClassAnalytics$/;

// Controllers whose routes come from a [Route] attribute rather than the
// convention, so the conventional {controller}/{action} URL does not exist.
const ROUTE_OVERRIDES = { 'AdminDeployment.Index': '/Admin/Deployment' };

// Downloads must not be navigated to - the browser would hang on the transfer.
const IS_DOWNLOAD = /^Export|^Installer$|^Bundle$|^Manifest$|^RootCertificate$|Csv$/;

const audit = () => {
  const problems = [];
  const name = el => {
    if (el.getAttribute('aria-label')) return el.getAttribute('aria-label');
    const by = el.getAttribute('aria-labelledby');
    if (by) {
      const t = by.split(/\s+/).map(id => (document.getElementById(id) || {}).textContent || '').join(' ');
      if (t.trim()) return t;
    }
    if (el.id) {
      const l = document.querySelector('label[for="' + CSS.escape(el.id) + '"]');
      if (l && l.textContent.trim()) return l.textContent.trim();
    }
    if (el.closest('label') && el.closest('label').textContent.trim()) return el.closest('label').textContent.trim();
    if (el.title) return el.title;
    if (el.getAttribute('placeholder')) return '(placeholder only) ' + el.getAttribute('placeholder');
    return '';
  };
  const describe = el => el.tagName.toLowerCase() +
    (el.type ? '[type=' + el.type + ']' : '') + (el.name ? '[name=' + el.name + ']' : '') + (el.id ? '#' + el.id : '');

  document.querySelectorAll('input:not([type=hidden]), select, textarea').forEach(el => {
    const n = name(el);
    if (!n) problems.push({ kind: 'control has no accessible name', el: describe(el) });
    else if (n.startsWith('(placeholder only)')) problems.push({ kind: 'control named only by placeholder', el: describe(el) });
  });
  document.querySelectorAll('button, a[href]').forEach(el => {
    const t = (el.textContent || '').trim() || el.getAttribute('aria-label') || el.title || '';
    if (!t && !el.hasAttribute('aria-hidden')) problems.push({ kind: 'button or link has no text', el: describe(el) + ' ' + el.className });
  });
  document.querySelectorAll('img').forEach(el => {
    if (!el.hasAttribute('alt')) problems.push({ kind: 'image without alt', el: el.getAttribute('src') });
  });
  const seen = {}, dupes = {};
  document.querySelectorAll('[id]').forEach(el => { if (seen[el.id]) dupes[el.id] = (dupes[el.id] || 1) + 1; else seen[el.id] = true; });
  Object.keys(dupes).forEach(id => problems.push({ kind: 'duplicate id', el: '#' + id + ' x' + dupes[id] }));
  document.querySelectorAll('label[for]').forEach(el => {
    if (!document.getElementById(el.getAttribute('for')))
      problems.push({ kind: 'label points at a missing control', el: 'for=' + el.getAttribute('for') });
  });
  if (!document.querySelector('main, [role=main]')) problems.push({ kind: 'no main landmark', el: '(page)' });
  if (!document.querySelector('h1, h2')) problems.push({ kind: 'no page heading', el: '(page)' });
  document.querySelectorAll('table').forEach((t, i) => {
    if (!t.querySelector('th')) problems.push({ kind: 'table without header cells', el: 'table#' + i });
  });
  return { problems, xssFired: !!window.__camsXss, title: document.title };
};

(async () => {
  const browser = await puppeteer.launch({
    executablePath: chrome, headless: 'new',
    args: ['--ignore-certificate-errors', '--no-sandbox'],
    defaultViewport: { width: 1440, height: 900 }
  });

  const rows = [['Role', 'Controller', 'Action', 'Url', 'Status', 'FinalUrl', 'ConsoleErrors', 'FailedRequests', 'A11yProblems', 'Verdict', 'Note']];
  const byKind = {};
  let pagesVisited = 0, pagesClean = 0, xssFired = false;
  const defects = [];

  for (const [roleName, role] of Object.entries(ROLES)) {
    const page = await browser.newPage();
    const consoleErrors = [], failedRequests = [];
    page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
    page.on('pageerror', e => consoleErrors.push('pageerror: ' + e.message));
    page.on('requestfailed', r => failedRequests.push(r.url() + ' ' + (r.failure() || {}).errorText));
    page.on('response', r => { if (r.status() >= 400) failedRequests.push(r.url() + ' HTTP ' + r.status()); });

    await page.goto(BASE + '/Account/Login', { waitUntil: 'networkidle2' });
    await page.type('#loginUsername', role.user);
    await page.type('#loginPassword', role.pass);
    await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click('button[type="submit"]')]);

    if (/\/Account\/Login/.test(page.url())) {
      console.log(`\n===== ${roleName}: SIGN-IN FAILED, portal skipped =====`);
      rows.push([roleName, '-', 'SignIn', '/Account/Login', '-', page.url(), '', '', '', 'FAIL', 'could not sign in']);
      defects.push(`${roleName} could not sign in`);
      await page.close();
      continue;
    }
    console.log(`\n===== ${roleName} (landed on ${page.url().replace(BASE, '')}) =====`);

    const mine = routes.filter(r => r.method === 'GET' && role.owns.includes(r.controller));
    for (const r of mine) {
      if (IS_DOWNLOAD.test(r.action)) {
        rows.push([roleName, r.controller, r.action, r.url, '', '', '', '', '', 'N/A', 'file download, covered separately']);
        continue;
      }
      consoleErrors.length = 0; failedRequests.length = 0;
      let status = 0, finalUrl = '', result = { problems: [], title: '' };
      let url = ROUTE_OVERRIDES[`${r.controller}.${r.action}`] || r.url;
      if (!ROUTE_OVERRIDES[`${r.controller}.${r.action}`] && NEEDS_ID.test(r.action)) url += '/1';

      try {
        const resp = await page.goto(BASE + url, { waitUntil: 'networkidle2', timeout: 30000 });
        status = resp.status();
        finalUrl = page.url();
        const contentType = (resp.headers()['content-type'] || '');
        const isMarkup = status === 200 && contentType.toLowerCase().includes('text/html');
        result = isMarkup
          ? await page.evaluate(audit)
          : { problems: [], title: status === 200 ? '(' + contentType.split(';')[0] + ')' : '' };
      } catch (e) {
        status = -1; finalUrl = page.url(); result = { problems: [{ kind: 'navigation failed', el: e.message }] };
      }
      if (result.xssFired) xssFired = true;

      // Chrome asks for /favicon.ico whenever a response carries no
      // <link rel="icon">, which here only happens because the sweep navigates
      // to JSON endpoints. Its console message does not name the URL, so a
      // generic resource failure with no non-favicon request behind it was the
      // favicon and is not a fault in the page.
      const otherFailures = failedRequests.filter(f => !/favicon/.test(f));
      const realConsoleErrors = consoleErrors.filter(e =>
        !(otherFailures.length === 0 && /Failed to load resource/i.test(e)));
      let verdict = 'PASS', note = result.title || '';
      if (status >= 500) { verdict = 'FAIL'; note = 'server error'; }
      // An action that takes an id, asked for one the fixture does not hold, is
      // untested rather than broken. This has to be decided before the console
      // check, because the 404 logs a console error of its own.
      else if (status === 404 && NEEDS_ID.test(r.action)) { verdict = 'N/A'; note = 'no fixture row for this id'; }
      else if (status === 404) { verdict = 'FAIL'; note = 'not found'; }
      else if (/\/Account\/Login/.test(finalUrl)) { verdict = 'FAIL'; note = 'own role bounced to login'; }
      else if (realConsoleErrors.length) { verdict = 'FAIL'; note = 'console error: ' + realConsoleErrors[0].slice(0, 120); }

      if (verdict === 'FAIL') defects.push(`${roleName} ${url}: ${note}`);
      pagesVisited++;
      if (verdict === 'PASS' && result.problems.length === 0) pagesClean++;
      result.problems.forEach(p => (byKind[p.kind] = byKind[p.kind] || []).push(`${roleName} ${url}: ${p.el}`));

      rows.push([roleName, r.controller, r.action, url, status, finalUrl,
        realConsoleErrors.length, otherFailures.length,
        result.problems.length, verdict, note]);

      console.log(`  ${url.padEnd(34)} ${String(status).padEnd(4)} ${verdict.padEnd(5)} ` +
        `${result.problems.length ? result.problems.length + ' a11y' : 'a11y clean'}` +
        `${consoleErrors.length ? '  CONSOLE:' + consoleErrors[0].slice(0, 60) : ''}`);
    }
    await page.close();
  }

  await browser.close();

  fs.writeFileSync(outDir + '/ui-sweep.csv', rows.map(r => r.map(c => {
    const s = String(c); return /[",\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
  }).join(',')).join('\n') + '\n');

  console.log('\n===== accessibility problems by kind =====');
  const kinds = Object.keys(byKind).sort((a, b) => byKind[b].length - byKind[a].length);
  if (!kinds.length) console.log('none across every page visited');
  kinds.forEach(k => {
    console.log(`\n${k}  (${byKind[k].length})`);
    byKind[k].slice(0, 10).forEach(e => console.log('   ' + e));
    if (byKind[k].length > 10) console.log(`   ... and ${byKind[k].length - 10} more`);
  });

  console.log(`\npages visited: ${pagesVisited}, entirely clean: ${pagesClean}`);
  console.log(`stored script payload executed: ${xssFired ? 'YES - SECURITY DEFECT' : 'no'}`);
  console.log(`failing pages: ${defects.length}`);
  defects.slice(0, 20).forEach(d => console.log('  ' + d));
  console.log('\nwrote ' + outDir + '/ui-sweep.csv');
})().catch(e => { console.error('ERROR ' + e.message); process.exit(2); });
