// Puts the fixture back: reactivates the student and teacher an earlier probe
// deactivated. Drives the real controls, so it also re-checks that the
// isActive field now renders a value instead of relying on a failed bind.
const fs = require('fs'), puppeteer = require('puppeteer-core');
const { DatabaseSync } = require('node:sqlite');
const chrome = require('./config').chromePath();
const config = require('./config');
const BASE = config.baseUrl;
// The isolated server's database, so the checks can assert on stored rows.
// Passed in because it lives wherever the test server was published.
const DB = process.env.CAMS_TEST_DB || process.argv[3];
if (!DB) { console.error('Set CAMS_TEST_DB to the test server\'s CAMS.db'); process.exit(2); }

const statuses = () => {
  const db = new DatabaseSync(DB);
  return {
    students: db.prepare('SELECT Username, Status FROM Students').all(),
    teachers: db.prepare('SELECT Username, Status FROM Teachers').all()
  };
};

(async () => {
  const b = await puppeteer.launch({ executablePath: chrome, headless: 'new',
    args: ['--ignore-certificate-errors', '--no-sandbox'], defaultViewport: { width: 1440, height: 900 } });
  const ctx = await b.createBrowserContext();
  const page = await ctx.newPage();
  await page.goto(BASE + '/Account/Login', { waitUntil: 'networkidle2' });
  await page.type('#loginUsername', config.admin.user);
  await page.type('#loginPassword', config.admin.password);
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click('button[type="submit"]')]);
  if (/Account\/Login/.test(page.url())) { console.error('admin sign-in failed'); process.exit(2); }

  console.log('before: ' + JSON.stringify(statuses()));

  for (const [listUrl, label] of [['/Admin/Students', 'students'], ['/Admin/Teachers', 'teachers']]) {
    // Reactivate every inactive row by submitting the control whose isActive is True.
    for (let pass = 0; pass < 5; pass++) {
      await page.goto(BASE + listUrl, { waitUntil: 'networkidle2' });
      const submitted = await page.evaluate(() => {
        const form = [...document.querySelectorAll('form[action="/Admin/SetAccountActive"]')]
          .find(f => (f.querySelector('[name=isActive]') || {}).value === 'True');
        if (!form) return false;
        form.submit();
        return true;
      });
      if (!submitted) break;
      await page.waitForNavigation({ waitUntil: 'networkidle2' }).catch(() => {});
    }
    console.log(`  ${label} pass done`);
  }

  console.log('after:  ' + JSON.stringify(statuses()));
  await b.close();
})().catch(e => { console.error('ERROR ' + e.message); process.exit(2); });
