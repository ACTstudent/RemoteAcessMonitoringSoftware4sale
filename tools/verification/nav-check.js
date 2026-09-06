// UX-02: the sidebar must mark exactly one link as the current page, and it
// must be the right one. The old layouts compared only the action name, so
// opening /Admin/Students from the teacher portal lit up the teacher's own
// "Student Profiles" link - both actions are called Students.
//
// A teacher now keeps one menu wherever they are: the same list covers their
// classroom pages and the global ones, with no "back to my portal" link.
// The student portal is not checked - the web portal is for teachers and
// administrators, and a student signs in on the CAMS Student Client.
const fs = require('fs'), puppeteer = require('puppeteer-core');
const chrome = require('./config').chromePath();
const config = require('./config');
const BASE = config.baseUrl;
const fixture = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));

const results = [];
const check = (name, pass, detail) => {
  results.push(pass);
  console.log(`  ${pass ? 'PASS' : 'FAIL'}  ${name}${detail ? '\n          ' + detail : ''}`);
};

async function signIn(browser, user, pass) {
  const ctx = await browser.createBrowserContext();
  const page = await ctx.newPage();
  await page.goto(BASE + '/Account/Login', { waitUntil: 'networkidle2' });
  await page.type('#loginUsername', user);
  await page.type('#loginPassword', pass);
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click('button[type="submit"]')]);
  return page;
}

const readNav = () => {
  const links = [...document.querySelectorAll('.sidebar-nav .nav-item-custom')];
  const active = links.filter(a => a.classList.contains('active'));
  const label = a => (a.querySelector('.nav-item-label, span') || {}).textContent?.trim();
  return {
    total: links.length,
    labels: links.map(label),
    activeCount: active.length,
    activeText: active.map(label),
    activeHref: active.map(a => a.getAttribute('href')),
    ariaCurrent: links.filter(a => a.getAttribute('aria-current') === 'page').length,
    brand: document.querySelector('.sidebar-brand-text')?.textContent.trim(),
    skipLink: !!document.querySelector('.skip-to-content'),
    mainId: document.querySelector('main')?.id,
    crudPage: document.body.classList.contains('crud-page')
  };
};

(async () => {
  const b = await puppeteer.launch({ executablePath: chrome, headless: 'new',
    args: ['--ignore-certificate-errors', '--no-sandbox'], defaultViewport: { width: 1440, height: 900 } });

  const teacher = await signIn(b, fixture.teacher.username, fixture.teacher.password);
  console.log('=== teacher portal ===');
  for (const [url, expected] of [
    ['/Teacher/Students', 'Student Profiles'],
    ['/Teacher/Classes', 'Class Management'],
    ['/Teacher/Monitoring', 'Live Monitoring'],
    ['/Teacher/Alerts', 'Monitoring Alerts']
  ]) {
    await teacher.goto(BASE + url, { waitUntil: 'networkidle2' });
    const nav = await teacher.evaluate(readNav);
    check(`${url} marks exactly one link, "${expected}"`,
      nav.activeCount === 1 && nav.activeText[0] === expected,
      `${nav.activeCount} active: ${nav.activeText.join(', ') || 'none'}`);
  }

  console.log('\n=== the bug: an admin page opened from the teacher portal ===');
  await teacher.goto(BASE + '/Admin/Students', { waitUntil: 'networkidle2' });
  let nav = await teacher.evaluate(readNav);
  check('/Admin/Students does not light up the teacher\'s own Students link',
    !nav.activeText.includes('Student Profiles'),
    `active: ${nav.activeText.join(', ') || 'none'}`);
  check('/Admin/Students marks the global Students link instead',
    nav.activeCount === 1 && nav.activeHref[0] === '/Admin/Students',
    `${nav.activeCount} active -> ${nav.activeHref.join(', ') || 'none'}`);

  console.log('\n=== one menu, wherever the teacher is ===');
  const onTeacherPage = await (async () => {
    await teacher.goto(BASE + '/Teacher/Dashboard', { waitUntil: 'networkidle2' });
    return teacher.evaluate(readNav);
  })();
  const onAdminPage = await (async () => {
    await teacher.goto(BASE + '/Admin/Index', { waitUntil: 'networkidle2' });
    return teacher.evaluate(readNav);
  })();
  check('the teacher sees the same menu on a teacher page and an admin page',
    JSON.stringify(onTeacherPage.labels) === JSON.stringify(onAdminPage.labels),
    `${onTeacherPage.total} links vs ${onAdminPage.total}`);
  check('there is no "back to my portal" link any more',
    !onAdminPage.labels.some(l => /my teacher portal/i.test(l || '')),
    onAdminPage.labels.filter(l => /portal/i.test(l || '')).join(', ') || 'none');
  check('the brand stays the same rather than switching portal',
    onTeacherPage.brand === onAdminPage.brand,
    `${onTeacherPage.brand} / ${onAdminPage.brand}`);
  check('the menu still reaches both the classroom and the global pages',
    onAdminPage.labels.includes('Session Management') && onAdminPage.labels.includes('Teachers'),
    onAdminPage.total + ' links');

  console.log('\n=== detail pages keep their parent marked ===');
  for (const [url, expected] of [
    ['/Teacher/ClassDetails/1', 'Class Management'],
    ['/Teacher/StudentDetails/1', 'Student Profiles']
  ]) {
    await teacher.goto(BASE + url, { waitUntil: 'networkidle2' });
    nav = await teacher.evaluate(readNav);
    check(`${url} keeps "${expected}" marked`,
      nav.activeText.includes(expected), `active: ${nav.activeText.join(', ') || 'none'}`);
  }

  console.log('\n=== the shell, and the standard design ===');
  nav = await teacher.evaluate(readNav);
  check('the shell has the skip link and a main landmark', nav.skipLink && nav.mainId === 'main-content');
  check('aria-current marks the page as well as the class', nav.ariaCurrent === nav.activeCount,
    `aria-current=${nav.ariaCurrent}, .active=${nav.activeCount}`);
  check('the management design is applied without the page opting in', nav.crudPage);

  const admin = await signIn(b, config.admin.user, config.admin.password);
  await admin.goto(BASE + '/Admin/Teachers', { waitUntil: 'networkidle2' });
  const adminNav = await admin.evaluate(readNav);
  check('the admin shell brands itself CAMS Admin', adminNav.brand === 'CAMS Admin', adminNav.brand);
  check('the admin still gets the administrator-only links',
    adminNav.labels.includes('Audit Trail') && adminNav.labels.includes('System Logs'),
    adminNav.total + ' links');
  check('/Admin/Teachers marks exactly one link',
    adminNav.activeCount === 1 && adminNav.activeText[0] === 'Teachers',
    `${adminNav.activeCount} active: ${adminNav.activeText.join(', ')}`);
  check('the admin design is applied too', adminNav.crudPage);

  console.log('\n=== a teacher is still not offered admin-only doors ===');
  const forbidden = ['Audit Trail', 'System Logs', 'Roles & Permissions', 'Database Maintenance', 'Deployment Hub'];
  const offered = onAdminPage.labels.filter(l => forbidden.includes(l));
  check('the sidebar hides links the authorization filter would refuse',
    offered.length === 0, offered.join(', ') || 'none of them');

  await b.close();
  const failed = results.filter(r => !r).length;
  console.log(`\n${results.length - failed}/${results.length} checks passed`);
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error('ERROR ' + e.message); process.exit(2); });
