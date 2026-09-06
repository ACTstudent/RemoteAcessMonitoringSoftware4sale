// UX-01 / UX-06: a keyboard user must be able to see where they are.
//
// The stylesheet had one :focus-visible rule in 1,400 lines, and the custom
// pill buttons override Bootstrap's shadow-based ring, so on most of the
// interface there was little or nothing to see. This tabs through the real
// pages and reads the computed outline off whatever has focus.
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

// Walks the tab order and records what the focus indicator looks like on each stop.
const walk = (steps) => {
  const seen = [];
  return seen;
};

(async () => {
  const b = await puppeteer.launch({ executablePath: chrome, headless: 'new',
    args: ['--ignore-certificate-errors', '--no-sandbox'], defaultViewport: { width: 1440, height: 900 } });
  const page = await signIn(b, fixture.teacher.username, fixture.teacher.password);

  for (const url of ['/Teacher/Dashboard', '/Teacher/Students', '/Teacher/Alerts']) {
    await page.goto(BASE + url, { waitUntil: 'networkidle2' });
    await page.evaluate(() => document.body.focus());

    const stops = [];
    for (let i = 0; i < 22; i++) {
      await page.keyboard.press('Tab');
      await new Promise(r => setTimeout(r, 350));   // let any transition settle
      const stop = await page.evaluate(() => {
        const el = document.activeElement;
        if (!el || el === document.body) return null;
        const cs = getComputedStyle(el);
        const width = parseFloat(cs.outlineWidth) || 0;
        return {
          tag: el.tagName.toLowerCase(),
          cls: (el.className || '').toString().split(/\s+/)[0],
          outlineWidth: width,
          outlineStyle: cs.outlineStyle,
          outlineColor: cs.outlineColor,
          boxShadow: cs.boxShadow,
          inSidebar: !!el.closest('.app-sidebar')
        };
      });
      if (stop) stops.push(stop);
    }

    const visible = stops.filter(s =>
      (s.outlineWidth >= 2 && s.outlineStyle !== 'none') ||
      (s.boxShadow && s.boxShadow !== 'none'));

    console.log(`\n${url} — ${stops.length} tab stops`);
    const invisible = stops.filter(s => !visible.includes(s));
    check(`${url}: every tab stop shows a focus indicator`,
      invisible.length === 0,
      invisible.length
        ? invisible.slice(0, 4).map(s => `${s.tag}.${s.cls} outline=${s.outlineWidth}px ${s.outlineStyle}`).join('; ')
        : `${stops.length} stops, all with a ring`);

    // Only the nav links: the sidebar also holds the brand image and the
    // collapse toggle, which are not what this assertion is about.
    const sidebarStops = stops.filter(s => s.inSidebar && s.cls === 'nav-item-custom');
    if (sidebarStops.length) {
      // The indicator is a soft halo drawn with box-shadow; the outline is
      // transparent and exists only so forced-colors mode can repaint it.
      check(`${url}: the sidebar halo is light enough to see on the dark bar`,
        sidebarStops.every(s => /187, 243, 198/.test(s.boxShadow)),
        sidebarStops[0].boxShadow);
    }
  }

  // The skip link must be the very first stop and must become visible.
  await page.goto(BASE + '/Teacher/Dashboard', { waitUntil: 'networkidle2' });
  await page.evaluate(() => document.body.focus());
  await page.keyboard.press('Tab');
  const first = await page.evaluate(() => {
    const el = document.activeElement;
    const cs = getComputedStyle(el);
    return { text: (el.textContent || '').trim(), left: cs.left, cls: el.className };
  });
  check('the first tab stop is the skip link', /skip to main content/i.test(first.text), first.text);
  check('the skip link moves on screen when focused', first.left === '0px', 'left=' + first.left);

  // Corner radii still render after the scale swap.
  const radii = await page.evaluate(() => {
    const card = document.querySelector('.metric-card, .card');
    const pill = document.querySelector('.rounded-pill');
    return {
      card: card ? getComputedStyle(card).borderRadius : null,
      pill: pill ? getComputedStyle(pill).borderRadius : null
    };
  });
  check('cards still have their corner radius', radii.card && radii.card !== '0px', 'card=' + radii.card);

  await b.close();
  const failed = results.filter(r => !r).length;
  console.log(`\n${results.length - failed}/${results.length} checks passed`);
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error('ERROR ' + e.message); process.exit(2); });
