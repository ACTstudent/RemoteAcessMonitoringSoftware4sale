// CODE-04: proves the behaviour that moved out of Razar into local files still
// works. Loading without a console error is not the same as still functioning,
// and these were all event handlers.
const fs = require('fs'), puppeteer = require('puppeteer-core');
const config = require('./config');
const chrome = config.chromePath();
const BASE = config.baseUrl;
const fixture = JSON.parse(fs.readFileSync(process.argv[2] || 'fixture.json', 'utf8'));

// Three states, not two. A check with no data to run against is not a pass, and
// reporting it as one is how a green run stops meaning anything.
const results = [];
const record = (name, ok, detail) => {
  const state = ok === 'skip' ? 'SKIP' : ok ? 'PASS' : 'FAIL';
  results.push({ name, state, detail });
  console.log(`  ${state}  ${name}${detail ? '  ' + detail : ''}`);
};

async function signIn(browser, user, pass) {
  // Each role gets its own context: pages in one browser share a cookie jar.
  const context = await browser.createBrowserContext();
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
    executablePath: chrome, headless: 'new',
    args: ['--ignore-certificate-errors', '--no-sandbox']
  });

  try {
    const admin = await signIn(browser, config.admin.user, config.admin.password);

    // 1. The roster's move confirmation, and the exemption for unassigning.
    await admin.goto(`${BASE}/Admin/Students`, { waitUntil: 'networkidle2' });
    const wired = await admin.$$eval('form[data-move-confirm]', forms => forms.length);
    record('roster forms carry the move-confirm hook', wired > 0, `${wired} form(s)`);

    const hookedUp = await admin.evaluate(() => {
      const form = document.querySelector('form[data-move-confirm]');
      if (!form) return 'no form';
      return {
        select: !!form.querySelector('[data-move-select]'),
        flag: !!form.querySelector('[data-move-flag]'),
        loaded: typeof window.CamsStudentMove === 'object'
      };
    });
    record('roster form exposes select, flag and the module',
      hookedUp.select && hookedUp.flag && hookedUp.loaded, JSON.stringify(hookedUp));

    // Put a student into a class first, otherwise every move check below has
    // nothing to move and reports a hollow pass.
    const seeded = await admin.evaluate(async () => {
      const form = document.querySelector('form[data-move-confirm]');
      const select = form && form.querySelector('[data-move-select]');
      if (!select) return 'no roster form';
      if (select.dataset.currentClassId) return 'already in a class';
      const target = Array.from(select.options).find(o => o.value);
      if (!target) return 'no class to assign to';
      select.value = target.value;
      const body = new URLSearchParams(new FormData(form));
      const response = await fetch(form.action, {
        method: 'POST', body, credentials: 'same-origin', redirect: 'follow'
      });
      return response.ok ? 'assigned' : `assign failed ${response.status}`;
    });
    console.log(`  ....  seeding a class membership: ${seeded}`);
    await admin.goto(`${BASE}/Admin/Students`, { waitUntil: 'networkidle2' });

    // Submitting with the class unchanged must go straight through, which is
    // what the inline version did and the shared version has to keep doing.
    const unchanged = await admin.evaluate(() => {
      const form = document.querySelector('form[data-move-confirm]');
      const select = form.querySelector('[data-move-select]');
      if (!select.dataset.currentClassId) return 'still no current class';
      select.value = select.dataset.currentClassId;
      let prevented = null;
      form.addEventListener('submit', e => { prevented = e.defaultPrevented; e.preventDefault(); }, { once: true });
      form.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
      return prevented;
    });
    record('same class again is not treated as a move',
      typeof unchanged === 'string' ? 'skip' : unchanged === false, String(unchanged));

    // And a genuine change must be intercepted so the teacher is asked.
    const changed = await admin.evaluate(() => {
      const form = document.querySelector('form[data-move-confirm]');
      const select = form.querySelector('[data-move-select]');
      const current = select.dataset.currentClassId;
      if (!current) return 'still no current class';
      const other = Array.from(select.options).find(o => o.value && o.value !== current);
      if (!other) return 'only one class exists, cannot test a real move';
      select.value = other.value;
      let prevented = null;
      form.addEventListener('submit', e => { prevented = e.defaultPrevented; }, { once: true });
      form.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
      return prevented;
    });
    record('a real move is intercepted for confirmation',
      typeof changed === 'string' ? 'skip' : changed === true, String(changed));

    // 2. The deployment endpoint echo. Routed at /Admin/Deployment, not at the
    // controller name.
    await admin.goto(`${BASE}/Admin/Deployment`, { waitUntil: 'networkidle2' });
    const endpointEcho = await admin.evaluate(() => {
      const select = document.querySelector('[data-deployment-endpoint]');
      const help = document.querySelector('[data-deployment-endpoint-help]');
      if (!select || !help) return 'markup missing';
      if (!select.options.length) return 'no endpoints offered';
      const before = help.textContent;
      select.dispatchEvent(new Event('change'));
      return { changed: help.textContent !== before, now: help.textContent.slice(0, 60) };
    });
    record('endpoint choice is echoed back',
      typeof endpointEcho === 'string' ? 'skip' : endpointEcho.changed, JSON.stringify(endpointEcho));

    const teacher = await signIn(browser, fixture.teacher.username, fixture.teacher.password);

    // 3. The alert select-all, both directions.
    await teacher.goto(`${BASE}/Teacher/Alerts`, { waitUntil: 'networkidle2' });
    const selectAll = await teacher.evaluate(() => {
      const all = document.querySelector('[data-alert-select-all]');
      const boxes = Array.from(document.querySelectorAll('[data-alert-select]'));
      if (!all) return 'no select-all box';
      if (!boxes.length) return 'no alerts to select';
      all.checked = true; all.dispatchEvent(new Event('change'));
      const allOn = boxes.every(b => b.checked);
      boxes[0].checked = false; boxes[0].dispatchEvent(new Event('change'));
      return { allOn, headerCleared: all.checked === false };
    });
    record('select-all ticks every row, and clearing one clears the header',
      typeof selectAll === 'string' ? 'skip' : (selectAll.allOn && selectAll.headerCleared),
      JSON.stringify(selectAll));

    // 4. The ordering teacher-monitoring.js depends on.
    await teacher.goto(`${BASE}/Teacher/Monitoring`, { waitUntil: 'networkidle2' });
    const hubReady = await teacher.evaluate(() => ({
      connection: typeof window.teacherHubConnection === 'object' && window.teacherHubConnection !== null,
      started: typeof window.teacherHubStarted !== 'undefined'
    }));
    record('the badge script still publishes the hub before monitoring reads it',
      hubReady.connection && hubReady.started, JSON.stringify(hubReady));

  } finally {
    await browser.close();
  }

  const failed = results.filter(r => r.state === 'FAIL');
  const skipped = results.filter(r => r.state === 'SKIP');
  console.log(`\n${results.length - failed.length - skipped.length} passed, ` +
    `${skipped.length} skipped for want of data, ${failed.length} failed`);
  if (skipped.length) {
    console.log('A skip is not a pass. Seed the missing data before trusting this run.');
  }
  process.exit(failed.length ? 1 : 0);
})();
