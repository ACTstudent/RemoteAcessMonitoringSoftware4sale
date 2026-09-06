// AUTH-03: a teacher's class-scoped pages must answer for their own class and
// refuse a class belonging to someone else. The same URL is requested before
// and after the class is assigned, so the only variable is ownership.
require('./config').acceptSelfSignedCertificate();
const fs = require('fs');
const config = require('./config');
const BASE = config.baseUrl;
const fixture = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));

function session() {
  const jar = new Map();
  const store = res => {
    for (const c of (res.headers.getSetCookie ? res.headers.getSetCookie() : [])) {
      const [pair] = c.split(';');
      const i = pair.indexOf('=');
      jar.set(pair.slice(0, i), pair.slice(i + 1));
    }
  };
  const cookie = () => [...jar].map(([k, v]) => `${k}=${v}`).join('; ');
  return {
    async get(path) {
      const res = await fetch(BASE + path, { redirect: 'manual', headers: { cookie: cookie() } });
      store(res);
      return { status: res.status, body: res.status === 200 ? await res.text() : '', location: res.headers.get('location') };
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
      await this.get('/Account/Login');
      const r = await this.post('/Account/Login', { username: user, password: pass }, '/Account/Login');
      return r;
    }
  };
}

const results = [];
const check = (name, pass, detail) => {
  results.push(pass);
  console.log(`  ${pass ? 'PASS' : 'FAIL'}  ${name}${detail ? '\n          ' + detail : ''}`);
};

(async () => {
  const teacher = session();
  let r = await teacher.login(fixture.teacher.username, fixture.teacher.password);
  if (r.status !== 302) { console.error('teacher sign-in failed: ' + r.status); process.exit(2); }
  console.log('teacher signed in -> ' + r.location + '\n');

  // Ids come from the fixture. Hard-coding class 1 was wrong on any database
  // that had seen more than one fixture run: it asked this teacher about a
  // different teacher's class, received a correct refusal, and reported it as
  // a failure. Five false alarms in one run is how a check stops being read.
  const ownClassId = fixture.ownClassId;
  const foreignClassId = fixture.foreignClassId;
  if (!ownClassId || !foreignClassId) {
    console.error('fixture.json has no ownClassId/foreignClassId. Rebuild it with fixture.js.');
    process.exit(2);
  }

  console.log("A class that is not this teacher's:");
  const foreignAnalytics = await teacher.get(`/Teacher/ClassAnalytics/${foreignClassId}`);
  const foreignDetails = await teacher.get(`/Teacher/ClassDetails/${foreignClassId}`);
  check('ClassAnalytics refuses a class this teacher does not own',
    foreignAnalytics.status !== 200, 'HTTP ' + foreignAnalytics.status);
  check('ClassDetails refuses the same class',
    foreignDetails.status !== 200 || /\/Teacher\/Classes$/.test(foreignDetails.location || ''),
    'HTTP ' + foreignDetails.status + (foreignDetails.location ? ' -> ' + foreignDetails.location : ''));

  console.log("\nThe teacher's own class:");
  const ownAnalytics = await teacher.get(`/Teacher/ClassAnalytics/${ownClassId}`);
  const ownDetails = await teacher.get(`/Teacher/ClassDetails/${ownClassId}`);
  check('ClassAnalytics renders for the owning teacher',
    ownAnalytics.status === 200, 'HTTP ' + ownAnalytics.status);
  check('ClassDetails renders for the owning teacher',
    ownDetails.status === 200, 'HTTP ' + ownDetails.status);

  // Some admin pages are shared with teachers on purpose - they carry
  // [TeacherSharedAction], and role-matrix.js checks every one of them against
  // that attribute. Expecting a refusal here contradicted the product and
  // produced three more false failures.
  console.log('\nAdmin pages shared with teachers by design:');
  for (const path of ['/Admin/Index', '/Admin/Students', '/Admin/Teachers']) {
    const res = await teacher.get(path);
    check(`teacher can open ${path}`, res.status === 200,
      'HTTP ' + res.status + (res.location ? ' -> ' + res.location : ''));
  }

  console.log('\nAdmin-only surfaces, as the teacher:');
  for (const path of ['/Admin/AuditLogs', '/Admin/SystemLogs', '/Admin/Database',
                      '/Admin/Deployment', '/Admin/Roles']) {
    const res = await teacher.get(path);
    const refused = res.status !== 200;
    check(`teacher cannot open ${path}`, refused,
      'HTTP ' + res.status + (res.location ? ' -> ' + res.location : ''));
  }

  const failed = results.filter(x => !x).length;
  console.log(`\n${results.length - failed}/${results.length} scope checks passed`);
  process.exit(failed ? 1 : 0);
})();
