// UX-04 / FLOW-03: what a page shows when its connection goes away.
//
// Driven by cutting the page off at the browser rather than by stopping the
// server, so the run does not have to restart it: Chrome's offline emulation
// severs the socket the same way a dropped Wi-Fi link does.
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

const readStatus = () => {
  const el = document.getElementById('connectionStatus');
  if (!el) return { present: false };
  return {
    present: true,
    hidden: el.hidden,
    text: (el.textContent || '').trim(),
    className: el.className,
    role: el.getAttribute('role'),
    live: el.getAttribute('aria-live')
  };
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

async function exercise(page, portal) {
  console.log(`\n=== ${portal} ===`);
  await new Promise(r => setTimeout(r, 3000));   // let the hub settle

  let status = await page.evaluate(readStatus);
  check(`${portal}: the indicator exists`, status.present);
  check(`${portal}: it is silent while connected`, status.hidden === true,
    status.hidden ? 'hidden' : `visible, showing "${status.text}"`);
  check(`${portal}: it is announced politely to assistive technology`,
    status.role === 'status' && status.live === 'polite',
    `role=${status.role}, aria-live=${status.live}`);

  // Cut the connection the way a dropped network does.
  const client = await page.target().createCDPSession();
  await client.send('Network.enable');
  await client.send('Network.emulateNetworkConditions',
    { offline: true, latency: 0, downloadThroughput: 0, uploadThroughput: 0 });

  let sawSomething = null;
  for (let waited = 0; waited < 30000; waited += 1000) {
    await new Promise(r => setTimeout(r, 1000));
    status = await page.evaluate(readStatus);
    if (!status.hidden && status.text) { sawSomething = { ...status, waited }; break; }
  }

  check(`${portal}: going offline is shown to the user`, sawSomething !== null,
    sawSomething ? `after ${sawSomething.waited / 1000}s: "${sawSomething.text}" (${sawSomething.className})`
                 : 'still hidden after 30s');
  if (sawSomething) {
    check(`${portal}: the message names a recovery path or says it is retrying`,
      /reconnect|connecting/i.test(sawSomething.text), sawSomething.text);
  }

  // Restore the network and let automatic reconnection do its work.
  await client.send('Network.emulateNetworkConditions',
    { offline: false, latency: 0, downloadThroughput: -1, uploadThroughput: -1 });

  let recovered = false;
  for (let waited = 0; waited < 40000; waited += 2000) {
    await new Promise(r => setTimeout(r, 2000));
    status = await page.evaluate(readStatus);
    if (status.hidden) { recovered = true; break; }
  }
  check(`${portal}: the indicator clears once the connection is back`, recovered,
    recovered ? 'hidden again' : `still showing "${status.text}"`);
}

(async () => {
  const b = await puppeteer.launch({ executablePath: chrome, headless: 'new',
    args: ['--ignore-certificate-errors', '--no-sandbox'], defaultViewport: { width: 1440, height: 900 } });

  const teacher = await signIn(b, fixture.teacher.username, fixture.teacher.password);
  await teacher.goto(BASE + '/Teacher/Dashboard', { waitUntil: 'networkidle2' });
  await exercise(teacher, 'teacher');

  const student = await signIn(b, fixture.student.username, fixture.student.password);
  await student.goto(BASE + '/Student/Index', { waitUntil: 'networkidle2' });
  await exercise(student, 'student');

  await b.close();
  const failed = results.filter(r => !r).length;
  console.log(`\n${results.length - failed}/${results.length} checks passed`);
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error('ERROR ' + e.message); process.exit(2); });
