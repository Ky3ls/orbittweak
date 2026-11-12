'use strict';
/* OrbitTweak – frontend. Talks to the C# host via WebView2 postMessage;
   in a normal browser a mock backend supplies demo data. */
const $ = s => document.querySelector(s);
const $$ = s => [...document.querySelectorAll(s)];
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const ico = (id, cls = 'i') => `<svg class="${cls}"><use href="#${id}"/></svg>`;
const bytes = b => { if (b == null || b < 0) return '—'; const u = ['B', 'KB', 'MB', 'GB', 'TB']; let i = 0; while (b >= 1024 && i < 4) { b /= 1024; i++; } return (i ? b.toFixed(1) : b) + ' ' + u[i]; };
const sleep = ms => new Promise(r => setTimeout(r, ms));

const HOST = !!(window.chrome && window.chrome.webview);
let rid = 0; const pending = {};
function api(cmd, args = {}) {
  if (!HOST) return Mock.call(cmd, args);
  return new Promise((res, rej) => { const id = ++rid; pending[id] = { res, rej }; window.chrome.webview.postMessage({ id, cmd, args }); });
}
if (HOST) window.chrome.webview.addEventListener('message', e => {
  const m = e.data;
  if (m.id && pending[m.id]) { const p = pending[m.id]; delete pending[m.id]; m.ok ? p.res(m.data) : p.rej(new Error(m.error || 'Fehler')); }
  else if (m.event) onEvent(m.event, m.data);
});

const S = { tweaks: [], settings: {}, fivem: {}, freed: 0, cpuHist: [], boosting: false, gfx: null, gfxEdit: {}, servers: [], clean: [], health: null };

/* ======================= THEMES ======================= */
const THEMES = {
  inferno: ['Inferno', '#FF7A18', '#FF2E63'],
  blood: ['Blood', '#FF3B3B', '#C2185B'],
  gold: ['Gold', '#FFB224', '#FF6A1A'],
  toxic: ['Toxic', '#9BE836', '#22C55E'],
  ice: ['Ice', '#3BB4FF', '#5B6CFF'],
  nebula: ['Nebula', '#9D6BFF', '#E14BFF'],
};
const hexRgb = h => [1, 3, 5].map(i => parseInt(h.substr(i, 2), 16)).join(',');
function applyTheme(k) {
  const t = THEMES[k] || THEMES.inferno, r = document.documentElement.style;
  r.setProperty('--a', t[1]); r.setProperty('--b', t[2]);
  r.setProperty('--a-rgb', hexRgb(t[1])); r.setProperty('--b-rgb', hexRgb(t[2]));
}

/* ======================= NAV ======================= */
let curPage = 'home';
function go(p) {
  curPage = p;
  $$('.nav').forEach(n => n.classList.toggle('active', n.dataset.p === p));
  $$('.page').forEach(s => s.classList.toggle('show', s.id === 'p-' + p));
  ({ servers: () => S.servers.length || loadServers(), gfx: () => S.gfx || loadGfx(), cleaner: () => S.clean.length || scan(),
     diag: () => { S.health ? renderHealth(S.health) : runHealth(); loadLogs(); }, launcher: renderCmd, home: refreshCache })[p]?.();
}
window.go = go;

/* ======================= TOAST / FEED ======================= */
function toast(title, text = '', ok = true) {
  const t = document.createElement('div');
  t.className = 'toast' + (ok ? '' : ' err');
  t.innerHTML = `<div class="ti">${ico(ok ? 'check' : 'alert')}</div><div><b>${esc(title)}</b>${text ? `<span>${esc(text)}</span>` : ''}</div>`;
  $('#toasts').append(t);
  setTimeout(() => { t.classList.add('out'); setTimeout(() => t.remove(), 250); }, 3600);
}
function feed(text) {
  const f = $('#feed'), d = document.createElement('div');
  d.innerHTML = `<em>${new Date().toLocaleTimeString('de-DE')}</em>  ${esc(text)}`;
  f.prepend(d); while (f.children.length > 6) f.lastChild.remove();
}

/* ======================= EVENTS ======================= */
function onEvent(ev, d) {
  if (ev === 'stats') stats(d);
  else if (ev === 'log') feed(d.text);
  else if (ev === 'boost') boostProgress(d.pct, d.text);
  else if (ev === 'update') updProgress(d);
}
function stats(d) {
  $('#tbCpu').textContent = d.cpu + '%'; $('#tbRam').textContent = d.ram + '%';
  $('#mCpu').innerHTML = `${d.cpu}<small>%</small>`;
  $('#mRam').innerHTML = `${d.ram}<small>%</small>`;
  $('#mRamTxt').textContent = `${d.ramUsed.toFixed(1)} / ${d.ramTotal.toFixed(0)} GB`;
  $('#mRamBar').style.width = d.ram + '%';
  $('#cRam').innerHTML = `${d.ram}<small>%</small>`; $('#cRamBar').style.width = d.ram + '%';
  $('#cRamTxt').textContent = `${d.ramUsed.toFixed(1)} / ${d.ramTotal.toFixed(0)} GB`;
  S.cpuHist.push(d.cpu); if (S.cpuHist.length > 40) S.cpuHist.shift();
  const n = S.cpuHist.length, pts = S.cpuHist.map((v, i) => [(i + 40 - n) * 200 / 39, 58 - v / 100 * 52]);
  const line = 'M' + pts.map(p => p.map(x => x.toFixed(1)).join(',')).join(' L');
  $('#sparkL').setAttribute('d', line);
  $('#sparkF').setAttribute('d', line + ` L200,60 L${pts[0][0].toFixed(1)},60 Z`);
  fmState(d.fm);
}
function fmState(f) {
  const on = f.running;
  $('#tbDot').classList.toggle('on', on);
  $('#tbFm').textContent = f.game ? 'FIVEM · IM SPIEL' : on ? 'FIVEM · LAUNCHER' : 'FIVEM OFFLINE';
  $('#fmIco').classList.toggle('off', !on);
  $('#fmState').textContent = f.game ? 'Im Spiel' : on ? 'Launcher offen' : 'FiveM offline';
  const chip = $('#fmChip');
  chip.className = 'chip ' + (f.game ? 'g' : on ? 'w' : '');
  chip.textContent = f.game ? 'LIVE' : on ? 'LÄDT' : 'STANDBY';
  $('#fmBuild').textContent = f.build || '—';
  $('#fmPrio').textContent = f.prio || '—';
  $('#fmRam').textContent = f.game ? bytes(f.gameRam) : '—';
}
async function ping() {
  try {
    const p = await api('ping');
    $('#tbPing').textContent = p.ms ?? '--';
    $('#mPing').innerHTML = `${p.ms ?? '--'}<small>ms</small>`;
    $('#mPingTxt').textContent = p.ms == null ? 'Keine Verbindung' : `Cfx.re · ${p.ms < 40 ? 'sehr gut' : p.ms < 80 ? 'gut' : 'hoch'}`;
  } catch { }
}
async function refreshCache() { try { const r = await api('fivem.cacheSize'); $('#fmCache').textContent = r.found ? bytes(r.size) : '—'; } catch { } }

