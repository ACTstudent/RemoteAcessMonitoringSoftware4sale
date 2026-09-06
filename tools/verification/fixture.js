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
  // Read the id out of the very select the class form posts, rather than
  // scraping the teacher list. The old regex ran against /Admin/Teachers,
  // stopped matching when that markup changed, and failed silently - which
  // produced a class with no teacher. That class is then filtered out of the
  // roster's assignment dropdown (Admin/Students only offers classes that have
  // an active teacher), so checks that depend on moving a student between
  // classes had nothing to work with and reported hollow passes.
  const classesPage = await get('/Admin/Classes');
  const teacherId = (classesPage.body.match(
    new RegExp('<option value="(\\d+)"[^>]*>[^<]*' + RUN)) || [])[1];
  created.teacherId = teacherId || null;
  if (!teacherId) {
    console.log('  WARNING: no teacher id found. The class will have no teacher, and');
    console.log('           it will not appear in the roster assignment dropdown.');
  }

  created.className = `Class ${RUN}`;
  r = await post('/Admin/CreateClass', {
    ClassName: created.className, Section: 'A', Subject: 'Computer Education',
    GradeLevel: '6', AcademicYear: '2026-2027', Schedule: 'MWF 08:00',
    TeacherId: teacherId || ''
  }, '/Admin/Classes');
  console.log(`create class    -> ${r.status} ${r.location || ''}`);

  // A second class, because one class cannot exercise a move. Anything that
  // asks "did moving a student between classes behave" needs somewhere to move
  // them to.
  created.secondClassName = `Class ${RUN}-B`;
  r = await post('/Admin/CreateClass', {
    ClassName: created.secondClassName, Section: 'B', Subject: 'Computer Education',
    GradeLevel: '6', AcademicYear: '2026-2027', Schedule: 'TTh 10:00',
    TeacherId: teacherId || ''
  }, '/Admin/Classes');
  console.log(`create class B  -> ${r.status} ${r.location || ''}`);

  // A class this teacher does not own, so a scoping check has a genuine
  // negative to test against.
  created.foreignClassName = `Class ${RUN}-Foreign`;
  r = await post('/Admin/CreateClass', {
    ClassName: created.foreignClassName, Section: 'Z', Subject: 'Computer Education',
    GradeLevel: '6', AcademicYear: '2026-2027', Schedule: 'F 14:00',
    TeacherId: ''
  }, '/Admin/Classes');
  console.log(`create foreign class -> ${r.status} ${r.location || ''}`);

  // Record the ids. Hard-coding class 1 was wrong on any database that had seen
  // more than one fixture run: the check then asked a teacher about someone
  // else's class, got a correct refusal, and reported it as a failure.
  const classList = await get('/Admin/Classes');
  // In this table the class name is rendered before the link that carries its
  // id, so a row's id is the first ClassDetails link *after* its name. Looking
  // backwards instead returns the previous row's id, which swaps the two ids
  // and inverts every scoping result - checked against the database rather than
  // guessed, because both directions look plausible from the markup.
  const links = [...classList.body.matchAll(/ClassDetails\/(\d+)/g)]
    .map(m => ({ id: Number(m[1]), at: m.index }));
  const idOf = name => {
    const at = classList.body.indexOf(name);
    if (at === -1) return null;
    const following = links.find(l => l.at > at);
    return following ? following.id : null;
  };
  created.classIds = [...new Set(links.map(l => l.id))];
  created.ownClassId = idOf(created.className);
  created.foreignClassId = idOf(created.foreignClassName);
  if (!created.ownClassId) {
    console.log('  WARNING: could not resolve the teacher\'s own class id.');
  }

  created.station = `LAB-${RUN}`;
  r = await post('/Admin/CreateComputer', {
    LaboratoryStation: created.station, Status: 'Available', AssignedTo: ''
  }, '/Admin/Computers');
  console.log(`create computer -> ${r.status} ${r.location || ''}`);

  fs.writeFileSync(OUT, JSON.stringify(created, null, 1));
  console.log('\nwrote ' + OUT);
})();
