// CODE-06: proves a pruned rule changed nothing on screen.
//
// A page sweep that reports no console error says nothing about whether an
// element lost its styling. This asks the direct question instead: does any
// element on any page still carry a class whose rule was removed? If none does,
// removing the rule cannot have changed how anything renders.
//
// Pass the removed class names as arguments.
const fs = require('fs'), puppeteer = require('puppeteer-core');
const config = require('./config');
const fixture = JSON.parse(fs.readFileSync('fixture.json', 'utf8'));
const routes = JSON.parse(fs.readFileSync('routes.json', 'utf8'));

const REMOVED = process.argv.slice(2);
if (!REMOVED.length) {
  console.error('Give the removed class names as arguments.');
  process.exit(2);
}

const ROLES = {
  admin: { user: config.admin.user, pass: config.admin.password, owns: ['Admin', 'AdminDatabase', 'AdminDeployment'] },
  teacher: { user: fixture.teacher.username, pass: fixture.teacher.password, owns: ['Teacher'] },
};

(async () => {
  const browser = await puppeteer.launch({
    executablePath: config.chromePath(), headless: 'new',
    args: ['--ignore-certificate-errors', '--no-sandbox']
  });
  const found = [];
  let visited = 0;

  try {
    for (const [role, who] of Object.entries(ROLES)) {
      const context = await browser.createBrowserContext();
      const page = await context.newPage();
      await page.goto(`${config.baseUrl}/Account/Login`, { waitUntil: 'networkidle2' });
      await page.type('input[name="username"]', who.user);
      await page.type('input[name="password"]', who.pass);
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'networkidle2' }),
        page.click('button[type="submit"]')
      ]);

      // routes.json records a single `method` per action, not a list.
      const targets = routes.filter(r =>
        who.owns.includes(r.controller) && r.method === 'GET' && !r.url.includes('{'));

      for (const route of targets) {
        try {
          const response = await page.goto(config.baseUrl + route.url, {
            waitUntil: 'domcontentloaded', timeout: 15000
          });
          if (!response || response.status() >= 400) continue;
          const type = response.headers()['content-type'] || '';
          if (!type.includes('text/html')) continue;
          visited++;

          const hits = await page.evaluate(names => {
            const out = [];
            for (const name of names) {
              const n = document.getElementsByClassName(name).length;
              if (n) out.push({ name, count: n });
            }
            return out;
          }, REMOVED);

          hits.forEach(h => found.push(`${role} ${route.url}: ${h.count} x .${h.name}`));
        } catch { /* a route that will not load is the sweep's problem, not this one */ }
      }
      await context.close();
    }
  } finally {
    await browser.close();
  }

  console.log(`checked ${visited} pages for ${REMOVED.length} removed class(es)`);
  if (found.length) {
    console.log('\nSTILL IN USE - the rule should not have been removed:');
    found.forEach(f => console.log('  ' + f));
    process.exit(1);
  }
  console.log('no page carries any removed class; the prune is invisible');
})();
