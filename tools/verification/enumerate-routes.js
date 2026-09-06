// Enumerates every public controller action at this commit so the authorization
// matrix is built from the code rather than from a hand-kept list that drifts.
const fs = require('fs'), path = require('path');
const REPO = path.resolve(__dirname, '..', '..');
const DIR = path.join(REPO, 'Monitoring And Remote Access', 'Server', 'Controllers');

const out = [];
for (const file of fs.readdirSync(DIR).filter(f => f.endsWith('.cs'))) {
  const text = fs.readFileSync(path.join(DIR, file), 'utf8');
  const controller = file.replace('Controller.cs', '');

  // Class-level route prefix, for the API controllers.
  const routeAttr = text.match(/\[Route\("([^"]+)"\)\]/);
  const classAuthorize = /\[Authorize/.test(text.slice(0, text.indexOf('class ')));

  const re = /((?:\s*\[[^\]]*\]\s*)*)\n\s*public\s+(?:async\s+)?(?:Task<)?(?:IActionResult|ActionResult<[^>]+>|IActionResult>)>?\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(/g;
  let m;
  while ((m = re.exec(text)) !== null) {
    const attrs = m[1] || '';
    const name = m[2];
    const isPost = /\[HttpPost/.test(attrs);
    const explicitRoute = attrs.match(/\[Http(?:Post|Get)\("([^"]+)"\)\]/);
    const url = routeAttr
      ? '/' + routeAttr[1].replace('[controller]', controller.toLowerCase()) +
        (explicitRoute ? '/' + explicitRoute[1] : '/' + name)
      : `/${controller}/${name}`;
    out.push({
      controller, action: name,
      method: isPost ? 'POST' : 'GET',
      url,
      authorize: classAuthorize || /\[Authorize/.test(attrs),
      allowAnonymous: /\[AllowAnonymous\]/.test(attrs)
    });
  }
}

out.sort((a, b) => (a.controller + a.action).localeCompare(b.controller + b.action));
const gets = out.filter(r => r.method === 'GET');
const posts = out.filter(r => r.method === 'POST');
console.log(`${out.length} actions across ${new Set(out.map(r => r.controller)).size} controllers`);
console.log(`  GET  ${gets.length}`);
console.log(`  POST ${posts.length}`);
fs.writeFileSync(process.argv[2], JSON.stringify(out, null, 1));
console.log('wrote ' + process.argv[2]);
for (const c of [...new Set(out.map(r => r.controller))]) {
  const rows = out.filter(r => r.controller === c);
  console.log(`  ${c.padEnd(18)} ${rows.length} (${rows.filter(r => r.method === 'GET').length} GET)`);
}
