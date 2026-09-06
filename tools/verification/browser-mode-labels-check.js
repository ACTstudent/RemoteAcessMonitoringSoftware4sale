// FLOW-06: a teacher must never be shown the collector's enum name, and
// degraded collection must not read as no collection.
const fs = require('fs'), puppeteer = require('puppeteer-core');
const config = require('./config');
const fixture = JSON.parse(fs.readFileSync(process.argv[2] || 'fixture.json', 'utf8'));

const JARGON = ['ManagedProtocol', 'WindowTitleFallback', 'Unavailable'];
let failed = 0;
const record = (ok, name, detail) => {
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

    // The history page: filter options and any rendered badges.
    await page.goto(`${config.baseUrl}/Teacher/BrowserMonitoringHistory`, { waitUntil: 'networkidle2' });

    const filterOptions = await page.$$eval('#browser-mode option', els => els.map(e => e.textContent.trim()));
    const jargonInFilter = filterOptions.filter(o => JARGON.includes(o));
    record(jargonInFilter.length === 0, 'the mode filter offers plain labels',
      JSON.stringify(filterOptions));

    const bodyText = await page.evaluate(() => document.body.innerText);
    const jargonOnPage = JARGON.filter(j => bodyText.includes(j));
    record(jargonOnPage.length === 0, 'no enum name appears anywhere on the history page',
      jargonOnPage.length ? 'found ' + jargonOnPage.join(', ') : '');

    // The monitoring page: the label map the live tiles read from.
    await page.goto(`${config.baseUrl}/Teacher/Monitoring`, { waitUntil: 'networkidle2' });
    const labelMap = await page.evaluate(() => {
      const el = document.getElementById('browserModeLabels');
      if (!el) return null;
      try { return JSON.parse(el.textContent); } catch { return 'unparseable'; }
    });
    record(labelMap && labelMap !== 'unparseable' && Object.keys(labelMap).length === 3,
      'the monitoring page carries a label for every mode',
      labelMap ? Object.keys(labelMap).join(', ') : 'block missing');

    if (labelMap && typeof labelMap === 'object') {
      const fallback = labelMap.WindowTitleFallback || {};
      record(/not idle/i.test(fallback.explanation || ''),
        'degraded collection explicitly says it is not idle',
        (fallback.explanation || '').slice(0, 70));
      record(!JARGON.includes(fallback.label || ''),
        'the degraded label is not the enum name', fallback.label || '');
    }

    // And the tile placeholder must not claim there is no activity.
    const placeholder = await page.evaluate(() => {
      const el = document.querySelector('[id^="browser-"]');
      return el ? el.textContent.trim() : 'no tile rendered yet';
    });
    record(!/no activity|idle|none/i.test(placeholder),
      'the tile placeholder does not claim there is no activity', placeholder);

  } finally {
    await browser.close();
  }
  console.log(`\n${failed} failed`);
  process.exit(failed ? 1 : 0);
})();
