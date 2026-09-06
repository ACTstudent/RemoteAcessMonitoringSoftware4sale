// UX-04: a rejected form must come back with what the user typed still in it.
//
// Before this, 53 validation failures redirected to the list and none returned
// the view with the model, so a duplicate username emptied the whole dialog.
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

(async () => {
  const b = await puppeteer.launch({ executablePath: chrome, headless: 'new',
    args: ['--ignore-certificate-errors', '--no-sandbox'], defaultViewport: { width: 1440, height: 900 } });
  const ctx = await b.createBrowserContext();
  const page = await ctx.newPage();

  await page.goto(BASE + '/Account/Login', { waitUntil: 'networkidle2' });
  await page.type('#loginUsername', config.admin.user);
  await page.type('#loginPassword', config.admin.password);
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click('button[type="submit"]')]);

  // A teacher whose username is already taken - the fixture teacher's.
  await page.goto(BASE + '/Admin/Teachers', { waitUntil: 'networkidle2' });
  const typed = {
    FirstName: 'Preserved',
    LastName: 'Submission Probe',
    Username: fixture.teacher.username,       // already taken, so this is rejected
    Email: 'preserve-probe@example.invalid',
    ContactNumber: '09171234567',
    PasswordHash: 'ShouldNotComeBack-2026!'
  };

  await page.evaluate(t => {
    const form = document.querySelector('form[action="/Admin/CreateTeacher"]');
    Object.keys(t).forEach(name => {
      const field = form.querySelector(`[name="${name}"]`);
      if (field) field.value = t[name];
    });
    form.submit();
  }, typed);
  await page.waitForNavigation({ waitUntil: 'networkidle2' }).catch(() => {});
  await new Promise(r => setTimeout(r, 1200));

  const landed = page.url().replace(BASE, '');
  console.log(`  submitted a duplicate username, landed on ${landed}\n`);

  const state = await page.evaluate(() => {
    const form = document.querySelector('form[action="/Admin/CreateTeacher"]');
    const read = n => {
      const el = form && form.querySelector(`[name="${n}"]`);
      return el ? el.value : null;
    };
    const modal = form && form.closest('.modal');
    return {
      firstName: read('FirstName'),
      lastName: read('LastName'),
      username: read('Username'),
      email: read('Email'),
      contact: read('ContactNumber'),
      password: read('PasswordHash'),
      modalOpen: !!(modal && modal.classList.contains('show')),
      focused: document.activeElement ? document.activeElement.getAttribute('name') : null,
      errorShown: /already|exists|taken|in use/i.test(document.body.textContent || ''),
      payloadInDom: (document.getElementById('preservedSubmission') || {}).textContent || ''
    };
  });

  check('the typed values come back', state.firstName === typed.FirstName && state.lastName === typed.LastName,
    `FirstName="${state.firstName}", LastName="${state.lastName}"`);
  check('the rejected username comes back so it can be corrected', state.username === typed.Username, state.username);
  check('the other fields come back too', state.email === typed.Email && state.contact === typed.ContactNumber,
    `Email="${state.email}", Contact="${state.contact}"`);
  check('the password is NOT carried back', !state.password, `password field = "${state.password}"`);
  check('the password never reaches the page at all', !state.payloadInDom.includes('ShouldNotComeBack'),
    state.payloadInDom.includes('ShouldNotComeBack') ? 'found in the payload' : 'absent from the payload');
  check('the dialog is reopened rather than left closed', state.modalOpen, 'modal show=' + state.modalOpen);
  check('focus lands on the password the user must retype', state.focused === 'PasswordHash', 'focus on ' + state.focused);
  check('the error is still reported', state.errorShown);

  // A successful submission must not leave anything behind.
  await page.goto(BASE + '/Admin/Teachers', { waitUntil: 'networkidle2' });
  const clean = await page.evaluate(() => ({
    payload: !!document.getElementById('preservedSubmission'),
    firstName: (document.querySelector('form[action="/Admin/CreateTeacher"] [name="FirstName"]') || {}).value
  }));
  check('a plain page visit carries no leftover submission', !clean.payload && !clean.firstName,
    `payload=${clean.payload}, FirstName="${clean.firstName}"`);

  await b.close();
  const failed = results.filter(r => !r).length;
  console.log(`\n${results.length - failed}/${results.length} checks passed`);
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error('ERROR ' + e.message); process.exit(2); });
