// Creates a restriction rule through the teacher portal so the policy path can
// be tested with something in it, rather than proving only that an empty set
// arrives.
require('./config').acceptSelfSignedCertificate();
const config = require('./config');
const BASE = config.baseUrl;

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

(async () => {
  const teacher = session();
  const status = await teacher.login(fixture.teacher.username, fixture.teacher.password);
  if (status !== 302) { console.error('teacher sign-in failed: ' + status); process.exit(2); }

  const startSession = false;
  if (startSession) {
    const r = await teacher.post('/Teacher/StartSession', { studentId: 1, computerId: 1 }, '/Teacher/Sessions');
    console.log('start session for student 1 -> ' + r.status + ' ' + (r.location || ''));
  }
  for (const rule of [
    { RuleType: 'Website', Mode: 'Block', Target: 'harness-global.example', Description: 'seeded for the policy test' },
    { RuleType: 'Application', Mode: 'Block', Target: 'harness-global.exe', Description: 'seeded for the policy test' }
  ]) {
    const r = await teacher.post('/Admin/CreateRestriction', rule, '/Admin/Restrictions');
    console.log(`create ${rule.RuleType} ${rule.Target} -> ${r.status} ${r.location || ''}`);
  }
})();
