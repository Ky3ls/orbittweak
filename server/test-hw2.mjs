// Checks the key binding of worker.js (first redeem, same PC, Windows reinstall, other PC, expired key) without Cloudflare:
// env.DB is an in-memory SQLite (node:sqlite) behind the D1 calls the worker uses (prepare/bind/first/all/run).
//   node --no-warnings server/test-hw2.mjs        (Node 22+)
import { DatabaseSync } from 'node:sqlite';
import { readFileSync } from 'node:fs';
import worker from './worker.js';

const dir = new URL('.', import.meta.url);
function fakeD1(schema) {
  const db = new DatabaseSync(':memory:');
  db.exec(schema);
  return {
    raw: db,
    prepare(sql) {
      let args = [];
      const st = {
        bind(...a) { args = a.map(v => v === undefined ? null : v); return st; },
        async first() { return db.prepare(sql).get(...args) ?? null; },
        async all() { return { results: db.prepare(sql).all(...args) }; },
        async run() { const r = db.prepare(sql).run(...args); return { meta: { changes: r.changes } }; }
      };
      return st;
    }
  };
}

let failed = 0, passed = 0;
const check = (ok, what) => { if (ok) passed++; else { failed++; console.log('FAIL  ' + what); } };
const H = c => c.repeat(64);   // a valid 64-hex hash
const call = async (env, method, path, { body, token, hwid, hw2 } = {}) => {
  const headers = { 'content-type': 'application/json' };
  if (token) headers.authorization = 'Bearer ' + token;
  if (hwid) headers['x-hwid'] = hwid;
  if (hw2) headers['x-hw2'] = hw2;
  const r = await worker.fetch(new Request('https://api.test' + path, { method, headers, body: body ? JSON.stringify(body) : undefined }), env);
  return { status: r.status, ...(await r.json()) };
};
const redeem = (env, code, hwid, hw2) => call(env, 'POST', '/api/redeem', { body: { code, hwid, hw2 } });
const me = (env, token, hwid, hw2) => call(env, 'GET', '/api/me', { token, hwid, hw2 });
const row = (env, code) => env.DB.raw.prepare('SELECT * FROM codes WHERE code = ?').get(code);
const add = (env, code, extra = '') => env.DB.raw.exec(`INSERT INTO codes (code, role, note, created_at${extra ? ', days, expires_at' : ''}) VALUES ('${code}', 'user', 'Test', 1${extra})`);

