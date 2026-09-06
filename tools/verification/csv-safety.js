// REP-01 / SEC-01: a value that a spreadsheet would treat as a formula must not
// come back out of an export still able to execute. A leading =, +, - or @ is
// the payload; the safe forms are a neutralising prefix or full quoting that
// keeps the cell inert.
require('./config').acceptSelfSignedCertificate();
const fs = require('fs');
const config = require('./config');
const BASE = config.baseUrl;
const RUN = 'CSV0906';

function session() {
  const jar = new Map();
  const store = res => {
    for (const c of (res.headers.getSetCookie ? res.headers.getSetCookie() : [])) {
      const [pair] = c.split(';'); const i = pair.indexOf('=');
      jar.set(pair.slice(0, i), pair.slice(i + 1));
    }
  };
  const cookie = () => [...jar].map(([k, v]) => `${k}=${v}`).join('; ');
  return {
    async get(path) {
      const res = await fetch(BASE + path, { redirect: 'manual', headers: { cookie: cookie() } });
      store(res);
      return { status: res.status, headers: res.headers,
               body: res.status === 200 ? await res.text() : '', location: res.headers.get('location') };
    },
    async post(path, fields, tokenPage) {
      const page = await this.get(tokenPage);
      const token = (page.body.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/) || [])[1];
      const form = new URLSearchParams(fields);
      if (token) form.set('__RequestVerificationToken', token);
      const res = await fetch(BASE + path, {
        method: 'POST', redirect: 'manual',
        headers: { 'content-type': 'application/x-www-form-urlencoded', cookie: cookie() },
        body: form.toString()
      });
      store(res);
      return { status: res.status, location: res.headers.get('location') };
    },
    async login(user, pass) {
      const page = await this.get('/Account/Login');
      const token = (page.body.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/) || [])[1];
      const form = new URLSearchParams({ username: user, password: pass });
      if (token) form.set('__RequestVerificationToken', token);
      const res = await fetch(BASE + '/Account/Login', {
        method: 'POST', redirect: 'manual',
        headers: { 'content-type': 'application/x-www-form-urlencoded', cookie: cookie() },
        body: form.toString()
      });
      store(res);
      return res.status;
    }
  };
}

const results = [];
const check = (name, pass, detail) => {
  results.push(pass);
  console.log(`  ${pass ? 'PASS' : 'FAIL'}  ${name}${detail ? '\n          ' + detail : ''}`);
};

(async () => {
  const admin = session();
  if (await admin.login(config.admin.user, config.admin.password) !== 302) { console.error('sign-in failed'); process.exit(2); }

  const payload = `=HYPERLINK("http://example.invalid","clickme") ${RUN}`;
  const r = await admin.post('/Admin/CreateStudent', {
    FullName: payload, StudentNumber: `S-${RUN}`, Username: `csv${RUN}`.toLowerCase(),
    PasswordHash: `Probe-${RUN}-2026!`
  }, '/Admin/Students');
  console.log(`seeded a student whose name is a formula -> ${r.status} ${r.location || ''}\n`);

  console.log('Export endpoints as admin:');
  for (const path of ['/Admin/ExportUsageCsv', '/Admin/ExportAttendanceCsv', '/Admin/ExportRemoteCommandsCsv']) {
    const res = await admin.get(path);
    const type = res.headers.get('content-type') || '';
    const disposition = res.headers.get('content-disposition') || '';
    console.log(`  ${path.padEnd(34)} HTTP ${res.status}  ${type}  ${disposition.slice(0, 40)}`);
    if (res.status === 200) {
      check(`${path} is served as a download rather than a page`,
        /csv|octet-stream/i.test(type) || /attachment/i.test(disposition),
        `content-type "${type}", disposition "${disposition}"`);
      if (res.body.includes(RUN)) {
        const line = res.body.split(/\r?\n/).find(l => l.includes(RUN)) || '';
        const cell = (line.match(/(^|,)("?)([^,"]*|(?:[^"]|"")*)\2(?=,|$)/g) || [])
          .map(s => s.replace(/^,/, '')).find(s => s.includes(RUN)) || line;
        const startsBare = /^"?=/.test(cell.trim());
        check(`${path} does not emit the value as a live formula`, !startsBare,
          'cell begins: ' + cell.trim().slice(0, 60));
      }
    }
  }

  // The teacher alert export is the one a teacher reaches most often.
  console.log('\nAnonymous callers must not reach any export:');
  const anon = session();
  for (const path of ['/Admin/ExportUsageCsv', '/Teacher/ExportAlertsCsv', '/Teacher/ExportRemoteHistoryCsv']) {
    const res = await anon.get(path);
    check(`anonymous refused ${path}`, res.status !== 200,
      'HTTP ' + res.status + (res.location ? ' -> ' + res.location : ''));
  }

  const failed = results.filter(x => !x).length;
  console.log(`\n${results.length - failed}/${results.length} checks passed`);
  process.exit(failed ? 1 : 0);
})();
