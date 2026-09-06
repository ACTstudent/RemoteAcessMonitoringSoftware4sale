// CODE-06: which CSS class selectors are actually reachable.
//
// A plain "is this class name in a view" search is not enough and would delete
// live rules. Class names are built at runtime in three ways here:
//   classList.add("thing")           - a literal, findable
//   className = "badge " + variant   - a fragment, only the prefix is findable
//   `badge-${x}` / "bg-" + status    - a template, nothing findable
// So anything whose name is a *prefix* of a fragment in the scripts is treated
// as reachable, and the report says which ones are only reachable that way.
const fs = require('fs'), path = require('path');
const REPO = path.resolve(__dirname, '..', '..');
const SRV = path.join(REPO, 'Monitoring And Remote Access', 'Server');

const walk = (dir, out = []) => {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (!/^(bin|obj|lib)$/i.test(e.name)) walk(p, out); }
    else out.push(p);
  }
  return out;
};

const all = walk(SRV);
const cssFiles = all.filter(f => /wwwroot[\\/]css[\\/].*\.css$/.test(f));
const consumers = all.filter(f => /\.(cshtml|js)$/.test(f) && !/wwwroot[\\/]lib[\\/]/.test(f));
const consumerText = consumers.map(f => fs.readFileSync(f, 'utf8')).join('\n');

// Every class name the stylesheets define.
const defined = new Map();
for (const file of cssFiles) {
  const css = fs.readFileSync(file, 'utf8');
  // Strip comments so a commented-out rule is not counted as defined, and
  // url(...) so a file extension is not mistaken for a class - "inter.woff2"
  // was being reported as an unused .woff2 selector.
  const stripped = css
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/url\([^)]*\)/g, 'url()');
  for (const m of stripped.matchAll(/\.(-?[_a-zA-Z][\w-]*)/g)) {
    const name = m[1];
    if (!defined.has(name)) defined.set(name, new Set());
    defined.get(name).add(path.basename(file));
  }
}

// String fragments in scripts that could be concatenated into a class name.
//
// The first version of this required the whole quoted string to be a single
// token ending in a hyphen, and so missed
//     el.className = "connection-status connection-status-" + state;
// where the fragment is the second word. It reported two live rules as
// unreferenced. Now every whitespace-separated token inside any quoted string
// is considered, and a trailing hyphen makes it a prefix.
const fragments = new Set();
for (const m of consumerText.matchAll(/(["'`])((?:[^"'`\\\n]|\\.)*)\1/g)) {
  for (const token of m[2].split(/\s+/)) {
    if (/^[\w-]+-$/.test(token)) fragments.add(token);
  }
}

const usedLiterally = new Set();
for (const m of consumerText.matchAll(/[\w-]+/g)) usedLiterally.add(m[0]);

const unreferenced = [];
const onlyViaFragment = [];

for (const [name, files] of defined) {
  if (usedLiterally.has(name)) continue;
  const viaFragment = [...fragments].some(f => name.startsWith(f));
  if (viaFragment) { onlyViaFragment.push({ name, files: [...files] }); continue; }
  unreferenced.push({ name, files: [...files] });
}

console.log(`${defined.size} class selectors defined across ${cssFiles.length} stylesheet(s)`);
console.log(`${defined.size - unreferenced.length - onlyViaFragment.length} referenced literally`);
console.log(`${onlyViaFragment.length} reachable only through a built-up name - NOT safe to delete`);
console.log(`${unreferenced.length} with no reference found\n`);

if (onlyViaFragment.length) {
  console.log('Reachable only via a runtime-built name:');
  onlyViaFragment.forEach(u => console.log(`  ${u.name}  [${u.files.join(', ')}]`));
  console.log();
}
console.log('No reference found (candidates, still need reading):');
unreferenced.forEach(u => console.log(`  ${u.name}  [${u.files.join(', ')}]`));
