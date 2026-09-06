// FLOW-01: a fresh install must say what is left to do and where to do it.
//
// Point CAMS_FRESH_URL at a server with an empty database to see the whole
// checklist; run it against the fixtured server to check the parts that do not
// depend on a fresh state.
const fs = require('fs'), puppeteer = require('puppeteer-core');
const config = require('./config');
const BASE = process.env.CAMS_FRESH_URL || config.baseUrl;

let failed = 0;
const check = (ok, name, detail) => {
  if (!ok) failed++;
  console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  ' + detail : ''}`);
};

async function signIn(context, user, pass) {
  const page = await context.newPage();
  await page.goto(`${BASE}/Account/Login`, { waitUntil: 'networkidle2' });
  await page.type('input[name="username"]', user);
  await page.type('input[name="password"]', pass);
  await Promise.all([
    page.waitForNavigation({ waitUntil: 'networkidle2' }),
    page.click('button[type="submit"]')
  ]);
  return page;
}

(async () => {
  const browser = await puppeteer.launch({
    executablePath: config.chromePath(), headless: 'new',
    args: ['--ignore-certificate-errors', '--no-sandbox']
  });

  try {
    const adminContext = await browser.createBrowserContext();
    const page = await signIn(adminContext, config.admin.user, config.admin.password);

    const panel = await page.evaluate(() => {
      const section = document.querySelector('.setup-checklist');
      if (!section) return null;
      const steps = [...document.querySelectorAll('.setup-step')].map(el => ({
        title: el.querySelector('.setup-step-title')?.textContent.trim(),
        done: el.classList.contains('is-done'),
        why: el.querySelector('.setup-step-why')?.textContent.trim() || '',
        action: el.querySelector('.setup-step-action')?.textContent.trim() || '',
        href: el.querySelector('a.setup-step-action')?.getAttribute('href') || null
      }));
      return {
        summary: section.querySelector('p')?.textContent.trim() || '',
        steps
      };
    });

    check(panel !== null, 'an administrator with setup outstanding sees a checklist');
    if (!panel) throw new Error('no checklist panel');

    check(panel.steps.length >= 5, 'it lists the steps', `${panel.steps.length} steps`);
    check(/Next:/.test(panel.summary),
      'and names one next step rather than a wall of red',
      panel.summary.replace(/\s+/g, ' ').slice(0, 90));

    const outstanding = panel.steps.filter(step => !step.done);
    check(outstanding.every(step => step.why.length > 20),
      'every outstanding step says what does not work until it is done');
    check(outstanding.every(step => step.action.length > 0),
      'and names its next action');

    const linked = outstanding.filter(step => step.href);
    check(linked.length >= outstanding.length - 1,
      'all but the on-the-workstation step link to where it is done',
      `${linked.length} of ${outstanding.length} linked`);

    // A link that names a page it cannot reach is worse than no link.
    for (const step of linked.slice(0, 3)) {
      const response = await page.goto(BASE + step.href, { waitUntil: 'domcontentloaded' });
      check(response.status() === 200, `the "${step.title}" link reaches its page`,
        `${step.href} -> ${response.status()}`);
    }
    await adminContext.close();

    // Only an administrator can act on any of these, so a teacher must not be
    // shown the list on a page they open every day.
    let fixture = null;
    try { fixture = JSON.parse(fs.readFileSync('fixture.json', 'utf8')); } catch { /* optional */ }

    if (!fixture) {
      console.log('  ....  teacher check skipped: no fixture.json');
    } else {
      const teacherContext = await browser.createBrowserContext();
      const teacherPage = await signIn(teacherContext, fixture.teacher.username, fixture.teacher.password);
      await teacherPage.goto(`${BASE}/Admin/Index`, { waitUntil: 'networkidle2' });
      const teacherSees = await teacherPage.evaluate(() => !!document.querySelector('.setup-checklist'));
      check(!teacherSees, 'a teacher is not shown the administrator checklist', `saw=${teacherSees}`);
      await teacherContext.close();
    }

  } finally {
    await browser.close();
  }

  console.log(`\n${failed} failed`);
  process.exit(failed ? 1 : 0);
})();