// ---------- new database (schema.sql) ----------
{
  const env = { DB: fakeD1(readFileSync(new URL('schema.sql', dir), 'utf8')), TOKEN_SECRET: 'test-secret' };
  const K = 'ORB-AAAA-BBBB-CCCC', A = H('a'), A2 = H('b'), B = H('c'), HWA = H('1'), HWB = H('2');
  add(env, K);

  // first redeem binds hwid + hw2
  let r = await redeem(env, K, A, HWA);
  check(r.status === 200 && r.token && !r.rebound, 'first redeem gives a token');
  check(row(env, K).hwid === A && row(env, K).hw2 === HWA, 'first redeem stores hwid and hw2');
  const tokA = r.token;

  // same PC
  check((await redeem(env, K, A, HWA)).status === 200, 'same PC can redeem again');
  r = await me(env, tokA, A, HWA);
  check(r.status === 200 && r.role === 'user' && !r.token, 'same PC: /api/me works, no new token');

  // Windows reinstall: new hwid, same hw2 -> key moves over
  r = await redeem(env, K, A2, HWA);
  check(r.status === 200 && r.token && r.rebound === true, 'reinstall: redeem moves the key and gives a token');
  check(row(env, K).hwid === A2 && row(env, K).rebinds === 1, 'reinstall: key bound to the new hwid, rebinds = 1');
  const tokA2 = r.token;
  check((await me(env, tokA2, A2, HWA)).status === 200, 'reinstall: new token works');
  check((await me(env, tokA, A, null)).status === 401, 'old install without hw2 is out');

  // other PC: different hw2, or no hw2 at all
  r = await redeem(env, K, B, HWB);
  check(r.status === 403 && /anderen PC/.test(r.error), 'other PC (other hw2) is refused');
  check((await redeem(env, K, B, '')).status === 403, 'other PC without hw2 is refused');
  check((await redeem(env, K, B, 'x'.repeat(64))).status === 403, 'other PC with junk hw2 is refused');
  check((await me(env, tokA2, B, HWB)).status === 401, 'token copied to another PC is refused');
  check(row(env, K).hwid === A2 && row(env, K).rebinds === 1, 'refused PCs change nothing');

  // reinstall noticed on login with a kept token (settings survived): fresh token for the new hwid
  const A3 = H('d');
  r = await me(env, tokA2, A3, HWA);
  check(r.status === 200 && r.token && r.rebound === true, 'reinstall on /api/me gives a fresh token');
  check(row(env, K).hwid === A3 && row(env, K).rebinds === 2, 'reinstall on /api/me moves the key');
  check((await me(env, r.token, A3, HWA)).status === 200 && !(await me(env, r.token, A3, HWA)).token, 'fresh token works, no further token');
  check((await me(env, tokA2, A3, null)).status === 401, 'stale token without hw2 is refused');

  // unbound key (what "PC lösen" leaves): the old hardware can not pull it back
  env.DB.raw.exec(`UPDATE codes SET hwid = NULL, bound_at = NULL, hw2 = NULL WHERE code = '${K}'`);
  check((await me(env, tokA2, A3, HWA)).status === 401, 'unbound key: old token is out even with matching hw2');
  r = await redeem(env, K, B, HWB);
  check(r.status === 200 && !r.rebound && row(env, K).hw2 === HWB, 'unbound key binds to the next PC with its hw2');

  // keys bound before hw2 existed get it filled in on the next login
  const L = 'ORB-LLLL-MMMM-NNNN';
  add(env, L);
  r = await redeem(env, L, A, null);
  check(r.status === 200 && row(env, L).hw2 === null, 'old app without hw2 still redeems');
  r = await me(env, r.token, A, HWA);
  check(r.status === 200 && row(env, L).hw2 === HWA, 'hw2 filled in on the next login');
  check((await redeem(env, L, A2, HWA)).rebound === true, 'and then a reinstall moves it');

  // expired key: refused on redeem, also after a reinstall
  const E = 'ORB-EEEE-FFFF-GGGG';
  add(env, E, `, 1, ${Math.floor(Date.now() / 1000) + 3600}`);
  const tokE = (await redeem(env, E, A, HWA)).token;
  env.DB.raw.exec(`UPDATE codes SET expires_at = 100 WHERE code = '${E}'`);
  r = await redeem(env, E, A, HWA);
  check(r.status === 403 && /abgelaufen/.test(r.error), 'expired key refused on redeem');
  r = await redeem(env, E, A2, HWA);
  check(r.status === 403 && /abgelaufen/.test(r.error) && row(env, E).hwid === A, 'expired key refused after a reinstall, not moved');
  check((await me(env, tokE, A, HWA)).status === 401, 'expired key refused on login');

  // runtime still starts with the first redeem and does not restart on a move
  const D = 'ORB-DDDD-EEEE-FFFF';
  env.DB.raw.exec(`INSERT INTO codes (code, role, created_at, days) VALUES ('${D}', 'user', 1, 7)`);
  r = await redeem(env, D, A, HWA);
  const exp = row(env, D).expires_at;
  check(r.days === 7 && r.exp === exp && exp > Date.now() / 1000 + 6 * 86400, 'runtime starts on first redeem');
  r = await redeem(env, D, A2, HWA);
  check(r.rebound && r.exp === exp, 'runtime does not restart after a reinstall');

  // admin list shows the moves
  env.DB.raw.exec(`INSERT INTO codes (code, role, created_at) VALUES ('ORB-ADMN-ADMN-ADMN', 'admin', 1)`);
  const tAdm = (await redeem(env, 'ORB-ADMN-ADMN-ADMN', B, HWB)).token;
  r = await call(env, 'GET', '/api/codes', { token: tAdm, hwid: B, hw2: HWB });
  const listed = r.codes && r.codes.find(c => c.code === D);
  check(listed && listed.rebinds === 1 && listed.hw2 === undefined, 'admin list has rebinds, not hw2');
  r = await call(env, 'POST', '/api/codes/unbind', { token: tAdm, hwid: B, hw2: HWB, body: { code: D } });
  check(r.ok && row(env, D).hwid === null && row(env, D).hw2 === null, 'admin unbind clears hwid and hw2');
}

// ---------- old database (before migrate-hw2.sql): works like before, just no move ----------
{
  const old = readFileSync(new URL('schema.sql', dir), 'utf8').replace(/^\s*(hw2|rebinds)\b.*\n/gm, '').replace(/(expires_at\s+INTEGER),/, '$1');
  check(!/hw2|rebinds/.test(old.replace(/--[^\n]*/g, '')), 'old schema built without hw2 columns');
  const env = { DB: fakeD1(old), TOKEN_SECRET: 'test-secret' };
  const K = 'ORB-OOOO-LLLL-DDDD';
  add(env, K);
  let r = await redeem(env, K, H('a'), H('1'));
  check(r.status === 200 && r.token, 'old db: redeem works');
  check((await me(env, r.token, H('a'), H('1'))).status === 200, 'old db: login works');
  check((await redeem(env, K, H('b'), H('1'))).status === 403, 'old db: reinstall still needs an admin');
  env.DB.raw.exec(`INSERT INTO codes (code, role, created_at) VALUES ('ORB-ADM2-ADM2-ADM2', 'admin', 1)`);
  const tAdm = (await redeem(env, 'ORB-ADM2-ADM2-ADM2', H('a'), H('1'))).token;
  r = await call(env, 'GET', '/api/codes', { token: tAdm, hwid: H('a'), hw2: H('1') });
  check(r.codes && r.codes.length === 2 && r.codes.every(c => c.rebinds === 0), 'old db: admin list works');
  r = await call(env, 'POST', '/api/codes/unbind', { token: tAdm, hwid: H('a'), body: { code: K } });
  check(r.ok, 'old db: unbind works');
}

console.log(`${passed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