/* ======================= SCORE / BOOST ======================= */
function score() {
  const t = S.tweaks, on = t.filter(x => x.applied).length, pct = t.length ? Math.round(on * 100 / t.length) : 0;
  $('#hsTweaks').textContent = `${on}/${t.length}`;
  $('#scoreChip').textContent = 'SCORE ' + pct;
  if (!S.boosting) { setRing(pct); $('#boostSub').textContent = `SCORE ${pct}%`; }
}
function setRing(p) { $('#scoreRing').style.strokeDashoffset = 703.7 * (1 - Math.min(p, 100) / 100); }
function addFreed(b) { S.freed += b || 0; $('#hsFreed').textContent = bytes(S.freed); }
function boostProgress(pct, text) {
  setRing(pct); $('#boostSub').textContent = pct + '%'; $('#heroText').textContent = text;
  const w = $('#wizProg'); if (w) { w.style.width = pct + '%'; $('#wizTxt').textContent = text; }
}
async function boost(fromWizard) {
  if (S.boosting) return;
  S.boosting = true;
  const core = $('#boostBtn'); core.classList.add('run');
  $('#heroTitle').textContent = 'Boost läuft…';
  $('#boostIco').innerHTML = '<use href="#refresh"/>'; $('#boostIco').classList.add('spin');
  feed('Boost gestartet');
  try {
    const r = await api('boost');
    addFreed(r.freed); await loadTweaks(); setRing(100);
    $('#boostTxt').textContent = 'DONE';
    $('#heroTitle').textContent = 'Boost abgeschlossen';
    $('#heroText').textContent = r.restart ? 'Fertig. Starte den PC neu, damit alle Tweaks greifen.' : 'Fertig. Dein System ist bereit für FiveM.';
    $('#lastBoost').textContent = 'Letzter Boost: gerade eben';
    if (!fromWizard) toast('Boost abgeschlossen', `${r.applied} Tweaks · ${bytes(r.freed)} freigegeben${r.restart ? ' · Neustart empfohlen' : ''}`);
    feed(`Boost fertig: ${r.applied} Tweaks, ${bytes(r.freed)} frei`);
    return r;
  } catch (e) { toast('Boost fehlgeschlagen', e.message, false); }
  finally {
    core.classList.remove('run'); $('#boostIco').classList.remove('spin'); $('#boostIco').innerHTML = '<use href="#zap"/>';
    S.boosting = false;
    setTimeout(() => { $('#boostTxt').textContent = 'BOOST'; $('#heroTitle').textContent = 'FiveM Full Boost'; score(); }, 2600);
  }
}

/* ======================= TWEAKS ======================= */
const CATS = { fivem: ['FiveM', 'gamepad'], system: ['System', 'cpu'], network: ['Netzwerk', 'wifi'], input: ['Input', 'keyboard'] };
let curTab = 'fivem';
async function loadTweaks() { S.tweaks = await api('tweaks.list'); renderTweaks(); score(); }
function tweakHtml(t, compact) {
  const badge = t.badge ? `<span class="chip ${t.badge === 'NEUSTART' ? 'w' : 'a'}" style="margin-left:8px;height:18px;font-size:9px">${esc(t.badge)}</span>` : '';
  const body = `<div class="ti">${ico(t.icon)}</div><div class="tx"><b>${esc(t.name)}</b>${badge}<p>${esc(t.desc)}</p></div><div class="tg ${t.applied ? 'on' : ''}" data-id="${t.id}"></div>`;
  return compact ? `<div class="tw">${body}</div>` : `<div class="card hov tcard ${t.applied ? 'on' : ''}">${body}</div>`;
}
function renderTweaks() {
  $('#tabs').innerHTML = Object.entries(CATS).map(([k, [n]]) => { const l = S.tweaks.filter(t => t.cat === k); return `<button class="tab ${k === curTab ? 'on' : ''}" data-c="${k}">${n}<span class="c">${l.filter(t => t.applied).length}/${l.length}</span></button>`; }).join('');
  $('#tgrid').innerHTML = S.tweaks.filter(t => t.cat === curTab).map(t => tweakHtml(t)).join('');
  $('#inputTweaks').innerHTML = S.tweaks.filter(t => t.cat === 'input').map(t => tweakHtml(t, true)).join('');
}
document.addEventListener('click', async e => {
  const tab = e.target.closest('.tab'); if (tab) { curTab = tab.dataset.c; renderTweaks(); return; }
  const tg = e.target.closest('.tg[data-id]') || e.target.closest('.tcard,.tw')?.querySelector('.tg[data-id]'); if (!tg) return;
  const t = S.tweaks.find(x => x.id === tg.dataset.id); if (!t) return;
  const want = !t.applied;
  $$(`.tg[data-id="${t.id}"]`).forEach(x => { x.classList.toggle('on', want); x.classList.add('busy'); });
  try {
    const r = await api('tweak.set', { id: t.id, on: want }); t.applied = r.applied;
    if (r.error) toast('Fehlgeschlagen', `${t.name}: ${r.error}`, false);
    else toast(want ? 'Aktiviert' : 'Zurückgesetzt', t.name + (t.badge === 'NEUSTART' && want ? ' · Neustart nötig' : ''));
  } catch (err) { toast('Fehler', err.message, false); }
  renderTweaks(); score();
});
async function applyCat(cat) {
  const list = S.tweaks.filter(t => t.cat === cat && t.rec && !t.applied);
  if (!list.length) return toast('Schon optimal', `Alle empfohlenen ${CATS[cat][0]}-Tweaks sind aktiv`);
  for (const t of list) { try { const r = await api('tweak.set', { id: t.id, on: true }); t.applied = r.applied; } catch { } renderTweaks(); }
  score(); toast(`${CATS[cat][0]} optimiert`, `${list.length} Tweaks angewendet`);
}
async function revertAll() {
  if (!await confirmBox('Alle Tweaks zurücksetzen?', 'Windows, Netzwerk, Input und FiveM werden auf Standard zurückgesetzt.')) return;
  await api('tweaks.revertAll'); await loadTweaks(); toast('Alles zurückgesetzt', 'Neustart empfohlen');
}

/* ======================= FIVEM ACTIONS ======================= */
async function clearFmCache() { const r = await api('fivem.cache'); if (r.error) return toast('Cache nicht geleert', r.error, false); addFreed(r.freed); refreshCache(); toast('FiveM-Cache geleert', bytes(r.freed) + ' freigegeben'); }
async function cleanRam() { const r = await api('ram.clean'); addFreed(r.freed); toast('RAM freigegeben', bytes(r.freed) + ' zurückgewonnen'); }
async function killFiveM() { const r = await api('fivem.kill'); toast(r.count ? 'FiveM beendet' : 'FiveM lief nicht', r.count ? r.count + ' Prozesse geschlossen' : '', !!r.count); }
async function pickFiveM() { const r = await api('fivem.pick'); if (r.found) { S.fivem = r; renderCmd(); refreshCache(); toast('FiveM-Pfad gesetzt', r.path); } }

