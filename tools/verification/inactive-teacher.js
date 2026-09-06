// AUTH-02, inactive-teacher row, done properly.
//
// The first attempt deactivated a teacher who still owned an active class, and
// the server correctly refused - class dependencies are enforced before an
// account can be closed. So this uses a second teacher who owns nothing, which
// isolates the question actually being asked: once an account is deactivated,
// what can the session that is already signed in still do?
const fs = require('fs'), puppeteer = require('puppeteer-core');
const { DatabaseSync } = require('node:sqlite');
const chrome = require('./config').chromePath();
const config = require('./config');
const BASE = config.baseUrl;
// The isolated server's database, so the checks can assert on stored rows.
// Passed in because it lives wherever the test server was published.
const DB = process.env.CAMS_TEST_DB || process.argv[3];
if (!DB) { console.error('Set CAMS_TEST_DB to the test server\'s CAMS.db'); process.exit(2); }

const T2 = { user: 't2r0906', pass: 'Teach2-R0906-2026!', first: 'Terry', last: 'Second R0906' };
const results = [];
const check = (name, pass, detail) => {
  results.push(pass);
  console.log(`  ${pass ? 'PASS' : 'FAIL'}  ${name}${detail ? '\n          ' + detail : ''}`);
};
const statusOf = u => {
  const row = new DatabaseSync(DB).prepare('SELECT Status FROM Teachers WHERE Username = ?').get(u);
  return row ? row.Status : '(absent)';
};

// Each role gets its own browser context. Pages in one context share a cookie
// jar, so signing in as a teacher would otherwise silently replace the admin
// session and every later admin action would run as the teacher.
async function signIn(browser, user, pass) {
  const context = await browser.createBrowserContext();
  const page = await context.newPage();
  await page.goto(BASE + '/Account/Login', { waitUntil: 'networkidle2' });
  await page.type('#loginUsername', user);
  await page.type('#loginPassword', pass);
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click('button[type="submit"]')]);
  return page;
}

(async () => {
  const b = await puppeteer.launch({ executablePath: chrome, headless: 'new',
    args: ['--ignore-certificate-errors', '--no-sandbox'], defaultViewport: { width: 1440, height: 900 } });

  await new Promise(r => setTimeout(r, 65000));
  const admin = await signIn(b, config.admin.user, config.admin.password);
  if (/Account\/Login/.test(admin.url())) { console.error('admin sign-in failed'); process.exit(2); }

  if (statusOf(T2.user) === '(absent)') {
    await admin.goto(BASE + '/Admin/Teachers', { waitUntil: 'networkidle2' });
    await admin.evaluate(t => {
      const form = document.querySelector('form[action="/Admin/CreateTeacher"]');
      const set = (n, v) => { const el = form.querySelector(`[name="${n}"]`); if (el) el.value = v; };
      set('FirstName', t.first); set('LastName', t.last); set('Username', t.user);
      set('PasswordHash', t.pass); set('Email', t.user + '@example.invalid');
      set('ContactNumber', '0000000000'); set('Status', 'Active');
      form.submit();
    }, T2);
    await admin.waitForNavigation({ waitUntil: 'networkidle2' }).catch(() => {});
  }
  console.log('second teacher created, status: ' + statusOf(T2.user));

  await new Promise(r => setTimeout(r, 65000));
  const teacher = await signIn(b, T2.user, T2.pass);
  check('the new teacher can sign in', !/Account\/Login/.test(teacher.url()), 'landed on ' + teacher.url().replace(BASE, ''));
  let r = await teacher.goto(BASE + '/Admin/Students', { waitUntil: 'networkidle2' });
  check('an active teacher reaches a shared admin page', r.status() === 200, 'HTTP ' + r.status());

  // Deactivate through the real control.
  await admin.goto(BASE + '/Admin/Teachers', { waitUntil: 'networkidle2' });
  // Pick the control by the teacher id it carries rather than by row text: the
  // username also appears in the edit dialog, so matching on text picks the
  // wrong element.
  const t2Id = new DatabaseSync(DB).prepare('SELECT TeacherId FROM Teachers WHERE Username = ?').get(T2.user).TeacherId;
  const submitted = await admin.evaluate(id => {
    const forms = [...document.querySelectorAll('form[action="/Admin/SetAccountActive"]')];
    const seen = forms.map(f => (f.querySelector('[name=id]') || {}).value);
    const form = forms.find(f => (f.querySelector('[name=id]') || {}).value === String(id));
    if (!form) return `no control for id ${id}; page has ${forms.length} control(s) ` +
      `for ids [${seen.join(', ')}], title "${document.title}"`;
    form.submit();
    return 'submitted for teacher ' + id;
  }, t2Id);
  await admin.waitForNavigation({ waitUntil: 'networkidle2' }).catch(() => {});
  const banner = await admin.evaluate(() =>
    [...document.querySelectorAll('.alert, .toast-body')].map(e => e.textContent.trim()).filter(Boolean).join(' | '));
  console.log(`\n  deactivate: ${submitted}; page said: ${banner || '(nothing)'}`);
  check('the account is now inactive in the database', statusOf(T2.user) === 'Inactive', 'status = ' + statusOf(T2.user));

  if (statusOf(T2.user) === 'Inactive') {
    console.log('\nOn the browser session that was already signed in:');
    for (const path of ['/Admin/Students', '/Admin/Classes', '/Teacher/Dashboard', '/Teacher/Students']) {
      const res = await teacher.goto(BASE + path, { waitUntil: 'networkidle2' });
      const landed = teacher.url().replace(BASE, '');
      const refused = res.status() !== 200 || /Account\/Login/.test(landed);
      console.log(`  ${path.padEnd(20)} HTTP ${res.status()} -> ${landed}`);
      if (path.startsWith('/Admin/'))
        check(`the deactivated teacher can no longer use ${path}`, refused, 'HTTP ' + res.status() + ' -> ' + landed);
    }
    await new Promise(r => setTimeout(r, 65000));
    const fresh = await signIn(b, T2.user, T2.pass);
    check('a deactivated teacher cannot sign in again',
      /Account\/Login/.test(fresh.url()), 'landed on ' + fresh.url().replace(BASE, ''));
    await fresh.close();
  }

  await b.close();
  const failed = results.filter(x => !x).length;
  console.log(`\n${results.length - failed}/${results.length} checks passed`);
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error('ERROR ' + e.message); process.exit(2); });
