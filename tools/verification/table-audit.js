// UX-03: table density and the responsive wrapper.
//
// A table that is not inside .table-responsive pushes the whole page sideways
// on a narrow screen instead of scrolling within its own box.
const fs = require('fs'), path = require('path');
const REPO = path.resolve(__dirname, '..', '..');
const ROOT = path.join(REPO, 'Monitoring And Remote Access', 'Server', 'Views');

function walk(dir) {
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...walk(full));
    else if (entry.name.endsWith('.cshtml')) out.push(full);
  }
  return out;
}

const unwrapped = [], densities = {};

for (const file of walk(ROOT)) {
  const text = fs.readFileSync(file, 'utf8');
  const rel = path.relative(ROOT, file).replace(/\\/g, '/');

  for (const match of text.matchAll(/<table[^>]*class="([^"]*)"[^>]*>/g)) {
    densities[match[1]] = (densities[match[1]] || 0) + 1;

    // Look back for a wrapper opened and not yet closed before this table.
    const before = text.slice(0, match.index);
    const lastWrapper = before.lastIndexOf('table-responsive');
    if (lastWrapper === -1) {
      unwrapped.push({ file: rel, classes: match[1], line: before.split('\n').length });
      continue;
    }
    // Crude but sufficient: the wrapper must be the nearest thing before it.
    const between = before.slice(lastWrapper);
    if ((between.match(/<\/div>/g) || []).length > (between.match(/<div/g) || []).length) {
      unwrapped.push({ file: rel, classes: match[1], line: before.split('\n').length });
    }
  }
}

console.log('table density in use:');
Object.entries(densities).sort((a, b) => b[1] - a[1])
  .forEach(([c, n]) => console.log(`  ${String(n).padStart(3)}  ${c}`));

console.log(`\ntables not inside .table-responsive: ${unwrapped.length}`);
unwrapped.forEach(u => console.log(`  ${u.file}:${u.line}  class="${u.classes}"`));