/* ======================= LAUNCHER ======================= */
const BUILDS = ['Auto', '2189', '2372', '2545', '2612', '2699', '2802', '2944', '3095', '3258', '3323', '3407'];
const PRE = [['ram', 'RAM freigeben', 'Working-Sets + Standby-Liste leeren'], ['dns', 'DNS-Cache leeren', 'Frische Server-Auflösung'], ['kill', 'Hintergrund-Apps beenden', 'OneDrive, Teams, Launcher & Co.'], ['cache', 'FiveM-Cache leeren', 'Nur bei Problemen (längeres Laden)'], ['prio', 'Priorität „Hoch“ setzen', 'Automatisch, sobald das Spiel läuft']];
const cfg = () => S.settings.launch ||= { build: 'Auto', pure: 0, pre: { ram: true, dns: true, kill: false, cache: false, prio: true } };
function seg(el, opts, cur, onPick) {
  el.innerHTML = opts.map(([n, v]) => `<button class="${String(v) === String(cur) ? 'on' : ''}" data-v="${v}">${n}</button>`).join('');
  el.onclick = e => { const b = e.target.closest('button'); if (!b) return; el.querySelectorAll('button').forEach(x => x.classList.toggle('on', x === b)); onPick(b.dataset.v); };
}
async function renderLauncher() {
  const c = cfg();
  seg($('#optBuild'), BUILDS.map(b => [b === 'Auto' ? 'AUTO' : 'b' + b, b]), c.build, v => { c.build = v; saveLaunch(); });
  seg($('#optPure'), [['Aus', 0], ['Pure 1', 1], ['Pure 2', 2]], c.pure, v => { c.pure = +v; saveLaunch(); });
  try { const ini = await api('ini.get'); seg($('#optChan'), [['Release', 'production'], ['Beta', 'beta'], ['Latest', 'canary']], ini.channel, async v => { await api('ini.set', { channel: v }); toast('Update-Kanal gesetzt', v); }); } catch { }
  $('#preList').innerHTML = PRE.map(([k, n, d]) => `<div class="pr"><div><b>${n}</b><span>${d}</span></div><div class="tg ${c.pre[k] ? 'on' : ''}" data-pre="${k}"></div></div>`).join('');
  $('#preList').onclick = e => { const t = e.target.closest('[data-pre]'); if (!t) return; c.pre[t.dataset.pre] = !c.pre[t.dataset.pre]; t.classList.toggle('on'); saveLaunch(); };
  renderCmd();
}
function saveLaunch() { api('settings.set', { key: 'launch', value: cfg() }); renderCmd(); }
function renderCmd() {
  const c = cfg(), con = ($('#lConnect')?.value || '').trim();
  let s = `"${esc((S.fivem && S.fivem.path) || '%LocalAppData%\\FiveM')}\\FiveM.exe"`;
  if (c.build !== 'Auto') s += ` <em>-b${esc(c.build)}</em>`;
  if (+c.pure) s += ` <em>-pure_${c.pure}</em>`;
  if (con) s += ` <em>+connect ${esc(con)}</em>`;
  $('#cmdLine').innerHTML = s;
}
async function launch(connect) {
  const c = cfg();
  const r = await api('fivem.launch', { build: c.build, pure: c.pure, connect: connect ?? ($('#lConnect').value || '').trim(), pre: c.pre });
  if (r.error) return toast('Start fehlgeschlagen', r.error, false);
  addFreed(r.freed); toast('FiveM wird gestartet', r.summary || 'Pre-Launch-Boost angewendet');
}

/* ======================= SERVERS ======================= */
const FIVEM_COLORS = ['#fff', '#F44336', '#4CAF50', '#FFEB3B', '#42A5F5', '#03A9F4', '#9C27B0', '#fff', '#FF5722', '#9E9E9E'];
function fivemName(s) {
  let out = '', col = null;
  for (const part of String(s || '').split(/(\^\d)/)) {
    const m = part.match(/^\^(\d)$/);
    if (m) { col = FIVEM_COLORS[+m[1]]; continue; }
    if (part) out += col ? `<span style="color:${col}">${esc(part)}</span>` : esc(part);
  }
  return out || 'Unbekannter Server';
}
function srvCard(s) {
  const pct = s.max ? Math.round(s.players * 100 / s.max) : 0;
  const initial = (s.name || '?').replace(/\^\d/g, '').trim()[0] || '?';
  return `<div class="card hov srv ${s.online ? '' : 'off'}">
    <div class="bn" ${s.banner ? `style="background-image:url('${esc(s.banner)}')"` : ''}></div>
    <button class="ib fav" title="Entfernen" onclick="removeServer('${esc(s.code)}')">${ico('x')}</button>
    <div class="bd">
      <div class="top"><div class="icon" ${s.icon ? `style="background-image:url('${esc(s.icon)}')"` : ''}>${s.icon ? '' : esc(initial)}</div><div style="min-width:0"><div class="nm">${fivemName(s.name)}</div></div></div>
      <div class="ds">${esc(s.desc || s.addr || '')}</div>
      <div class="tags">${s.online ? '<span class="chip g">ONLINE</span>' : '<span class="chip r">OFFLINE</span>'}${s.locale ? `<span class="chip">${esc(s.locale)}</span>` : ''}${(s.tags || []).slice(0, 3).map(t => `<span class="chip">${esc(t)}</span>`).join('')}</div>
      <div class="row">
        <div class="pl"><div><span>Spieler</span><b>${s.players ?? 0}/${s.max ?? 0}</b></div><div class="bar"><i style="width:${pct}%"></i></div></div>
        <span class="chip ${s.ping == null ? '' : s.ping < 50 ? 'g' : s.ping < 100 ? 'w' : 'r'}">${s.ping == null ? '-- ms' : s.ping + ' ms'}</span>
        <button class="btn p sm" onclick="joinServer('${esc(s.code)}')" ${s.online ? '' : 'disabled'}>${ico('play')}Join</button>
      </div>
    </div></div>`;
}
async function loadServers(force) {
  $('#srvList').innerHTML = `<div class="empty">Lade Server-Daten…</div>`;
  try { S.servers = await api('servers.list', { force: !!force }); } catch { S.servers = []; }
  renderServers(); loadCfx();
}
function renderServers() {
  $('#srvList').innerHTML = S.servers.length ? S.servers.map(srvCard).join('')
    : `<div class="empty"><div style="font-weight:600;color:var(--text)">Noch keine Favoriten</div><div style="margin-top:4px">Füge oben einen Join-Code (cfx.re/join/…) oder IP:Port hinzu.</div></div>`;
}
async function addServer() {
  const q = $('#srvIn').value.trim(); if (!q) return;
  try { const s = await api('servers.add', { q }); S.servers = S.servers.filter(x => x.code !== s.code).concat(s); renderServers(); $('#srvIn').value = ''; toast('Server hinzugefügt', String(s.name).replace(/\^\d/g, '')); }
  catch (e) { toast('Server nicht gefunden', e.message, false); }
}
async function removeServer(code) { await api('servers.remove', { code }); S.servers = S.servers.filter(x => x.code !== code); renderServers(); }
function joinServer(code) { const s = S.servers.find(x => x.code === code); launch(s.connect || code); }
function directConnect() { const v = $('#dcIn').value.trim(); if (v) launch(v); }
async function loadCfx() {
  $('#cfxList').innerHTML = '<div class="sub" style="padding:8px 0">Prüfe…</div>';
  try {
    const l = await api('cfx.status');
    $('#cfxList').innerHTML = l.map(s => `<div class="svc"><i class="dot ${s.ok ? 'on' : ''}" style="${s.ok ? '' : 'background:var(--bad)'}"></i><span class="nm">${esc(s.name)}</span><b style="font-family:var(--disp)" class="${s.ok ? '' : 'muted'}">${s.ok ? s.ms + ' ms' : 'DOWN'}</b></div>`).join('');
  } catch { $('#cfxList').innerHTML = '<div class="sub">Nicht erreichbar</div>'; }
}

