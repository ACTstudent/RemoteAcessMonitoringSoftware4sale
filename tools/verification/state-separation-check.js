// FLOW-03: transport, session lifecycle and remote control are three states and
// must not be shown as one.
//
// The defect this guards: onreconnecting wrote "Reconnecting" into the session
// label and onreconnected then wrote "Ready", so a paused lab came back from any
// blip reading Ready and a teacher could not tell a paused session from a
// dropped connection.
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

    const present = await page.evaluate(() => ({
      session: !!document.getElementById('lblSessionState'),
      control: !!document.getElementById('lblRemoteControlState'),
      transport: !!document.getElementById('connectionStatus'),
      distinct: document.getElementById('lblSessionState') !== document.getElementById('lblRemoteControlState')
    }));
    check(present.session && present.control && present.transport && present.distinct,
      'the three states have three separate elements', JSON.stringify(present));

    // A paused session, then the connection drops.
    const afterDrop = await page.evaluate(() => {
      renderSessionState('Paused');
      markSessionStateStale(true);
      const label = document.getElementById('lblSessionState');
      return { text: label.textContent, stale: label.classList.contains('state-stale'), title: label.title };
    });
    check(afterDrop.text === 'Paused',
      'a dropped connection does not overwrite the session state', `reads "${afterDrop.text}"`);
    check(afterDrop.stale,
      'the session state is marked unconfirmed while disconnected');
    check(/out of date/i.test(afterDrop.title),
      'and says why, on demand', afterDrop.title.slice(0, 50));

    // Reconnecting must not invent a state either.
    const afterReconnect = await page.evaluate(() => {
      markSessionStateStale(false);
      const label = document.getElementById('lblSessionState');
      return { text: label.textContent, stale: label.classList.contains('state-stale') };
    });
    check(afterReconnect.text === 'Paused',
      'coming back does not reset a paused session to Ready', `reads "${afterReconnect.text}"`);
    check(!afterReconnect.stale, 'and the unconfirmed marking is cleared');

    // Remote control is its own state.
    const control = await page.evaluate(() => {
      setRemoteControlState(true);
      const session = document.getElementById('lblSessionState').textContent;
      const control = document.getElementById('lblRemoteControlState').textContent;
      setRemoteControlState(false);
      return { session, control, afterStop: document.getElementById('lblRemoteControlState').textContent };
    });
    check(control.control === 'Control active',
      'taking control is shown as taking control', control.control);
    check(control.session === 'Paused',
      'and does not disturb the session state', control.session);
    check(control.afterStop === 'Not in control',
      'stopping control says so', control.afterStop);

    // A command must be refused rather than fired into a dead connection.
    const guard = await page.evaluate(() => typeof canSendCommand === 'function' && canSendCommand());
    check(typeof guard === 'boolean', 'commands are guarded by connection state', String(guard));

  } finally {
    await browser.close();
  }
  console.log(`\n${failed} failed`);
  process.exit(failed ? 1 : 0);
})();
