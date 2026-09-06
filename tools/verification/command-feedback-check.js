// FLOW-04: a command's progress and its partial failure must both be visible,
// and every result must name the workstation it refers to.
//
// The state that matters is "outcome unknown". A transport error after the
// server took the command means nobody knows whether it ran, and showing that
// as "failed" invites a teacher to shut a machine down a second time.
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
    await page.goto(`${config.baseUrl}/Teacher/Monitoring`, { waitUntil: 'networkidle2' });

    check(await page.$('#commandFeed') !== null,
      'the page has somewhere to report command progress');

    const states = await page.evaluate(() => {
      const seen = {};
      const read = () => {
        const entry = document.querySelector('#commandFeed .command-entry');
        return { text: entry.textContent, cls: entry.className };
      };

      const pending = trackCommand('Lock', 'LAB-01');
      seen.pending = read();
      pending.acknowledged('the workstation locked');
      seen.acknowledged = read();

      const refused = trackCommand('Shutdown', 'LAB-02');
      refused.failed('that workstation is not in your class');
      seen.failed = read();

      const lost = trackCommand('Restart', 'LAB-03');
      lost.unknown('the connection dropped');
      seen.unknown = read();

      return seen;
    });

    check(/sending/i.test(states.pending.text) && /command-pending/.test(states.pending.cls),
      'a command in flight is shown as pending', states.pending.text);

    check(/LAB-01/.test(states.acknowledged.text) && /command-ok/.test(states.acknowledged.cls),
      'an acknowledged command names its workstation', states.acknowledged.text);

    check(/LAB-02/.test(states.failed.text) && /command-failed/.test(states.failed.cls),
      'a refused command names its workstation and reads as refused', states.failed.text);

    check(/command-unknown/.test(states.unknown.cls),
      'an unanswered command is its own state, not a failure', states.unknown.cls);

    check(/unknown/i.test(states.unknown.text) && !/refused/i.test(states.unknown.text),
      'and does not describe itself as refused', states.unknown.text);

    check(/check the workstation/i.test(states.unknown.text),
      'it tells the teacher to check before sending again', states.unknown.text.slice(-60));

    // The four states must not render identically.
    const distinct = await page.evaluate(() => {
      const colourOf = cls => {
        const el = document.createElement('li');
        el.className = `command-entry ${cls}`;
        document.body.appendChild(el);
        const colour = getComputedStyle(el).borderLeftColor;
        el.remove();
        return colour;
      };
      return {
        ok: colourOf('command-ok'),
        failed: colourOf('command-failed'),
        unknown: colourOf('command-unknown')
      };
    });
    check(distinct.unknown !== distinct.failed && distinct.unknown !== distinct.ok,
      'unknown does not look like either success or failure', JSON.stringify(distinct));

  } finally {
    await browser.close();
  }
  console.log(`\n${failed} failed`);
  process.exit(failed ? 1 : 0);
})();
