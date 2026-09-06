// Builds the run fixture through the real admin forms, so CRUD-01 is exercised
// rather than seeded behind the application's back. Runs against the isolated
// server only. Every name carries the RUN suffix so the rows are identifiable.
require('./config').acceptSelfSignedCertificate();
const fs = require('fs');

const config = require('./config');
const BASE = config.baseUrl;
const ADMIN = { user: config.admin.user, pass: config.admin.password };
const RUN = process.argv[2] || 'RUN';
const OUT = process.argv[3];

let jar = new Map();
function storeCookies(res) {
  const set = res.headers.getSetCookie ? res.headers.getSetCookie() : [];
  for (const c of set) {
    const [pair] = c.split(';');
    const idx = pair.indexOf('=');
    jar.set(pair.slice(0, idx), pair.slice(idx + 1));
  }
}
const cookieHeader = () => [...jar].map(([k, v]) => `${k}=${v}`).join('; ');

async function get(path) {
  const res = await fetch(BASE + path, { redirect: 'manual', headers: { cookie: cookieHeader() } });
  storeCookies(res);
  const body = res.status === 200 ? await res.text() : '';
  return { status: res.status, body, location: res.headers.get('location') };
}

function tokenFrom(html) {
  return (html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/) || [])[1];
}

async function post(path, fields, pageForToken) {
  const page = await get(pageForToken);
  const token = tokenFrom(page.body);
  const form = new URLSearchParams(fields);
  if (token) form.set('__RequestVerificationToken', token);
  const res = await fetch(BASE + path, {
    method: 'POST', redirect: 'manual',
    headers: { 'content-type': 'application/x-www-form-urlencoded', cookie: cookieHeader() },
    body: form.toString()
  });
  storeCookies(res);
  return { status: res.status, location: res.headers.get('location') };
}

(async () => {
  const created = { run: RUN, admin: ADMIN.user };

  const loginPage = await get('/Account/Login');
  if (loginPage.status === 429) { console.error('rate limited; wait a minute and retry'); process.exit(2); }
  const signIn = await post('/Account/Login',
    { username: ADMIN.user, password: ADMIN.pass }, '/Account/Login');
  if (signIn.status !== 302) { console.error('admin sign-in failed: ' + signIn.status); process.exit(2); }
  console.log('signed in as admin -> ' + signIn.location);

  // --- Teacher ---
  created.teacher = { username: `t1${RUN}`.toLowerCase(), password: `Teach-${RUN}-2026!` };
  let r = await post('/Admin/CreateTeacher', {
    FirstName: 'Tessa', LastName: `Teacher ${RUN}`,
    Username: created.teacher.username, PasswordHash: created.teacher.password,
    Email: `t1${RUN}@example.invalid`.toLowerCase(), ContactNumber: '0000000000', Status: 'Active'
  }, '/Admin/Teachers');
  console.log(`create teacher  -> ${r.status} ${r.location || ''}`);

  // --- Students: one ordinary, one whose name is a script payload (SEC-01) ---
  created.student = { username: `s1${RUN}`.toLowerCase(), password: `Study-${RUN}-2026!`, number: `S-${RUN}-1` };
  r = await post('/Admin/CreateStudent', {
    FullName: `Sam Student ${RUN}`, StudentNumber: created.student.number,
    Username: created.student.username, PasswordHash: created.student.password
  }, '/Admin/Students');
  console.log(`create student  -> ${r.status} ${r.location || ''}`);

  created.xssStudent = { number: `S-${RUN}-X`, name: `<img src=x onerror="window.__camsXss=1">${RUN}` };
  r = await post('/Admin/CreateStudent', {
    FullName: created.xssStudent.name, StudentNumber: created.xssStudent.number,
    Username: `x1${RUN}`.toLowerCase(), PasswordHash: `Probe-${RUN}-2026!`
  }, '/Admin/Students');
  console.log(`create xss probe student -> ${r.status} ${r.location || ''}`);

  // --- Class and workstation ---
  const teachersPage = await get('/Admin/Teachers');
  const teacherId = (teachersPage.body.match(
    new RegExp('value="(\\d+)"[^>]*>[^<]*' + RUN)) || [])[1];
  created.teacherId = teacherId || null;

  created.className = `Class ${RUN}`;
  r = await post('/Admin/CreateClass', {
    ClassName: created.className, Section: 'A', Subject: 'Computer Education',
    GradeLevel: '6', AcademicYear: '2026-2027', Schedule: 'MWF 08:00',
    TeacherId: teacherId || ''
  }, '/Admin/Classes');
  console.log(`create class    -> ${r.status} ${r.location || ''}`);

  created.station = `LAB-${RUN}`;
  r = await post('/Admin/CreateComputer', {
    LaboratoryStation: created.station, Status: 'Available', AssignedTo: ''
  }, '/Admin/Computers');
  console.log(`create computer -> ${r.status} ${r.location || ''}`);

  fs.writeFileSync(OUT, JSON.stringify(created, null, 1));
  console.log('\nwrote ' + OUT);
})();