/* ======================= GRAPHICS ======================= */
const Q4 = [['Normal', 0], ['Hoch', 1], ['Sehr hoch', 2], ['Ultra', 3]], Q3 = Q4.slice(0, 3);
const GFX = [
  { k: 'TextureQuality', n: 'Texturen', d: 'Abhängig vom VRAM', o: Q3 },
  { k: 'ShadowQuality', n: 'Schatten', d: 'Großer FPS-Faktor', o: [['Normal', 1], ['Hoch', 2], ['Sehr hoch', 3]] },
  { k: 'ReflectionQuality', n: 'Reflexionen', d: 'Autos und Wasser', o: Q4 },
  { k: 'GrassQuality', n: 'Gras', d: 'Sehr FPS-lastig', o: Q4 },
  { k: 'WaterQuality', n: 'Wasser', d: 'Wellen und Brechung', o: Q3 },
  { k: 'ParticleQuality', n: 'Partikel', d: 'Explosionen, Rauch', o: Q4 },
  { k: 'ShaderQuality', n: 'Shader', d: 'Beleuchtungsdetails', o: Q3 },
  { k: 'PostFX', n: 'Post-FX', d: 'Bloom, Filter, Effekte', o: Q4 },
  { k: 'SSAO', n: 'Umgebungsverdeckung', d: 'Kontaktschatten', o: [['Aus', 0], ['Normal', 1], ['Hoch', 2]] },
  { k: 'Tessellation', n: 'Tessellation', d: 'Geometrie-Details', o: [['Aus', 0], ['Normal', 1], ['Hoch', 2], ['Sehr hoch', 3]] },
  { k: 'MSAA', n: 'MSAA', d: 'Kantenglättung (teuer)', o: [['Aus', 0], ['2x', 2], ['4x', 4], ['8x', 8]] },
  { k: 'FXAA_Enabled', n: 'FXAA', d: 'Kantenglättung (günstig)', o: [['Aus', 'false'], ['An', 'true']] },
  { k: 'AnisotropicFiltering', n: 'Anisotrope Filterung', d: 'Schärfere Texturen in der Ferne', o: [['Aus', 0], ['4x', 4], ['8x', 8], ['16x', 16]] },
  { k: 'Shadow_SoftShadows', n: 'Weiche Schatten', d: 'Schattenkanten', o: [['Scharf', 0], ['Weich', 1], ['Weicher', 2], ['Max', 3]] },
  { k: 'LodScale', n: 'Distanz-Skalierung', d: 'Sichtweite von Objekten', r: 1 },
  { k: 'CityDensity', n: 'Bevölkerungsdichte', d: 'NPCs und Verkehr', r: 1 },
  { k: 'MotionBlurStrength', n: 'Bewegungsunschärfe', d: 'Für PvP auf 0', r: 1 },
];
const PRESETS = {
  comp: { n: 'COMPETITIVE', d: 'Maximale FPS und klare Sicht. Für PvP, Gangwar und schwächere PCs.', fps: 5, q: 1,
    v: { TextureQuality: 1, ShadowQuality: 1, ReflectionQuality: 0, GrassQuality: 0, WaterQuality: 0, ParticleQuality: 0, ShaderQuality: 0, PostFX: 0, SSAO: 0, Tessellation: 0, MSAA: 0, FXAA_Enabled: 'true', AnisotropicFiltering: 4, Shadow_SoftShadows: 0, LodScale: 0.6, CityDensity: 0.3, MotionBlurStrength: 0 } },
  bal: { n: 'BALANCED', d: 'Gute Optik bei stabilen FPS. Für Roleplay im Alltag.', fps: 3, q: 3,
    v: { TextureQuality: 2, ShadowQuality: 2, ReflectionQuality: 1, GrassQuality: 1, WaterQuality: 1, ParticleQuality: 1, ShaderQuality: 1, PostFX: 1, SSAO: 1, Tessellation: 1, MSAA: 0, FXAA_Enabled: 'true', AnisotropicFiltering: 16, Shadow_SoftShadows: 1, LodScale: 0.8, CityDensity: 0.6, MotionBlurStrength: 0 } },
  cine: { n: 'CINEMATIC', d: 'Maximale Qualität für Screenshots, Clips und High-End-PCs.', fps: 1, q: 5,
    v: { TextureQuality: 2, ShadowQuality: 3, ReflectionQuality: 3, GrassQuality: 2, WaterQuality: 2, ParticleQuality: 3, ShaderQuality: 2, PostFX: 3, SSAO: 2, Tessellation: 3, MSAA: 2, FXAA_Enabled: 'true', AnisotropicFiltering: 16, Shadow_SoftShadows: 3, LodScale: 1, CityDensity: 1, MotionBlurStrength: 0 } },
};
const pips = n => Array.from({ length: 5 }, (_, i) => `<i class="${i < n ? 'on' : ''}"></i>`).join('');
const num = v => v === 'true' ? 1 : v === 'false' ? 0 : parseFloat(v);
async function loadGfx() {
  try { S.gfx = await api('gfx.get'); } catch { S.gfx = { found: false, values: {} }; }
  S.gfxEdit = { ...S.gfx.values };
  $('#gfxState').className = 'chip ' + (S.gfx.found ? 'g' : 'r');
  $('#gfxState').textContent = S.gfx.found ? 'SETTINGS.XML GEFUNDEN' : 'SETTINGS.XML FEHLT';
  $('#gfxPath').textContent = S.gfx.found ? S.gfx.path : 'GTA V settings.xml nicht gefunden. Starte GTA V oder FiveM einmal.';
  $('#presets').innerHTML = Object.entries(PRESETS).map(([k, p]) => `<div class="card hov preset" data-pr="${k}"><div class="ck chip a">AKTIV</div><div class="pt">${p.n}</div><p>${p.d}</p><div class="meter"><span>FPS</span><div class="pips">${pips(p.fps)}</div><span>OPTIK</span><div class="pips">${pips(p.q)}</div></div></div>`).join('');
  $('#presets').onclick = e => { const c = e.target.closest('[data-pr]'); if (!c) return; Object.assign(S.gfxEdit, PRESETS[c.dataset.pr].v); renderGfx(true); };
  renderGfx(false);
}
function renderGfx(dirty) {
  const v = S.gfxEdit;
  $('#gctl').innerHTML = GFX.map(g => {
    const cur = v[g.k];
    if (g.r) { const n = Math.round(parseFloat(cur ?? 1) * 100); return `<div class="gc"><div class="gn"><b>${g.n}</b><span>${g.d}</span></div><div class="rng"><input type="range" min="0" max="100" value="${n}" style="--p:${n}%" data-r="${g.k}"><b>${n}%</b></div></div>`; }
    return `<div class="gc"><div class="gn"><b>${g.n}</b><span>${g.d}</span></div><div class="seg" data-g="${g.k}">${g.o.map(([n, val]) => `<button class="${String(cur) === String(val) ? 'on' : ''}" data-v="${val}">${n}</button>`).join('')}</div></div>`;
  }).join('');
  $('#gfxDirty').classList.toggle('hidden', !dirty);
  $$('.preset').forEach(p => { const pv = PRESETS[p.dataset.pr].v; p.classList.toggle('sel', Object.keys(pv).every(k => Math.abs(num(pv[k]) - num(v[k])) < .02)); });
}
document.addEventListener('click', e => { const b = e.target.closest('.gc .seg button'); if (!b) return; S.gfxEdit[b.parentElement.dataset.g] = isNaN(b.dataset.v) ? b.dataset.v : +b.dataset.v; renderGfx(true); });
document.addEventListener('input', e => { const r = e.target.closest('[data-r]'); if (!r) return; r.style.setProperty('--p', r.value + '%'); r.nextElementSibling.textContent = r.value + '%'; S.gfxEdit[r.dataset.r] = r.value / 100; $('#gfxDirty').classList.remove('hidden'); });
document.addEventListener('change', e => { if (e.target.closest('[data-r]')) renderGfx(true); });
async function gfxSave() { const r = await api('gfx.save', { values: S.gfxEdit }); if (r.error) return toast('Nicht gespeichert', r.error, false); S.gfx.values = { ...S.gfxEdit }; renderGfx(false); toast('Grafik gespeichert', 'Backup erstellt · gilt beim nächsten Start'); }
async function gfxRestore() { const r = await api('gfx.restore'); if (r.error) return toast('Kein Backup', r.error, false); await loadGfx(); toast('Backup wiederhergestellt', 'Original-Grafikeinstellungen aktiv'); }

