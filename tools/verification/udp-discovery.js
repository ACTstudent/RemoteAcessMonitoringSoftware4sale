// NET-01, discovery half: listen on UDP 5001 the way the Windows agent does and
// prove the server's advertisement actually reaches the wire, with a payload a
// client could use. ServerDiscoveryService was measured at 0% coverage - nothing
// had ever executed it.
const dgram = require('dgram');
const socket = dgram.createSocket({ type: 'udp4', reuseAddr: true });
const seen = [];
const LISTEN_MS = 11000;   // the server advertises every 3 s

socket.on('error', e => { console.error('listener error: ' + e.message); process.exit(2); });

socket.on('message', (msg, rinfo) => {
  const text = msg.toString('utf8');
  let parsed = null;
  try { parsed = JSON.parse(text); } catch { /* recorded as unparseable below */ }
  seen.push({ from: `${rinfo.address}:${rinfo.port}`, bytes: msg.length, text, parsed });
});

socket.bind(5001, () => {
  socket.setBroadcast(true);
  console.log('listening on UDP 5001 for ' + (LISTEN_MS / 1000) + 's...');
});

setTimeout(() => {
  socket.close();
  console.log(`\n${seen.length} advertisement(s) received`);
  if (!seen.length) {
    console.log('FAIL: nothing arrived on UDP 5001');
    process.exit(1);
  }

  const first = seen[0];
  console.log(`  from      ${first.from}`);
  console.log(`  bytes     ${first.bytes}`);
  console.log(`  payload   ${first.text}`);

  const checks = [];
  const check = (name, ok, detail) => { checks.push(ok); console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? ' — ' + detail : ''}`); };

  console.log('');
  check('the payload is JSON', !!first.parsed);
  if (first.parsed) {
    const url = first.parsed.serverUrl;
    const product = first.parsed.appName;
    check('it names the product', /CAMS/i.test(product || ''), 'product=' + product);
    check('it advertises an https hub url', /^https:\/\/.+\/remoteMonitoringHub$/.test(url || ''), url);
    check('the url carries a routable LAN address, not loopback',
      !!url && !/127\.0\.0\.1|localhost/.test(url), url);
    check('it advertises the port the server is actually on', /:5100\//.test(url || ''), url);
  }
  // Every advertisement in the window should be identical while the address is stable.
  const distinct = new Set(seen.map(s => s.text));
  check('repeat advertisements are consistent', distinct.size === 1, distinct.size + ' distinct payload(s)');
  check('it repeats on the documented ~3s interval', seen.length >= 2, seen.length + ' in ' + (LISTEN_MS / 1000) + 's');

  const failed = checks.filter(c => !c).length;
  console.log(`\n${checks.length - failed}/${checks.length} checks passed`);
  process.exit(failed ? 1 : 0);
}, LISTEN_MS);
