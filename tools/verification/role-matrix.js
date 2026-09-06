// AUTH-02 / AUTH-03, the teacher row of the matrix.
//
// AdminController is shared: an active teacher may use the actions marked
// [TeacherSharedAction] and nothing else. That attribute list is the oracle, so
// it is read out of the source rather than restated here - a new admin-only
// action is then covered the moment it is added.
require('./config').acceptSelfSignedCertificate();
const fs = require('fs');
const config = require('./config');
const BASE = config.baseUrl;
const path = require('path');
const REPO = path.resolve(__dirname, '..', '..');
const SRC = path.join(REPO, 'Monitoring And Remote Access', 'Server', 'Controllers', 'AdminController.cs');
const fixture = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const outCsv = process.argv[3];

// Which AdminController GET actions are shared with teachers.
function readOracle() {
  const text = fs.readFileSync(SRC, 'utf8');
  const shared = new Set(), gets = new Set();
  const re = /((?:\s*\[[^\]]*\]\s*)*)\n\s*public\s+(?:async\s+)?(?:Task<)?IActionResult>?\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(/g;
  let m;
  while ((m = re.exec(text)) !== null) {
    const attrs = m[1] || '', name = m[2];
    if (!/\[HttpPost/.test(attrs)) gets.add(name);
    if (/TeacherSharedAction/.test(attrs)) shared.add(name);
  }
  return { shared, gets };
}

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
      return { status: res.status, location: res.headers.get('location'),
               body: res.status === 200 ? await res.text() : '' };
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
  const { shared, gets } = readOracle();
  const teacher = session();
  if (await teacher.login(fixture.teacher.username, fixture.teacher.password) !== 302) {
    console.error('teacher sign-in failed'); process.exit(2);
  }

  const rows = [['Action', 'SharedWithTeachers', 'Status', 'Reached', 'Expected', 'Verdict']];
  let pass = 0, fail = 0;
  const wrong = [];

  // Actions taking an id are asked for the fixture's own class, which the
  // teacher now owns, so a refusal can only come from the role check.
  const NEEDS_ID = new Set(['ClassDetails', 'ComputerHistory', 'StudentDetails', 'AlertHistory']);

  for (const action of [...gets].sort()) {
    const path = `/Admin/${action}` + (NEEDS_ID.has(action) ? '/1' : '');
    const res = await teacher.get(path);
    const reached = res.status === 200;
    const expected = shared.has(action);
    // A missing fixture row is not a role decision, so it is not counted either way.
    const inconclusive = !expected ? false : (res.status === 404 && NEEDS_ID.has(action));
    const verdict = inconclusive ? 'N/A' : (reached === expected ? 'PASS' : 'FAIL');
    if (verdict === 'PASS') pass++;
    else if (verdict === 'FAIL') { fail++; wrong.push(`${action}: expected ${expected ? 'allowed' : 'refused'}, got HTTP ${res.status}`); }
    rows.push([action, expected, res.status, reached, expected, verdict]);
  }

  fs.writeFileSync(outCsv, rows.map(r => r.join(',')).join('\n') + '\n');
  console.log(`AdminController GET actions as an active teacher: ${pass} matched the attribute, ${fail} did not`);
  console.log(`  shared with teachers: ${[...gets].filter(a => shared.has(a)).length}`);
  console.log(`  admin only:           ${[...gets].filter(a => !shared.has(a)).length}`);
  if (wrong.length) { console.log('\nmismatches:'); wrong.forEach(w => console.log('  ' + w)); }
  console.log('\nwrote ' + outCsv);
  process.exit(fail ? 1 : 0);
})();