/* ======================= INPUT LAB ======================= */
const W = { up: {}, held: new Set(), max: 0, reps: [], last: 0 };
function resetWasd() { Object.assign(W, { up: {}, held: new Set(), max: 0, reps: [], last: 0 }); $('#wsSwitch').textContent = '--'; $('#wsRepeat').textContent = '--'; $('#wsMax').textContent = '0'; }
const OPP = { KeyA: 'KeyD', KeyD: 'KeyA', KeyW: 'KeyS', KeyS: 'KeyW' };
addEventListener('keydown', e => {
  if (curPage !== 'input' || e.target.tagName === 'INPUT') return;
  const k = e.code, el = $(`.key[data-k="${k}"]`);
  if (e.repeat) {
    if (W.last) { W.reps.push(e.timeStamp - W.last); if (W.reps.length > 20) W.reps.shift(); $('#wsRepeat').textContent = Math.round(1000 / (W.reps.reduce((a, b) => a + b, 0) / W.reps.length)) + '/s'; }
    W.last = e.timeStamp; return;
  }
  W.last = e.timeStamp; W.held.add(k); W.max = Math.max(W.max, W.held.size); $('#wsMax').textContent = W.max;
  if (el) { el.classList.add('dn'); e.preventDefault(); }
  const o = OPP[k];
  if (o && W.held.has(o)) $('#wsSwitch').textContent = '0 ms';
  else if (o && W.up[o] && e.timeStamp - W.up[o] < 300) $('#wsSwitch').textContent = (e.timeStamp - W.up[o]).toFixed(1) + ' ms';
});
addEventListener('keyup', e => { W.held.delete(e.code); W.up[e.code] = e.timeStamp; W.reps = []; W.last = 0; $(`.key[data-k="${e.code}"]`)?.classList.remove('dn'); });
function initLab() {
  const P = { n: 0, max: 0, last: 0 }, pad = $('#pollPad');
  pad.addEventListener('onpointerrawupdate' in window ? 'pointerrawupdate' : 'pointermove', () => { P.n++; P.last = performance.now(); });
  setInterval(() => {
    const hz = P.n * 4; P.n = 0; if (performance.now() - P.last > 400) return;
    P.max = Math.max(P.max, hz);
    $('#pollHz').innerHTML = `${hz}<small>Hz</small>`;
    $('#pollTxt').textContent = `Spitze ${P.max} Hz · ` + (P.max >= 900 ? '1000-Hz-Maus' : P.max >= 450 ? '500-Hz-Maus' : 'Polling-Rate im Maus-Tool erhöhen');
  }, 250);
  $('#labTabs').onclick = e => { const b = e.target.closest('button'); if (!b) return; $$('#labTabs button').forEach(x => x.classList.toggle('on', x === b)); pad.classList.toggle('hidden', b.dataset.t !== 'poll'); $('#reactPad').classList.toggle('hidden', b.dataset.t !== 'react'); };
  const R = { st: 'idle', t: 0, to: 0, res: [] }, rp = $('#reactPad');
  rp.addEventListener('pointerdown', () => {
    if (R.st === 'idle' || R.st === 'done') {
      R.st = 'wait'; rp.className = 'react wait'; $('#reactMs').textContent = 'Warten…'; $('#reactTxt').textContent = 'Erst klicken, wenn es grün wird';
      R.to = setTimeout(() => { R.st = 'go'; R.t = performance.now(); rp.className = 'react go'; $('#reactMs').textContent = 'JETZT!'; }, 1200 + Math.random() * 2600);
    } else if (R.st === 'wait') { clearTimeout(R.to); R.st = 'done'; rp.className = 'react'; $('#reactMs').textContent = 'Zu früh!'; $('#reactTxt').textContent = 'Nochmal klicken'; }
    else if (R.st === 'go') {
      const ms = performance.now() - R.t; R.res.push(ms); if (R.res.length > 5) R.res.shift(); R.st = 'done'; rp.className = 'react';
      $('#reactMs').innerHTML = `${ms.toFixed(0)}<small>ms</small>`;
      $('#reactTxt').textContent = `Ø ${(R.res.reduce((a, b) => a + b, 0) / R.res.length).toFixed(0)} ms aus ${R.res.length} Versuchen · Klick für den nächsten`;
    }
  });
}

/* ======================= CLEANER ======================= */
async function scan() { $('#cTotal').textContent = '…'; $('#cTotalSub').textContent = 'wird gescannt…'; S.clean = await api('clean.scan'); renderClean(); }
function renderClean() {
  $('#cleanList').innerHTML = S.clean.map(c => `<div class="card hov crow"><div class="cb ${c.sel ? 'on' : ''}" data-cid="${c.id}"><svg viewBox="0 0 24 24"><path d="M20 6 9 17l-5-5"/></svg></div><div class="ti">${ico(c.icon || 'trash')}</div><div class="tx"><b>${esc(c.name)}</b><p>${esc(c.desc)}</p></div><div class="sz ${c.size > 0 ? '' : 'muted'}">${bytes(c.size)}</div></div>`).join('');
  const tot = S.clean.filter(c => c.sel).reduce((a, c) => a + Math.max(0, c.size), 0);
  $('#cTotal').textContent = bytes(tot); $('#cTotalSub').textContent = 'ausgewählt zum Löschen';
  $('#donutV').style.strokeDashoffset = 540.4 * (1 - Math.min(1, tot / (5 * 1073741824) + (tot > 0 ? .08 : 0)));
}
document.addEventListener('click', e => { const c = e.target.closest('[data-cid]'); if (!c) return; const t = S.clean.find(x => x.id === c.dataset.cid); t.sel = !t.sel; renderClean(); });
async function clean() {
  const ids = S.clean.filter(c => c.sel).map(c => c.id); if (!ids.length) return;
  $('#cleanBtn').disabled = true; $('#cTotalSub').textContent = 'bereinige…';
  const r = await api('clean.run', { ids });
  addFreed(r.freed); toast('Bereinigung fertig', bytes(r.freed) + ' freigegeben' + (r.skipped ? ' · FiveM-Cache übersprungen (Spiel läuft)' : ''));
  $('#cleanBtn').disabled = false; await scan(); $('#cTotalSub').textContent = bytes(r.freed) + ' gerade freigegeben';
}

