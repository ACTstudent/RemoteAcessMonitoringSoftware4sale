// FLOW-05: repeated events must not stack, and a notice must survive the toast
// that carried it.
const fs = require('fs'), puppeteer = require('puppeteer-core');
const config = require('./config');
const fixture = JSON.parse(fs.readFileSync('fixture.json', 'utf8'));

let failed = 0;
const check = (ok, name, detail) => {
  if (!ok) failed++;
  console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  ' + detail : ''}`);
};

(async () => {
  const browser = await puppeteer.launch({
    executablePath: config.chromePath(), headless: 'new',
    args: ['--ignore-certificate-errors', '--no-sandbox']
  });
  try {
    const page = await browser.newPage();
    await page.goto(`${config.baseUrl}/Account/Login`, { waitUntil: 'networkidle2' });
    await page.type('input[name="username"]', fixture.teacher.username);
    await page.type('input[name="password"]', fixture.teacher.password);
    await Promise.all([
      page.waitForNavigation({ waitUntil: 'networkidle2' }),
      page.click('button[type="submit"]')
    ]);
    await page.goto(`${config.baseUrl}/Teacher/Dashboard`, { waitUntil: 'networkidle2' });

    // The same warning twenty times, as a student parked on a blocked page
    // would produce.
    const repeated = await page.evaluate(() => {
      for (let i = 0; i < 20; i++) {
        CamsToast.show('Blocked site: example.invalid', { type: 'error', title: 'Policy' });
      }
      return {
        onScreen: document.querySelectorAll('.cams-toast').length,
        badge: document.querySelector('.cams-toast-count')?.textContent || '',
        history: CamsToast.history().length
      };
    });

    check(repeated.onScreen === 1,
      'twenty identical notices produce one toast, not twenty', `${repeated.onScreen} on screen`);
    check(repeated.badge === '×20',
      'the toast says how many times it happened', repeated.badge);
    check(repeated.history === 20,
      'every occurrence is still recorded in the history', `${repeated.history} entries`);

    // A different notice is its own toast.
    const distinct = await page.evaluate(() => {
      CamsToast.show('A different thing happened', { title: 'Other' });
      return document.querySelectorAll('.cams-toast').length;
    });
    check(distinct === 2, 'a different notice is not folded into the first', `${distinct} on screen`);

    // History survives the toast being dismissed.
    const afterDismiss = await page.evaluate(async () => {
      document.querySelectorAll('[data-toast-dismiss]').forEach(b => b.click());
      await new Promise(r => setTimeout(r, 600));
      return {
        toasts: document.querySelectorAll('.cams-toast').length,
        history: CamsToast.history().length,
        panel: !!document.getElementById('cams-toast-history'),
        listed: document.querySelectorAll('.cams-toast-history-item').length
      };
    });
    check(afterDismiss.history >= 21,
      'dismissing a toast does not erase what it said', `${afterDismiss.history} entries`);
    check(afterDismiss.panel && afterDismiss.listed > 0,
      'and the history is on the page, not just in memory',
      `${afterDismiss.listed} listed`);

    // Bounded, so the history is not its own kind of noise.
    const bounded = await page.evaluate(() => {
      for (let i = 0; i < 200; i++) CamsToast.show('noise ' + i);
      return CamsToast.history().length;
    });
    check(bounded <= 50, 'the history is bounded', `${bounded} entries`);

    // The history is a copy, not the live array.
    const isCopy = await page.evaluate(() => {
      const before = CamsToast.history().length;
      CamsToast.history().length = 0;
      return CamsToast.history().length === before;
    });
    check(isCopy, 'callers cannot quietly rewrite the history');

  } finally {
    await browser.close();
  }
  console.log(`\n${failed} failed`);
  process.exit(failed ? 1 : 0);
})();
