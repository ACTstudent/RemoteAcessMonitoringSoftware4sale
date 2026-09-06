// A file can hold several <class> blocks; take the union of its line hits,
// not the worst single class, or a compiler-generated 0% class hides the rest.
const fs = require('fs');
const x = fs.readFileSync(process.argv[2], 'utf8');
const per = {};
const re = /<class\s[^>]*filename="([^"]+)"[^>]*>([\s\S]*?)<\/class>/g;
let m;
while ((m = re.exec(x)) !== null) {
  const f = m[1], body = m[2];
  const p = per[f] || (per[f] = { hit: new Set(), all: new Set(), bt: 0, bc: 0 });
  for (const l of body.matchAll(/<line number="(\d+)" hits="(\d+)"/g)) {
    p.all.add(l[1]);
    if (+l[2] > 0) p.hit.add(l[1]);
  }
  for (const b of body.matchAll(/condition-coverage="[^"]*\((\d+)\/(\d+)\)"/g)) {
    p.bc += +b[1]; p.bt += +b[2];
  }
}
const isView = f => f.endsWith('.cshtml');
const isMigration = f => f.indexOf('Migrations') !== -1;
const rows = Object.entries(per)
  .filter(([f]) => !isView(f) && !isMigration(f))
  .map(([f, p]) => ({
    f, ln: p.all.size,
    lr: p.all.size ? p.hit.size / p.all.size : 1,
    br: p.bt ? p.bc / p.bt : null, bt: p.bt
  }))
  .sort((a, b) => a.lr - b.lr);

console.log('non-view, non-migration C# files: ' + rows.length);
console.log('\nlowest 20 by line coverage:');
rows.slice(0, 20).forEach(r => console.log(
  ('  ' + (r.lr * 100).toFixed(0) + '%').padEnd(7) +
  ('br ' + (r.br === null ? 'n/a' : (r.br * 100).toFixed(0) + '%')).padEnd(10) +
  (r.ln + ' lines').padEnd(12) + r.f));

const tot = rows.reduce((a, r) => ({ h: a.h + Math.round(r.lr * r.ln), n: a.n + r.ln }), { h: 0, n: 0 });
const btot = rows.reduce((a, r) => ({ c: a.c + Math.round((r.br || 0) * r.bt), t: a.t + r.bt }), { c: 0, t: 0 });
console.log('\naggregate over these files: ' + (tot.h / tot.n * 100).toFixed(1) + '% lines, ' +
  (btot.c / btot.t * 100).toFixed(1) + '% branches (' + tot.n + ' lines, ' + btot.t + ' branches)');

// Views and migrations, reported separately so they are not silently mixed in.
const views = Object.entries(per).filter(([f]) => isView(f));
const vt = views.reduce((a, [, p]) => ({ h: a.h + p.hit.size, n: a.n + p.all.size }), { h: 0, n: 0 });
console.log('Razor views: ' + views.length + ' files, ' + (vt.h / vt.n * 100).toFixed(1) +
  '% lines from unit tests (views are exercised by the browser lane, not here)');

const csv = ['File,LineRatePct,BranchRatePct,Lines,Branches'];
rows.forEach(r => csv.push([r.f.replace(/\\/g, '/'), (r.lr * 100).toFixed(1),
  r.br === null ? '' : (r.br * 100).toFixed(1), r.ln, r.bt].join(',')));
fs.writeFileSync(process.argv[3], csv.join('\n') + '\n');
console.log('\nwrote ' + process.argv[3]);