/* ======================= DIAGNOSE ======================= */
async function runHealth() { $('#hList').innerHTML = '<div class="sub" style="padding:10px 0">Analysiere System…</div>'; const h = S.health = await api('health'); renderHealth(h); return h; }
function renderHealth(h) {
  $('#hScore').textContent = h.score; $('#hsHealth').textContent = h.score + '%';
  const bad = h.items.filter(i => i.st !== 'ok').length;
  $('#hScoreTxt').textContent = bad ? `${bad} Punkte können verbessert werden` : 'Alles im grünen Bereich';
  $('#hList').innerHTML = h.items.map((i, n) => `<div class="hc"><div class="st ${i.st}">${ico(i.st === 'ok' ? 'check' : 'alert')}</div><div class="tx"><b>${esc(i.name)}</b><p>${esc(i.value)}</p></div>${i.fix && i.st !== 'ok' ? `<button class="btn sm p" data-fix="${n}">Beheben</button>` : ''}</div>`).join('');
}
document.addEventListener('click', async e => {
  const b = e.target.closest('[data-fix]'); if (!b) return;
  const i = S.health.items[+b.dataset.fix]; b.disabled = true;
  const r = await api('health.fix', { fix: i.fix }); toast(r.ok ? 'Behoben' : 'Nicht behoben', i.name, r.ok); await loadTweaks(); runHealth();
});
let logFilter = 'all', logData = { lines: [] };
const isErr = x => /error|fail|exception|crash/i.test(x);
async function loadLogs() {
  try { logData = await api('logs'); } catch { }
  renderLogs();
  $('#crashList').innerHTML = (logData.crashes || []).length
    ? logData.crashes.slice(0, 5).map(c => `<div class="svc"><span class="nm" style="font-size:12px">${esc(c.name)}</span><span class="chip">${esc(c.date)}</span><b class="muted" style="font-family:var(--disp)">${bytes(c.size)}</b></div>`).join('')
    : '<div class="sub" style="padding:6px 0">Keine Crashes gefunden</div>';
}
function renderLogs() {
  const l = (logData.lines || []).filter(x => logFilter === 'all' || isErr(x));
  $('#logView').innerHTML = l.length ? l.map(x => `<div class="${isErr(x) ? 'e' : /warn/i.test(x) ? 'w' : ''}">${esc(x)}</div>`).join('') : '<span class="muted">Keine Einträge</span>';
  $('#logView').scrollTop = 1e9;
}
$('#logSeg').onclick = e => { const b = e.target.closest('button'); if (!b) return; logFilter = b.dataset.f; $$('#logSeg button').forEach(x => x.classList.toggle('on', x === b)); renderLogs(); };

/* ======================= UPDATES ======================= */
async function checkUpdate(manual) {
  $('#updCheck').disabled = true;
  try {
    const u = await api('update.check');
    $('#verCur').textContent = 'v' + u.current;
    if (!u.configured) $('#updTxt').textContent = 'Update-Server nicht eingerichtet';
    else if (u.error) $('#updTxt').textContent = 'Prüfung fehlgeschlagen: ' + u.error;
    else if (u.noRelease) { $('#updTxt').textContent = 'Auf GitHub ist noch keine Version veröffentlicht'; if (manual) toast('Kein Release gefunden', 'Noch keine Version auf GitHub veröffentlicht', false); }
    else if (!u.available) { $('#updTxt').textContent = 'Du hast die neueste Version'; if (manual) toast('Alles aktuell', 'Version ' + u.current); }
    else {
      $('#updTxt').textContent = 'Neue Version verfügbar';
      $('#updVer').textContent = u.latest; $('#updDate').textContent = u.date || '';
      $('#updNotes').innerHTML = (u.notes || []).map(n => `<li>${esc(n)}</li>`).join('');
      $('#updBox').classList.remove('hidden');
      $('#updPill').classList.add('show'); $('#updPillTxt').textContent = 'UPDATE ' + u.latest;
      $('#navUpd').classList.remove('hidden');
      if (!manual && S.prompted !== u.latest) {
        S.prompted = u.latest;
        if (S.settings.autoInstall) { toast('Update wird installiert', 'Version ' + u.latest); go('settings'); doUpdate(); }
        else updatePrompt(u);
      }
    }
  } catch { $('#updTxt').textContent = 'Prüfung fehlgeschlagen'; }
  $('#updCheck').disabled = false;
}
function updatePrompt(u) {
  modal(`<div class="kicker" style="justify-content:center">UPDATE VERFÜGBAR</div><h1 style="margin-top:10px">Version <span class="grad">${esc(u.latest)}</span> ist da</h1>
    <p>Du nutzt Version ${esc(u.current)}. Soll OrbitTweak jetzt aktualisieren? Die App startet danach automatisch neu.</p>
    ${(u.notes || []).length ? `<ul class="changes" style="text-align:left;max-width:400px;margin:14px auto 0">${u.notes.map(n => `<li>${esc(n)}</li>`).join('')}</ul>` : ''}
    <div class="bar hidden" id="mUpdMeter" style="margin-top:18px"><i id="mUpdBar" style="width:0"></i></div><div class="sub" id="mUpdProg" style="font-size:11px;margin-top:6px"></div>
    <div class="act" id="mUpdAct"><button class="btn" onclick="closeModal()">Später</button><button class="btn p lg" onclick="promptInstall()">${ico('download')}Jetzt aktualisieren</button></div>`);
}
function promptInstall() { $('#mUpdAct').classList.add('hidden'); $('#mUpdMeter').classList.remove('hidden'); $('#mUpdProg').textContent = 'Lade herunter…'; doUpdate(); }
function updProgress(d) {
  const mb = $('#mUpdBar'); if (mb) { mb.style.width = d.pct + '%'; $('#mUpdProg').textContent = d.total ? `${bytes(d.done)} / ${bytes(d.total)}` : ''; }
  $('#updMeter').classList.remove('hidden'); $('#updBar').style.width = d.pct + '%';
  $('#updProg').textContent = d.total ? `${bytes(d.done)} / ${bytes(d.total)}` : '';
}
async function doUpdate() {
  const b = $('#updBtn'); b.disabled = true; b.querySelector('span').textContent = 'Lädt…';
  try {
    const r = await api('update.download');
    if (r.error) throw new Error(r.error);
    $('#updProg').textContent = 'Installiere, OrbitTweak startet gleich neu…';
    if ($('#mUpdProg')) $('#mUpdProg').textContent = 'Installiere, OrbitTweak startet gleich neu…';
    await sleep(400); await api('update.install');
  } catch (e) {
    toast('Update fehlgeschlagen', e.message, false); b.disabled = false; b.querySelector('span').textContent = 'Erneut versuchen';
    if ($('#mUpdAct')) { $('#mUpdAct').classList.remove('hidden'); $('#mUpdProg').textContent = e.message; }
  }
}

/* ======================= SETTINGS ======================= */
const SETS = [
  ['autoUpdate', 'Automatisch nach Updates suchen', 'Beim Start und danach alle 2 Stunden'],
  ['autoInstall', 'Updates ohne Nachfrage installieren', 'Aus: OrbitTweak fragt vorher, ob es aktualisieren darf'],
  ['restoreBeforeBoost', 'Wiederherstellungspunkt vor dem Boost', 'Windows-Snapshot vor jeder Optimierung'],
  ['purgeStandby', 'Standby-Liste beim RAM-Clean leeren', 'Gibt gecachten Speicher frei (empfohlen bei 8–16 GB)'],
  ['autoRamClean', 'Auto-RAM-Clean im Spiel', 'Alle 5 Minuten, solange FiveM läuft'],
  ['autoPriority', 'Auto-Priorität „Hoch“', 'GTA-Prozess bekommt automatisch mehr CPU-Zeit'],
];
function renderSettings() {
  const cur = THEMES[S.settings.accent] ? S.settings.accent : 'inferno';
  $('#themes').innerHTML = Object.entries(THEMES).map(([k, [n, a, b]]) => `<div class="sw ${k === cur ? 'on' : ''}" data-th="${k}"><i style="background:linear-gradient(135deg,${a},${b})"></i>${n}</div>`).join('');
  $('#setList').innerHTML = SETS.map(([k, n, d]) => `<div class="srow"><div><b>${n}</b><p>${d}</p></div><div class="tg ${S.settings[k] ? 'on' : ''}" data-set="${k}"></div></div>`).join('');
}
document.addEventListener('click', e => {
  const th = e.target.closest('[data-th]');
  if (th) { S.settings.accent = th.dataset.th; applyTheme(th.dataset.th); renderSettings(); api('settings.set', { key: 'accent', value: th.dataset.th }); return; }
  const st = e.target.closest('[data-set]');
  if (st) { const k = st.dataset.set; S.settings[k] = !S.settings[k]; st.classList.toggle('on'); api('settings.set', { key: k, value: S.settings[k] }); if (!k.startsWith('auto') || k === 'autoRamClean' || k === 'autoPriority') loadTweaks(); }
});
async function restorePoint() { toast('Wird erstellt…', 'Kann bis zu einer Minute dauern'); const r = await api('restore'); toast(r.ok ? 'Wiederherstellungspunkt erstellt' : 'Fehlgeschlagen', r.ok ? 'Du kannst jederzeit zurück' : 'Ist der Computerschutz aktiviert?', r.ok); }

