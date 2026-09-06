// A workstation whose screen stops updating must say so. It used to look
// identical to a live one - the last frame simply stayed on screen - which is
// exactly what happens when the student locks the machine or Windows raises a
// UAC prompt, because the secure desktop cannot be captured.
//
// This connects a real agent, streams a frame, then stops sending, and watches
// what the teacher's monitoring page does about it.
const fs = require('fs'), puppeteer = require('puppeteer-core');
const { spawn } = require('child_process');
const chrome = require('./config').chromePath();
const config = require('./config');
const BASE = config.baseUrl;
const fixture = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));

const results = [];
const check = (name, pass, detail) => {
  results.push(pass);
  console.log(`  ${pass ? 'PASS' : 'FAIL'}  ${name}${detail ? '\n          ' + detail : ''}`);
};

const readCards = () => [...document.querySelectorAll('.workstation-card')].map(card => {
  const column = card.closest('[id^="unit-card-"]');
  const id = column ? column.id.replace('unit-card-', '') : null;
  const time = id ? document.getElementById(`time-${id}`) : null;
  return {
    id,
    stale: card.classList.contains('workstation-stale'),
    caption: time ? time.textContent.trim() : null
  };
});

(async () => {
  const b = await puppeteer.launch({ executablePath: chrome, headless: 'new',
    args: ['--ignore-certificate-errors', '--no-sandbox'], defaultViewport: { width: 1440, height: 900 } });
  const ctx = await b.createBrowserContext();
  const page = await ctx.newPage();

  await page.goto(BASE + '/Account/Login', { waitUntil: 'networkidle2' });
  await page.type('#loginUsername', fixture.teacher.username);
  await page.type('#loginPassword', fixture.teacher.password);
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click('button[type="submit"]')]);
  await page.goto(BASE + '/Teacher/Monitoring', { waitUntil: 'networkidle2' });
  await new Promise(r => setTimeout(r, 2500));

  console.log('  starting an agent that sends one frame then goes quiet...');
  const harness = spawn(process.argv[3], ['stream-then-stop'], { stdio: 'inherit' });

  // Wait for the card to appear with a frame.
  let appeared = null;
  for (let waited = 0; waited < 25000; waited += 1000) {
    await new Promise(r => setTimeout(r, 1000));
    const cards = await page.evaluate(readCards);
    // "Waiting for live frame" is the placeholder the card starts with, so this
    // has to anchor on the real caption or it matches before any frame arrives.
    if (cards.length && cards.some(c => /^Live frame /.test(c.caption || ''))) { appeared = cards; break; }
  }
  check('the workstation appears with a live frame', appeared !== null,
    appeared ? appeared[0].caption : 'no card with a frame within 25s');
  if (appeared) {
    check('a freshly streaming workstation is not marked stale',
      appeared.every(c => !c.stale), appeared.map(c => c.stale).join(', '));
  }

  // The agent has stopped sending. Give the watcher time to notice.
  console.log('  frames have stopped; waiting for the page to notice...');
  let noticed = null;
  for (let waited = 0; waited < 20000; waited += 1000) {
    await new Promise(r => setTimeout(r, 1000));
    const cards = await page.evaluate(readCards);
    const stale = cards.find(c => c.stale);
    if (stale) { noticed = { ...stale, waited }; break; }
  }

  check('the page reports that the screen stopped updating', noticed !== null,
    noticed ? `after ${noticed.waited / 1000}s: "${noticed.caption}"` : 'still shown as live after 20s');
  if (noticed) {
    check('the caption says how long ago the last frame was',
      /last frame .*ago/i.test(noticed.caption), noticed.caption);
    check('it does not claim to know why', !/lock|crash|error|offline/i.test(noticed.caption),
      noticed.caption);
  }

  try { harness.kill(); } catch { /* already gone */ }
  await b.close();
  const failed = results.filter(r => !r).length;
  console.log(`\n${results.length - failed}/${results.length} checks passed`);
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error('ERROR ' + e.message); process.exit(2); });
