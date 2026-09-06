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

  console.log('Class-scoped pages, before the class belongs to this teacher:');
  const beforeAnalytics = await teacher.get('/Teacher/ClassAnalytics/1');
  const beforeDetails = await teacher.get('/Teacher/ClassDetails/1');
  check('ClassAnalytics refuses a class owned by nobody else',
    beforeAnalytics.status !== 200, 'HTTP ' + beforeAnalytics.status);
  check('ClassDetails refuses the same class',
    beforeDetails.status !== 200, 'HTTP ' + beforeDetails.status);

  // Assign the class through the admin portal, then ask again as the teacher.
  const admin = session();
  r = await admin.login(config.admin.user, config.admin.password);
  if (r.status !== 302) { console.error('admin sign-in failed: ' + r.status); process.exit(2); }
  const assign = await admin.post('/Admin/AssignTeacher',
    { classId: 1, teacherId: fixture.teacherIdResolved || 1 }, '/Admin/Classes');
  console.log(`\n  admin assigned the class -> ${assign.status} ${assign.location || ''}\n`);

  console.log('The same two URLs, after the class belongs to this teacher:');
  const afterAnalytics = await teacher.get('/Teacher/ClassAnalytics/1');
  const afterDetails = await teacher.get('/Teacher/ClassDetails/1');
  check('ClassAnalytics now renders for the owning teacher',
    afterAnalytics.status === 200, 'HTTP ' + afterAnalytics.status);
  check('ClassDetails now renders for the owning teacher',
    afterDetails.status === 200, 'HTTP ' + afterDetails.status);

  // A teacher must not reach admin-only surfaces at any point.
  console.log('\nAdmin-only surfaces, as the teacher:');
  for (const path of ['/Admin/Index', '/Admin/Students', '/Admin/Teachers', '/Admin/AuditLogs',
                      '/Admin/SystemLogs', '/Admin/Database', '/Admin/Deployment', '/Admin/Roles']) {
    const res = await teacher.get(path);
    const refused = res.status !== 200;
    check(`teacher cannot open ${path}`, refused,
      'HTTP ' + res.status + (res.location ? ' -> ' + res.location : ''));
  }

  const failed = results.filter(x => !x).length;
  console.log(`\n${results.length - failed}/${results.length} scope checks passed`);
  process.exit(failed ? 1 : 0);
})();