/* ======================= MODAL / WIZARD ======================= */
function modal(html) { $('#mbox').innerHTML = html; $('#modal').classList.add('show'); }
function closeModal() { $('#modal').classList.remove('show'); }
function confirmBox(title, text) {
  return new Promise(res => {
    modal(`<h1>${esc(title)}</h1><p>${esc(text)}</p><div class="act"><button class="btn" id="mNo">Abbrechen</button><button class="btn p" id="mYes">Bestätigen</button></div>`);
    $('#mNo').onclick = () => { closeModal(); res(false); }; $('#mYes').onclick = () => { closeModal(); res(true); };
  });
}
const steps = n => `<div class="steps">${[0, 1, 2, 3].map(i => `<i class="${i <= n ? 'on' : ''}"></i>`).join('')}</div>`;
async function wizard(step) {
  if (step === 0) modal(`${steps(0)}<div class="kicker" style="justify-content:center">SETUP-ASSISTENT</div><h1 style="margin-top:10px">Willkommen bei <span class="grad">OrbitTweak</span></h1><p>In einer Minute ist dein PC bereit für FiveM: Wir prüfen dein System, zeigen dir die Optimierungen und wenden sie mit Wiederherstellungspunkt an. Alles bleibt umkehrbar.</p><div class="act"><button class="btn" onclick="finishWizard()">Überspringen</button><button class="btn p lg" onclick="wizard(1)">${ico('zap')}Los geht's</button></div>`);
  if (step === 1) {
    modal(`${steps(1)}<h1>System-<span class="grad">Scan</span></h1><p>Analysiere Hardware, Windows und FiveM…</p><div class="bar mt" style="height:8px"><i style="width:30%" id="scanBar"></i></div>`);
    const [h] = await Promise.all([runHealth(), sleep(1000)]);
    $('#scanBar').style.width = '100%'; await sleep(300);
    const todo = S.tweaks.filter(t => t.rec && !t.applied);
    modal(`${steps(2)}<h1>Health: <span class="grad">${h.score}%</span></h1><p>${todo.length} empfohlene Optimierungen gefunden.</p><div class="wl">${Object.entries(CATS).map(([k, [n, i]]) => { const c = todo.filter(t => t.cat === k); return c.length ? `<div class="hc"><div class="st" style="background:rgba(var(--a-rgb),.12);color:var(--a)">${ico(i)}</div><div class="tx"><b>${n}</b><p>${c.map(t => esc(t.name)).join(' · ')}</p></div><span class="chip a">${c.length}</span></div>` : ''; }).join('')}</div><div class="act"><button class="btn" onclick="finishWizard()">Später</button><button class="btn p lg" onclick="wizard(3)">${ico('zap')}Optimieren</button></div>`);
  }
  if (step === 3) {
    modal(`${steps(3)}<h1>Boost <span class="grad">läuft</span></h1><p id="wizTxt">Starte…</p><div class="bar mt" style="height:8px"><i id="wizProg" style="width:0"></i></div>`);
    const r = await boost(true);
    modal(`${steps(3)}<h1>Dein PC ist <span class="grad">FiveM-ready</span></h1><p>${r ? `${r.applied} Optimierungen angewendet, ${bytes(r.freed)} freigegeben.${r.restart ? ' Starte deinen PC neu, damit alle Tweaks greifen.' : ''}` : ''}</p><div class="act"><button class="btn p lg" onclick="finishWizard()">Fertig</button></div>`);
  }
}
function finishWizard() { closeModal(); api('settings.set', { key: 'firstRun', value: false }); S.settings.firstRun = false; }

/* ======================= BOOT ======================= */
async function boot() {
  $('#ticks').innerHTML = Array.from({ length: 72 }, (_, i) => { const a = i * 5 * Math.PI / 180, m = i % 6 === 0, r1 = 146, r2 = m ? 136 : 140; return `<line class="${m ? 'm' : ''}" x1="${150 + r1 * Math.sin(a)}" y1="${150 - r1 * Math.cos(a)}" x2="${150 + r2 * Math.sin(a)}" y2="${150 - r2 * Math.cos(a)}"/>`; }).join('');
  $('#side').onclick = e => { const n = e.target.closest('.nav'); if (n) go(n.dataset.p); };
  document.addEventListener('mousedown', e => {
    if (e.button !== 0 || !e.target.closest('[data-drag]') || e.target.closest('button,.pill')) return;
    api(e.detail === 2 ? 'win.max' : 'win.drag');
  });
  document.addEventListener('contextmenu', e => { if (!e.target.closest('input,.cmd,.logv')) e.preventDefault(); });
  initLab();

  const d = await api('init');
  S.settings = d.settings; S.fivem = d.fivem;
  applyTheme(S.settings.accent);
  $('#uname').textContent = d.user;
  $('#verTxt').textContent = 'v' + d.version; $('#verCur').textContent = 'v' + d.version;
  $('#adminTxt').textContent = d.admin ? 'ADMIN ✓' : 'KEIN ADMIN';
  $('#mCpuName').textContent = d.sys.cpu;
  $('#mGpu').textContent = d.sys.gpu.replace(/^NVIDIA (GeForce )?/i, '').replace(/^AMD /i, '').replace(/^Intel\(R\) /i, '');
  $('#mGpu').title = d.sys.gpu;
  $('#mOs').textContent = `${d.sys.ramGb} GB RAM`;
  if (d.settings.lastBoost) $('#lastBoost').textContent = 'Letzter Boost: ' + new Date(d.settings.lastBoost).toLocaleDateString('de-DE');
  $('#launchSub').textContent = d.fivem.found ? 'mit Pre-Launch-Boost' : 'FiveM nicht gefunden';
  feed(d.fivem.found ? 'FiveM erkannt: ' + d.fivem.path : 'FiveM nicht gefunden: Pfad im Launcher wählen');
  renderSettings(); renderLauncher();
  await loadTweaks();
  refreshCache(); ping(); setInterval(ping, 8000);
  api('health').then(h => { S.health = h; $('#hsHealth').textContent = h.score + '%'; }).catch(() => { });
  if (S.settings.autoUpdate !== false) setTimeout(() => checkUpdate(false), 1500);
  setInterval(() => { if (S.settings.autoUpdate !== false) checkUpdate(false); }, 2 * 3600 * 1000);
  if (S.settings.firstRun) setTimeout(() => wizard(0), 700);
}

/* ======================= MOCK BACKEND (browser preview) ======================= */
const Mock = {
  settings: { accent: 'inferno', autoInstall: false, restoreBeforeBoost: true, purgeStandby: true, autoPriority: true, autoRamClean: false, autoUpdate: true, firstRun: false, lastBoost: null },
  tweaks: [
    ['gpu', 'fivem', 'Hochleistungs-GPU erzwingen', 'FiveM läuft immer auf der dedizierten Grafikkarte statt der iGPU.', 'monitor', null, 1],
    ['fso', 'fivem', 'Vollbild-Optimierung für FiveM aus', 'Echter Exclusive-Fullscreen, weniger Input-Lag.', 'max', null, 1],
    ['prio', 'fivem', 'Auto-Priorität „Hoch“', 'GTA-Prozess bekommt automatisch hohe CPU-Priorität.', 'gauge', 'LIVE', 1],
    ['autoram', 'fivem', 'Auto-RAM-Clean im Spiel', 'Leert alle 5 Minuten Standby-Liste & Working-Sets.', 'ram', 'LIVE', 0],
    ['timer', 'fivem', 'Timer-Auflösung 0.5 ms', 'Stabilere Frametimes, solange OrbitTweak läuft.', 'timer', 'LIVE', 1],
    ['ult', 'system', 'Ultimate Performance Plan', 'Kein CPU-Parking, kein Heruntertakten.', 'zap', null, 1],
    ['gm', 'system', 'Spielmodus', 'Windows priorisiert das Spiel.', 'gamepad', null, 1],
    ['dvr', 'system', 'Xbox Game DVR aus', 'Keine Hintergrundaufnahme.', 'videooff', null, 1],
    ['hags', 'system', 'Hardware-GPU-Planung', 'Weniger Latenz, stabilere Frametimes.', 'monitor', 'NEUSTART', 1],
    ['mmcss', 'system', 'Spiele-Priorität (MMCSS)', 'Mehr CPU- und GPU-Zeit für Spiele.', 'cpu', null, 1],
    ['nagle', 'network', 'Nagle-Algorithmus aus', 'Pakete werden sofort gesendet.', 'wifi', null, 1],
    ['thr', 'network', 'Netzwerk-Drosselung aus', 'Keine Multimedia-Paketdrosselung.', 'activity', null, 1],
    ['kbd', 'input', 'Tastatur-Verzögerung 0', 'Minimale Wiederholverzögerung, maximale Repeat-Rate.', 'keyboard', null, 1],
    ['queue', 'input', 'Input-Puffer optimieren', 'Kleinere Maus- und Tastatur-Warteschlange.', 'mouse', 'NEUSTART', 1],
    ['accel', 'input', 'Mausbeschleunigung aus', '1:1-Gefühl beim Zielen.', 'target', null, 1],
    ['usb', 'input', 'USB-Energiesparen aus', 'Keine Input-Aussetzer durch Stromsparen.', 'usb', null, 1],
  ].map(([id, cat, name, desc, icon, badge, rec], i) => ({ id, cat, name, desc, icon, badge, rec: !!rec, applied: i % 3 === 0 })),
  async call(cmd, a) {
    await sleep(100); const M = this;
    switch (cmd) {
      case 'init': setInterval(() => onEvent('stats', { cpu: Math.round(18 + Math.random() * 30), ram: 54, ramUsed: 17.3, ramTotal: 32, fm: { running: true, game: true, build: 'b3095', prio: 'Hoch', gameRam: 5.6e9 } }), 1000);
        return { user: 'Max', admin: true, version: '2.2.0', sys: { cpu: 'AMD Ryzen 7 7800X3D 8-Core Processor', gpu: 'NVIDIA GeForce RTX 4070 SUPER', os: 'Windows 11 Pro', ramGb: 32 }, fivem: { found: true, path: 'C:\\Users\\Max\\AppData\\Local\\FiveM' }, settings: M.settings };
      case 'tweaks.list': return M.tweaks;
      case 'tweak.set': { const t = M.tweaks.find(x => x.id === a.id); t.applied = a.on; return { applied: a.on }; }
      case 'boost': for (let i = 0; i <= 10; i++) { onEvent('boost', { pct: i * 10, text: 'Schritt ' + i }); await sleep(220); } M.tweaks.forEach(t => t.rec && (t.applied = true)); return { freed: 1.4e9, restart: true, applied: 9 };
      case 'ping': return { ms: 18 + Math.round(Math.random() * 6), jitter: 2 };
      case 'fivem.cacheSize': return { found: true, size: 3.2e9 };
      case 'servers.list': return [
        { code: 'k4', name: '^8Nova^7City ^1| ^7Roleplay ^3[GER]', desc: 'Die lebendigste Stadt Deutschlands', players: 187, max: 256, online: true, ping: 22, locale: 'DE', tags: ['roleplay', 'economy'] },
        { code: 'r2', name: '^4Blue^7Line ^5PvP ^7Arena', desc: 'Gangwar · FFA · Ranked', players: 64, max: 128, online: true, ping: 31, locale: 'DE', tags: ['pvp'] },
        { code: 'aa', name: '^2Green^7Valley RP', desc: 'Whitelist-Server', players: 0, max: 64, online: false, ping: null, locale: 'EN', tags: ['whitelist'] }];
      case 'cfx.status': return [{ name: 'Server-Liste', ok: true, ms: 41 }, { name: 'Cfx.re Web', ok: true, ms: 57 }, { name: 'Content-CDN', ok: true, ms: 23 }, { name: 'Cfx.re Forum', ok: true, ms: 64 }];
      case 'gfx.get': return { found: true, path: 'C:\\Users\\Max\\Documents\\Rockstar Games\\GTA V\\settings.xml', values: { ...PRESETS.bal.v } };
      case 'ini.get': return { channel: 'production' };
      case 'clean.scan': return [['fm', 'FiveM Cache', 'cache, server-cache, server-cache-priv', 'gamepad', 3.2e9], ['logs', 'FiveM Logs & Crashes', 'Alte Logs und Crashdumps', 'file', 2.1e8], ['temp', 'Temporäre Dateien', 'Benutzer- und System-Temp', 'folder', 1.6e9], ['shader', 'Shader-Cache', 'DirectX / NVIDIA / AMD', 'monitor', 7.4e8], ['wu', 'Windows-Update Reste', 'Bereits installierte Pakete', 'download', 9.1e8], ['bin', 'Papierkorb', 'Endgültig leeren', 'trash', 2.4e8]].map(([id, name, desc, icon, size], i) => ({ id, name, desc, icon, size, sel: i < 5 }));
      case 'health': return { score: 74, items: [{ name: 'FiveM installiert', value: 'C:\\Users\\Max\\AppData\\Local\\FiveM', st: 'ok' }, { name: 'Arbeitsspeicher', value: '32 GB, perfekt für FiveM', st: 'ok' }, { name: 'Energieplan', value: 'Ausbalanciert, CPU taktet herunter', st: 'warn', fix: 'tweak:ult' }, { name: 'Xbox Game DVR', value: 'Aktiv, kostet FPS', st: 'bad', fix: 'tweak:dvr' }, { name: 'GPU-Treiber', value: 'NVIDIA 566.36 · 4 Monate alt', st: 'ok' }, { name: 'FiveM-Cache', value: '3.2 GB, leeren empfohlen', st: 'warn', fix: 'cache' }] };
      case 'logs': return { lines: ['[ 1203] Loading game build 3095', '[ 2210] Warning: texture budget exceeded', '[ 4501] Connecting to 127.0.0.1:30120', '[ 9012] SCRIPT ERROR: @core/server.lua:12 attempt to index nil', '[12001] Session started'], crashes: [] };
      case 'update.check': return { configured: true, current: '2.2.0', latest: '2.3.0', available: new URLSearchParams(location.search).has('upd'), date: '12.11.2026', notes: ['Neuer Look mit weniger Effekten', 'Server-Favoriten laden schneller', 'Fix: Launcher-Pfad wird gespeichert'] };
      default: return { ok: true, freed: 3.4e8, count: 0, applied: true };
    }
  }
};

boot();
