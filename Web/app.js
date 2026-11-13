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
  ember: ['Ember', '#FF7A3D', '#FFB067'],
  rose: ['Rosé', '#FF5C8A', '#FFA3BD'],
  violet: ['Violet', '#8B7BFF', '#C9B8FF'],
  ocean: ['Ocean', '#3D9BFF', '#7FD4FF'],
  mint: ['Mint', '#2DD4A0', '#9AF2D4'],
  gold: ['Gold', '#F5B53D', '#FFE08A'],
  mono: ['Mono', '#E8E8EC', '#A8A8B3'],
  crimson: ['Crimson', '#FF3B4E', '#FF8A7A'],
  sunset: ['Sunset', '#FF6B3D', '#FFC93D'],
  magenta: ['Magenta', '#E040FB', '#F5A3FF'],
  sky: ['Sky', '#38BDF8', '#A5F3FC'],
  teal: ['Teal', '#14B8A6', '#5EEAD4'],
  lime: ['Lime', '#84CC16', '#D9F99D'],
  copper: ['Copper', '#D97745', '#F2B07E'],
  ice: ['Ice', '#93C5FD', '#E0F2FE'],
};
const THEME_ALIAS = { inferno: 'ember', blood: 'rose', toxic: 'mint', nebula: 'violet' };
const themeKey = k => THEMES[k] ? k : THEMES[THEME_ALIAS[k]] ? THEME_ALIAS[k] : 'ember';
const hexRgb = h => [1, 3, 5].map(i => parseInt(h.substr(i, 2), 16)).join(',');
function applyTheme(k) {
  const t = THEMES[themeKey(k)], r = document.documentElement.style;
  r.setProperty('--a', t[1]); r.setProperty('--b', t[2]);
  r.setProperty('--a-rgb', hexRgb(t[1])); r.setProperty('--b-rgb', hexRgb(t[2]));
}

/* ======================= MOTION ======================= */
function countTo(el, to, suffix = '', dur = 700) {
  if (!el) return;
  const from = el._v ?? 0; el._v = to;
  cancelAnimationFrame(el._raf);
  if (from === to) { el.innerHTML = Math.round(to) + suffix; return; }
  const t0 = performance.now();
  const step = now => {
    const k = Math.min(1, (now - t0) / dur), e = 1 - Math.pow(1 - k, 3);
    el.innerHTML = Math.round(from + (to - from) * e) + suffix;
    if (k < 1) el._raf = requestAnimationFrame(step);
  };
  el._raf = requestAnimationFrame(step);
}
function moveInd() {
  const a = $('.nav.active'), ind = $('#navInd'); if (!a || !ind) return;
  ind.style.transform = `translateY(${a.offsetTop}px)`; ind.style.opacity = 1;
}
function enterPage(el) {
  el.classList.remove('enter'); void el.offsetWidth;
  el.querySelectorAll('.card,.tabs,.addbar,.savebar,.empty').forEach((c, i) => c.style.setProperty('--i', Math.min(i, 12)));
  el.classList.add('enter');
  clearTimeout(el._et); el._et = setTimeout(() => el.classList.remove('enter'), 1400);
}
let spotRaf = 0;
document.addEventListener('pointermove', e => {
  if (spotRaf) return;
  spotRaf = requestAnimationFrame(() => {
    spotRaf = 0;
    const c = e.target.closest && e.target.closest('.card'); if (!c) return;
    const r = c.getBoundingClientRect();
    c.style.setProperty('--mx', (e.clientX - r.left) + 'px'); c.style.setProperty('--my', (e.clientY - r.top) + 'px');
  });
});
function smoothPath(pts) {
  let d = `M${pts[0][0].toFixed(1)},${pts[0][1].toFixed(1)}`;
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[i - 1] || pts[i], p1 = pts[i], p2 = pts[i + 1], p3 = pts[i + 2] || p2;
    const c1 = [p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6], c2 = [p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6];
    d += ` C${c1.map(v => v.toFixed(1))} ${c2.map(v => v.toFixed(1))} ${p2.map(v => v.toFixed(1))}`;
  }
  return d;
}

/* ======================= NAV ======================= */
let curPage = 'home';
function go(p) {
  if ((p === 'display') !== (curPage === 'display')) api('display.preview', { on: p === 'display' }).catch(() => { });
  if ((p === 'pad') !== (curPage === 'pad')) api('pad.watch', { on: p === 'pad' }).then(r => r && padState(r.state)).catch(() => { });
  curPage = p;
  $$('.nav').forEach(n => n.classList.toggle('active', n.dataset.p === p));
  $$('.page').forEach(s => s.classList.toggle('show', s.id === 'p-' + p));
  moveInd(); enterPage($('#p-' + p));
  ({ servers: () => S.servers.length || loadServers(), gfx: () => { S.gfx || loadGfx(); gwRefresh(); }, f8: () => loadF8().then(renderF8), minimap: modsRefresh, clip: clipList, mods: modsRefresh, settings: () => setTab($('#setTabs .tab.on')?.dataset.st || 'design'), xhair: () => setTimeout(xhPlace, 30), cleaner: () => S.clean.length || scan(),
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
  else if (ev === 'xhair') { XH.on = d.on; xhState(); toast(d.on ? 'Fadenkreuz an' : 'Fadenkreuz aus', d.on ? 'per Taste · Server-Regeln beachten' : 'per Taste'); }
  else if (ev === 'display') { DP.cfg.on = d.on; dpState(); }
  else if (ev === 'pad') padState(d);
  else if (ev === 'padon') { PAD.cfg.on = d.on; padUI(); toast(d.on ? 'Controller-Overlay an' : 'Controller-Overlay aus', 'per Taste'); }
  else if (ev === 'modInstalled') { passed('Mod installiert', `${MOD_CATS[d.cat]?.[0] || 'Mod'} · ${d.name}`); modList(); }
  else if (ev === 'reshade') { const b = $('#rsBar i'); if (b) b.style.width = d.pct + '%'; $('#rsProg').textContent = d.text; }
  else if (ev === 'clip') { toast('Clip gespeichert', 'per Taste · ' + (CL_RECS[d.recorder]?.[1] || '')); if (curPage === 'clip') setTimeout(clipList, 4000); }
}
function stats(d) {
  $('#tbCpu').textContent = d.cpu + '%'; $('#tbRam').textContent = d.ram + '%';
  countTo($('#mCpu'), d.cpu, '<small>%</small>');
  countTo($('#mRam'), d.ram, '<small>%</small>');
  $('#mRamTxt').textContent = `${d.ramUsed.toFixed(1)} / ${d.ramTotal.toFixed(0)} GB`;
  $('#mRamBar').style.width = d.ram + '%';
  countTo($('#cRam'), d.ram, '<small>%</small>'); $('#cRamBar').style.width = d.ram + '%';
  $('#cRamTxt').textContent = `${d.ramUsed.toFixed(1)} / ${d.ramTotal.toFixed(0)} GB`;
  S.cpuHist.push(d.cpu); if (S.cpuHist.length > 40) S.cpuHist.shift();
  const n = S.cpuHist.length, pts = S.cpuHist.map((v, i) => [(i + 40 - n) * 200 / 39, 58 - v / 100 * 52]);
  const line = pts.length > 1 ? smoothPath(pts) : `M0,${pts[0][1]} L200,${pts[0][1]}`;
  $('#sparkL').setAttribute('d', line);
  $('#sparkF').setAttribute('d', line + ` L200,60 L${pts.length > 1 ? pts[0][0].toFixed(1) : 0},60 Z`);
  fmState(d.fm);
}
function fmState(f) {
  const on = f.running;
  $('#tbDot').classList.toggle('on', on);
  $('#tbFm').textContent = f.game ? 'FiveM · im Spiel' : on ? 'FiveM · Launcher' : 'FiveM offline';
  $('#fmIco').classList.toggle('off', !on);
  $('#fmState').textContent = f.game ? 'Im Spiel' : on ? 'Launcher offen' : 'FiveM offline';
  const chip = $('#fmChip');
  chip.className = 'chip ' + (f.game ? 'g' : on ? 'w' : '');
  chip.textContent = f.game ? 'Live' : on ? 'Lädt' : 'Standby';
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
  if (!S.boosting) { setRing(pct); $('#scoreChip').textContent = pct >= 90 ? 'Optimal' : 'Bereit'; $('#scoreChip').className = 'chip ' + (pct >= 90 ? 'g' : ''); }
}
function setRing(p) {
  p = Math.max(0, Math.min(p, 100));
  $('#scoreRing').style.strokeDashoffset = 678.6 * (1 - p / 100);
  countTo($('#ringNum'), p, '<small>%</small>', 1200);
}
function addFreed(b) { S.freed += b || 0; $('#hsFreed').textContent = bytes(S.freed); }
function boostProgress(pct, text) {
  setRing(pct); $('#heroText').textContent = text;
  const w = $('#wizProg'); if (w) { w.style.width = pct + '%'; $('#wizTxt').textContent = text; }
}
async function boost(fromWizard) {
  if (S.boosting) return;
  S.boosting = true;
  const btn = $('#boostBtn'), g = $('#gauge');
  btn.classList.remove('done'); btn.classList.add('run'); g.classList.add('run');
  $('#scoreChip').textContent = 'Läuft'; $('#scoreChip').className = 'chip a';
  $('#heroTitle').textContent = 'Boost läuft…'; $('#boostSub').textContent = 'wird optimiert';
  $('#boostTxt').textContent = 'Optimiere…';
  $('#boostIco').innerHTML = '<use href="#refresh"/>'; $('#boostIco').classList.add('spin');
  setRing(0);
  feed('Boost gestartet');
  try {
    const r = await api('boost');
    addFreed(r.freed); await loadTweaks(); setRing(100);
    g.classList.remove('pop'); void g.offsetWidth; g.classList.add('pop');
    btn.classList.add('done'); $('#boostTxt').textContent = 'Fertig'; $('#boostSub').textContent = 'optimiert';
    $('#boostIco').classList.remove('spin'); $('#boostIco').innerHTML = '<use href="#check"/>';
    $('#scoreChip').textContent = 'Fertig'; $('#scoreChip').className = 'chip g';
    $('#heroTitle').textContent = 'Boost abgeschlossen';
    $('#heroText').textContent = r.restart ? 'Fertig. Starte den PC neu, damit alle Tweaks greifen.' : 'Fertig. Dein System ist bereit für FiveM.';
    $('#lastBoost').textContent = 'Letzter Boost: gerade eben';
    if (!fromWizard) passed('Boost abgeschlossen', `${r.applied} Tweaks · ${bytes(r.freed)} frei${r.restart ? ' · Neustart empfohlen' : ''}`);
    if (!fromWizard) toast('Boost abgeschlossen', `${r.applied} Tweaks · ${bytes(r.freed)} freigegeben${r.restart ? ' · Neustart empfohlen' : ''}`);
    feed(`Boost fertig: ${r.applied} Tweaks, ${bytes(r.freed)} frei`);
    return r;
  } catch (e) { toast('Boost fehlgeschlagen', e.message, false); }
  finally {
    btn.classList.remove('run'); g.classList.remove('run');
    $('#boostIco').classList.remove('spin');
    S.boosting = false;
    setTimeout(() => {
      if (S.boosting) return;
      btn.classList.remove('done'); $('#boostTxt').textContent = 'Boost starten'; $('#boostIco').innerHTML = '<use href="#zap"/>';
      $('#heroTitle').textContent = 'FiveM Full Boost'; $('#boostSub').textContent = 'optimiert'; score();
    }, 2800);
  }
}

/* ======================= TWEAKS ======================= */
const CATS = { fivem: ['FiveM', 'gamepad'], system: ['System', 'cpu'], network: ['Netzwerk & Hitreg', 'wifi'], input: ['Input', 'keyboard'] };
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
      <div class="tags">${s.online ? '<span class="chip g">Online</span>' : '<span class="chip r">Offline</span>'}${s.locale ? `<span class="chip">${esc(s.locale)}</span>` : ''}${(s.tags || []).slice(0, 3).map(t => `<span class="chip">${esc(t)}</span>`).join('')}</div>
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
  { k: 'Windowed', n: 'Anzeigemodus', d: 'Randlos für Fadenkreuz-Overlay', o: [['Vollbild', 0], ['Fenster', 1], ['Randlos', 2]] },
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
  comp: { n: 'Competitive', d: 'Maximale FPS und klare Sicht. Für PvP, Gangwar und schwächere PCs.', fps: 5, q: 1,
    v: { TextureQuality: 1, ShadowQuality: 1, ReflectionQuality: 0, GrassQuality: 0, WaterQuality: 0, ParticleQuality: 0, ShaderQuality: 0, PostFX: 0, SSAO: 0, Tessellation: 0, MSAA: 0, FXAA_Enabled: 'true', AnisotropicFiltering: 4, Shadow_SoftShadows: 0, LodScale: 0.6, CityDensity: 0.3, MotionBlurStrength: 0 } },
  bal: { n: 'Balanced', d: 'Gute Optik bei stabilen FPS. Für Roleplay im Alltag.', fps: 3, q: 3,
    v: { TextureQuality: 2, ShadowQuality: 2, ReflectionQuality: 1, GrassQuality: 1, WaterQuality: 1, ParticleQuality: 1, ShaderQuality: 1, PostFX: 1, SSAO: 1, Tessellation: 1, MSAA: 0, FXAA_Enabled: 'true', AnisotropicFiltering: 16, Shadow_SoftShadows: 1, LodScale: 0.8, CityDensity: 0.6, MotionBlurStrength: 0 } },
  cine: { n: 'Cinematic', d: 'Maximale Qualität für Screenshots, Clips und High-End-PCs.', fps: 1, q: 5,
    v: { TextureQuality: 2, ShadowQuality: 3, ReflectionQuality: 3, GrassQuality: 2, WaterQuality: 2, ParticleQuality: 3, ShaderQuality: 2, PostFX: 3, SSAO: 2, Tessellation: 3, MSAA: 2, FXAA_Enabled: 'true', AnisotropicFiltering: 16, Shadow_SoftShadows: 3, LodScale: 1, CityDensity: 1, MotionBlurStrength: 0 } },
};
const pips = n => Array.from({ length: 5 }, (_, i) => `<i class="${i < n ? 'on' : ''}"></i>`).join('');
const num = v => v === 'true' ? 1 : v === 'false' ? 0 : parseFloat(v);
async function loadGfx() {
  try { S.gfx = await api('gfx.get'); } catch { S.gfx = { found: false, values: {} }; }
  S.gfxEdit = { ...S.gfx.values };
  $('#gfxState').className = 'chip ' + (S.gfx.found ? 'g' : 'r');
  $('#gfxState').textContent = S.gfx.found ? 'settings.xml gefunden' : 'settings.xml fehlt';
  $('#gfxPath').textContent = S.gfx.found ? S.gfx.path : 'GTA V settings.xml nicht gefunden. Starte GTA V oder FiveM einmal.';
  $('#presets').innerHTML = Object.entries(PRESETS).map(([k, p]) => `<div class="card hov preset" data-pr="${k}"><div class="ck chip a">Aktiv</div><div class="pt">${p.n}</div><p>${p.d}</p><div class="meter"><span>FPS</span><div class="pips">${pips(p.fps)}</div><span>Optik</span><div class="pips">${pips(p.q)}</div></div></div>`).join('');
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
  $('#labTabs').onclick = e => { const b = e.target.closest('button'); if (!b) return; $$('#labTabs button').forEach(x => x.classList.toggle('on', x === b)); pad.classList.toggle('hidden', b.dataset.t !== 'poll'); $('#reactPad').classList.toggle('hidden', b.dataset.t !== 'react'); $('#padPad').classList.toggle('hidden', b.dataset.t !== 'pad'); if (b.dataset.t === 'pad') gpStart(); };
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
      $('#updPill').classList.add('show'); $('#updPillTxt').textContent = 'Update ' + u.latest;
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
  modal(`<div class="kicker" style="justify-content:center">Update verfügbar</div><h1 style="margin-top:10px">Version <span class="grad">${esc(u.latest)}</span> ist da</h1>
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
  const cur = themeKey(S.settings.accent);
  $('#themes').innerHTML = Object.entries(THEMES).map(([k, [n, a, b]]) => `<div class="sw ${k === cur ? 'on' : ''}" data-th="${k}"><i style="background:linear-gradient(135deg,${a},${b})"></i>${n}</div>`).join('');
  const row = ([k, n, d]) => `<div class="srow"><div><b>${n}</b><p>${d}</p></div><div class="tg ${S.settings[k] ? 'on' : ''}" data-set="${k}"></div></div>`;
  $('#setList').innerHTML = SETS.filter(s => !['autoUpdate', 'autoInstall'].includes(s[0])).map(row).join('');
  $('#updSets').innerHTML = SETS.filter(s => ['autoUpdate', 'autoInstall'].includes(s[0])).map(row).join('');
}
document.addEventListener('click', e => {
  const th = e.target.closest('[data-th]');
  if (th) { S.settings.accent = th.dataset.th; applyTheme(th.dataset.th); renderSettings(); api('settings.set', { key: 'accent', value: th.dataset.th }); padDraw(); padPush(); return; }
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
  if (step === 0) modal(`${steps(0)}<div class="kicker" style="justify-content:center">Setup-Assistent</div><h1 style="margin-top:10px">Willkommen bei <span class="grad">OrbitTweak</span></h1><p>In einer Minute ist dein PC bereit für FiveM: Wir prüfen dein System, zeigen dir die Optimierungen und wenden sie mit Wiederherstellungspunkt an. Alles bleibt umkehrbar.</p><div class="act"><button class="btn" onclick="finishWizard()">Überspringen</button><button class="btn p lg" onclick="wizard(1)">${ico('zap')}Los geht's</button></div>`);
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

/* ======================= CROSSHAIR ======================= */
const KEYS = [['Aus', 0], ['F6', 0x75], ['F7', 0x76], ['F9', 0x78], ['F10', 0x79], ['F11', 0x7A], ['Einfg', 0x2D], ['Pos1', 0x24], ['Bild↑', 0x21]];
const XH_DEF = { style: 'cross', color: '#3DFF8B', size: 8, thick: 2, gap: 4, alpha: 100, outline: true, oThick: 1, dot: false, dotSize: 2 };
const XH_PRESETS = {
  classic: ['Classic', { style: 'cross', color: '#3DFF8B', size: 8, thick: 2, gap: 4, alpha: 100, outline: true, oThick: 1, dot: false, dotSize: 2 }],
  dot: ['Dot', { style: 'dot', color: '#FF3B5C', size: 8, thick: 2, gap: 4, alpha: 100, outline: true, oThick: 1, dot: true, dotSize: 4 }],
  circle: ['Kreis', { style: 'circle', color: '#3DE0FF', size: 10, thick: 2, gap: 4, alpha: 100, outline: true, oThick: 1, dot: true, dotSize: 2 }],
  sniper: ['Sniper', { style: 'cross', color: '#FFE03D', size: 16, thick: 1, gap: 7, alpha: 100, outline: true, oThick: 1, dot: true, dotSize: 2 }],
  tshape: ['T-Form', { style: 't', color: '#FFFFFF', size: 9, thick: 2, gap: 3, alpha: 100, outline: true, oThick: 1, dot: false, dotSize: 2 }],
};
const XH_STYLES = [
  ['cross', 'Kreuz', '<path d="M12 3v6M12 15v6M3 12h6M15 12h6"/>'],
  ['t', 'T-Form', '<path d="M12 15v6M3 12h6M15 12h6"/>'],
  ['dot', 'Punkt', '<circle cx="12" cy="12" r="3" fill="currentColor"/>'],
  ['circle', 'Kreis', '<circle cx="12" cy="12" r="7"/>'],
  ['plus', 'Plus', '<path d="M12 4v16M4 12h16"/>'],
];
const XH_COLORS = ['#3DFF8B', '#FFFFFF', '#3DE0FF', '#FFE03D', '#FF3B5C', '#FF4DF0', '#FF8A3D'];
const XH_SLIDERS = [['size', 'Länge', 1, 40], ['thick', 'Dicke', 1, 8], ['gap', 'Lücke', 0, 20], ['alpha', 'Deckkraft', 20, 100, '%'], ['oThick', 'Kontur', 0, 3], ['dotSize', 'Punkt', 1, 8], ['offsetX', 'Versatz X', -60, 60], ['offsetY', 'Versatz Y', -60, 60]];
const XH_RULES = 'Manche FiveM-Server verbieten Fadenkreuz-Overlays. Prüfe vorher die Regeln deines Servers, die Nutzung erfolgt auf eigene Verantwortung.';
const XH = { cfg: { ...XH_DEF }, on: false, onlyGame: false, key: 0x79, offsetX: 0, offsetY: 0 };

function xhDraw(c) {
  const o = c.outline ? c.oThick : 0, t = c.thick, g = c.style === 'plus' ? 0 : c.gap, L = c.size;
  const R = Math.ceil(Math.max(c.style === 'dot' ? 0 : L + g + t, c.dot || c.style === 'dot' ? c.dotSize : 0) + o + 3);
  const N = 2 * R + 1, cv = document.createElement('canvas'); cv.width = cv.height = N;
  const x = cv.getContext('2d'), m = R, t0 = m - Math.floor((t - 1) / 2);
  const shapes = [];
  if (c.style === 'cross' || c.style === 't' || c.style === 'plus') {
    shapes.push([m + 1 + g, t0, L, t], [m - g - L, t0, L, t]);
    shapes.push([t0, m + 1 + g, t, L]);
    if (c.style !== 't') shapes.push([t0, m - g - L, t, L]);
    if (g === 0) shapes.push([t0, t0, t, t]);
  }
  const dot = c.dot || c.style === 'dot', ds = c.style === 'dot' ? c.dotSize : c.dotSize;
  const paint = (col, e) => {
    x.fillStyle = col; x.strokeStyle = col;
    shapes.forEach(([a, b, w, h]) => x.fillRect(a - e, b - e, w + 2 * e, h + 2 * e));
    if (c.style === 'circle') { x.lineWidth = t + 2 * e; x.beginPath(); x.arc(m + .5, m + .5, L, 0, Math.PI * 2); x.stroke(); }
    if (dot) {
      if (ds <= 2) { const d0 = m - Math.floor((ds - 1) / 2); x.fillRect(d0 - e, d0 - e, ds + 2 * e, ds + 2 * e); }
      else { x.beginPath(); x.arc(m + .5, m + .5, ds / 2 + e, 0, Math.PI * 2); x.fill(); }
    }
  };
  if (o > 0) paint('#000', o);
  paint(c.color, 0);
  if (c.alpha >= 100) return cv;
  const out = document.createElement('canvas'); out.width = out.height = N;
  const ox = out.getContext('2d'); ox.globalAlpha = c.alpha / 100; ox.drawImage(cv, 0, 0); return out;
}
function xhRender(bump) {
  const src = xhDraw(XH.cfg), cv = $('#xhCanvas'), z = $('#xhZoom');
  cv.width = src.width; cv.height = src.height; cv.getContext('2d').drawImage(src, 0, 0);
  xhPlace();
  const zx = z.getContext('2d'); zx.clearRect(0, 0, 96, 96); zx.imageSmoothingEnabled = false;
  const s = Math.max(24, Math.min(src.width, 48)), sx = (src.width - s) / 2; zx.drawImage(src, sx, sx, s, s, 0, 0, 96, 96);
  if (bump) { const sc = $('#xhScene'); sc.classList.remove('bump'); void sc.offsetWidth; sc.classList.add('bump'); }
  return src;
}
// same math as the overlay window: middle pixel of the odd-sized image sits on the center pixel
function xhPlace() {
  const sc = $('#xhScene'), cv = $('#xhCanvas'); if (!sc || !cv.width) return;
  const R = (cv.width - 1) / 2;
  cv.style.left = (Math.floor(sc.clientWidth / 2) - R + XH.offsetX) + 'px';
  cv.style.top = (Math.floor(sc.clientHeight / 2) - R + XH.offsetY) + 'px';
}
addEventListener('resize', () => xhPlace());
async function copyCmd(text, btn) {
  let ok = false;
  try { const r = await api('clip', { text }); ok = r && r.ok; } catch { }
  if (!ok) try { await navigator.clipboard.writeText(text); ok = true; } catch { }
  if (btn) { btn.classList.add('ok'); const o = btn.innerHTML; btn.innerHTML = `${ico('check')}Kopiert`; setTimeout(() => { btn.classList.remove('ok'); btn.innerHTML = o; }, 1600); }
  toast(ok ? 'Befehl kopiert' : 'Kopieren fehlgeschlagen', ok ? `${text} · In FiveM: F8, Strg+V, Enter` : text, ok);
}
window.copyCmd = copyCmd;
let xhT = 0;
function xhPush(img = true) {
  clearTimeout(xhT);
  xhT = setTimeout(async () => {
    const src = img ? xhDraw(XH.cfg) : null;
    const r = await api('xhair.set', { png: src ? src.toDataURL('image/png').split(',')[1] : '', json: JSON.stringify(XH.cfg), on: XH.on, onlyGame: XH.onlyGame, key: XH.key, offsetX: XH.offsetX, offsetY: XH.offsetY }).catch(() => null);
    if (r && r.keyOk === false) toast('Taste belegt', 'Eine andere App nutzt diese Taste schon', false);
  }, 90);
}
function xhUI() {
  $('#xhPresets').innerHTML = Object.entries(XH_PRESETS).map(([k, [n, c]]) => { const on = JSON.stringify(c) === JSON.stringify(XH.cfg); return `<button class="xpre ${on ? 'on' : ''}" data-xp="${k}"><canvas data-pv="${k}"></canvas>${n}</button>`; }).join('');
  $$('[data-pv]').forEach(cv => { const s = xhDraw(XH_PRESETS[cv.dataset.pv][1]); cv.width = s.width; cv.height = s.height; cv.getContext('2d').drawImage(s, 0, 0); });
  $('#xhStyles').innerHTML = XH_STYLES.map(([k, n, svg]) => `<button class="xst ${XH.cfg.style === k ? 'on' : ''}" data-xs="${k}"><svg viewBox="0 0 24 24">${svg}</svg>${n}</button>`).join('');
  const custom = !XH_COLORS.includes(XH.cfg.color.toUpperCase());
  $('#xhColors').innerHTML = XH_COLORS.map(c => `<button class="xcol ${XH.cfg.color.toUpperCase() === c ? 'on' : ''}" data-xc="${c}" style="background:${c}"></button>`).join('') +
    `<label class="xcustom ${custom ? 'on' : ''}" title="Eigene Farbe"><input type="color" id="xhPick" value="${XH.cfg.color}"></label>`;
  const val = k => k === 'offsetX' || k === 'offsetY' ? XH[k] : XH.cfg[k];
  $('#xhSliders').innerHTML = `<div class="xs tgl"><span>Schwarze Kontur</span><div class="tg ${XH.cfg.outline ? 'on' : ''}" data-xb="outline"></div></div><div class="xs tgl"><span>Mittelpunkt</span><div class="tg ${XH.cfg.dot ? 'on' : ''}" data-xb="dot"></div></div>` +
    XH_SLIDERS.map(([k, n, mi, ma, u = '']) => { const v = val(k), p = (v - mi) / (ma - mi) * 100; return `<div class="xs"><span>${n}</span><input type="range" min="${mi}" max="${ma}" value="${v}" style="--p:${p}%" data-xr="${k}"><b>${v}${u}</b></div>`; }).join('');
  xhState();
  xhRender();
}
function xhState() {
  $('#xhShrink').classList.toggle('on', S.settings.xhairShrink !== false);
  $('#xhOn').classList.toggle('on', XH.on); $('#xhOnly').classList.toggle('on', XH.onlyGame);
  $('#xhMain').querySelector('span').textContent = XH.on ? 'Ausschalten' : 'Einschalten';
  $('#xhMain').classList.toggle('p', !XH.on);
  $('#navXh').classList.toggle('hidden', !XH.on);
  $('#xhKey').innerHTML = KEYS.map(([n, v]) => `<button class="${XH.key === v ? 'on' : ''}" data-xk="${v}">${n}</button>`).join('');
  const kn = KEYS.find(k => k[1] === XH.key)?.[0];
  $('#xhKeyTxt').textContent = XH.key ? `${kn} schaltet das Fadenkreuz im Spiel an und aus` : 'Keine Taste, nur hier umschaltbar';
}
function xhToggle(confirmed) {
  if (!XH.on && !S.settings.xhairAck && !confirmed) {
    return modal(`<div class="kicker">Hinweis</div><h1 style="margin-top:10px">Server-Regeln <span class="grad">beachten</span></h1>
      <p>${XH_RULES}</p><p>OrbitTweak zeigt das Fadenkreuz nur als Fenster über dem Spiel an und verändert nichts an FiveM.</p>
      <div class="act"><button class="btn" onclick="closeModal()">Abbrechen</button><button class="btn p lg" onclick="xhAck()">${ico('check')}Verstanden, einschalten</button></div>`);
  }
  XH.on = !XH.on; xhState(); xhPush(false);
  if (XH.on && S.settings.xhairShrink !== false) setTimeout(() => toast('GTA-Fadenkreuz wird minimiert', 'OrbitTweak sendet profile_reticuleSize 0 an FiveM'), 700);
  toast(XH.on ? 'Fadenkreuz an' : 'Fadenkreuz aus', XH.on ? 'Server-Regeln beachten · ' + (XH.onlyGame ? 'nur im Spiel sichtbar' : 'bleibt auch beim Raustabben') : '');
}
function xhAck() { closeModal(); S.settings.xhairAck = true; api('settings.set', { key: 'xhairAck', value: true }); xhToggle(true); }
async function setBorderless() {
  const r = await api('gfx.save', { values: { Windowed: 2 } });
  if (r.error) return toast('Nicht gespeichert', r.error, false);
  if (S.gfx) { S.gfx.values.Windowed = 2; S.gfxEdit.Windowed = 2; }
  toast('Randlos eingestellt', 'Gilt ab dem nächsten FiveM-Start');
}
document.addEventListener('click', e => {
  const p = e.target.closest('[data-xp]'); if (p) { XH.cfg = { ...XH_PRESETS[p.dataset.xp][1] }; xhUI(); xhRender(true); xhPush(); return; }
  const s = e.target.closest('[data-xs]'); if (s) { XH.cfg.style = s.dataset.xs; xhUI(); xhRender(true); xhPush(); return; }
  const c = e.target.closest('[data-xc]'); if (c) { XH.cfg.color = c.dataset.xc; xhUI(); xhPush(); return; }
  const b = e.target.closest('[data-xb]'); if (b) { XH.cfg[b.dataset.xb] = !XH.cfg[b.dataset.xb]; xhUI(); xhPush(); return; }
  const k = e.target.closest('[data-xk]'); if (k) { XH.key = +k.dataset.xk; xhState(); xhPush(false); return; }
  const t = e.target.closest('[data-xt]'); if (t) { if (t.dataset.xt === 'on') return xhToggle(); XH.onlyGame = !XH.onlyGame; xhState(); xhPush(false); return; }
  const bg = e.target.closest('#xhBg button'); if (bg) xhScene(bg.dataset.bg);
});
document.addEventListener('input', e => {
  const r = e.target.closest('[data-xr]');
  if (r) {
    const k = r.dataset.xr, v = +r.value, u = k === 'alpha' ? '%' : '';
    if (k === 'offsetX' || k === 'offsetY') XH[k] = v; else XH.cfg[k] = v;
    r.style.setProperty('--p', (v - r.min) / (r.max - r.min) * 100 + '%'); r.nextElementSibling.textContent = v + u;
    $$('.xpre').forEach(x => x.classList.toggle('on', JSON.stringify(XH_PRESETS[x.dataset.xp][1]) === JSON.stringify(XH.cfg)));
    xhRender(); xhPush(); return;
  }
  if (e.target.id === 'xhPick') { XH.cfg.color = e.target.value.toUpperCase(); xhRender(); xhPush(); }
});
document.addEventListener('change', e => { if (e.target.id === 'xhPick') xhUI(); });
document.addEventListener('click', e => { if (!e.target.closest('#xhShrink')) return; S.settings.xhairShrink = S.settings.xhairShrink === false; api('settings.set', { key: 'xhairShrink', value: S.settings.xhairShrink }); xhState(); });

/* ======================= SCREEN COLOR ======================= */
const DP_PRESETS = {
  std: ['Standard', 'Original', { vibrance: 0, brightness: 0, contrast: 0, gamma: 100 }],
  vivid: ['Lebendig', 'Kräftige Farben', { vibrance: 45, brightness: 0, contrast: 8, gamma: 100 }],
  night: ['Nachtsicht', 'Dunkles sichtbar', { vibrance: 20, brightness: 10, contrast: -4, gamma: 140 }],
  sharp: ['Kontrast', 'Klare Kanten', { vibrance: 25, brightness: -3, contrast: 20, gamma: 95 }],
  cine: ['Kino', 'Satt und warm', { vibrance: 12, brightness: -3, contrast: 12, gamma: 90 }],
};
const DP_SL = [['vibrance', 'Farbintensität', 'NVIDIA Digital Vibrance', 0, 100], ['brightness', 'Helligkeit', 'Gesamtes Bild', -50, 50], ['contrast', 'Kontrast', 'Abstand hell zu dunkel', -50, 50], ['gamma', 'Gamma', 'Hellt Schatten auf', 50, 200]];
const DP = { cfg: { on: false, onlyGame: true, vibrance: 0, brightness: 0, contrast: 0, gamma: 100, key: 0 }, nvidia: false, unlocked: false, gammaOk: null, vibOk: null };
const dpFmt = (k, v) => k === 'gamma' ? (v / 100).toFixed(2) : k === 'vibrance' ? v + '%' : (v > 0 ? '+' : '') + v;
const dpFilter = c => `saturate(${1 + c.vibrance / 45}) brightness(${1 + c.brightness / 70 + (c.gamma - 100) / 220}) contrast(${1 + c.contrast / 60})`;
function dpUI() {
  const c = DP.cfg;
  $('#dpPresets').innerHTML = Object.entries(DP_PRESETS).map(([k, [n, d, v]]) => { const on = Object.keys(v).every(x => v[x] === c[x]); return `<div class="card dpre ${on ? 'on' : ''}" data-dp="${k}"><i style="filter:${dpFilter(v)}"></i><b>${n}</b><span>${d}</span></div>`; }).join('');
  $('#dpSliders').innerHTML = DP_SL.map(([k, n, d, mi, ma]) => {
    const v = c[k], p = (v - mi) / (ma - mi) * 100, off = k === 'vibrance' && !DP.nvidia;
    return `<div class="dps ${off ? 'off' : ''}"><div class="n"><b>${n}${k === 'vibrance' ? `<span class="chip ${DP.nvidia ? 'g' : ''}" style="height:20px;font-size:10.5px">${DP.nvidia ? 'NVIDIA' : 'nur NVIDIA'}</span>` : ''}</b><span>${d}</span></div><input type="range" min="${mi}" max="${ma}" value="${v}" style="--p:${p}%" data-dr="${k}" ${off ? 'disabled' : ''}><b>${dpFmt(k, v)}</b></div>`;
  }).join('');
  $('#dpKey').innerHTML = KEYS.map(([n, v]) => `<button class="${c.key === v ? 'on' : ''}" data-dk="${v}">${n}</button>`).join('');
  const kn = KEYS.find(k => k[1] === c.key)?.[0];
  $('#dpKeyTxt').textContent = c.key ? `${kn} schaltet das Farbprofil im Spiel an und aus` : 'Profil im Spiel schnell umschalten';
  dpState();
  const u = bgUrl(); $$('.dpre i').forEach(i => i.style.backgroundImage = `url('${u}')`); $$('.dpimg').forEach(d => d.style.backgroundImage = `url('${u}')`);
}
function dpState() {
  const c = DP.cfg;
  $('#dpOnly').classList.toggle('on', c.onlyGame);
  $('#dpMain').querySelector('span').textContent = c.on ? 'Ausschalten' : 'Einschalten';
  $('#dpMain').classList.toggle('p', !c.on);
  $('#navDp').classList.toggle('hidden', !c.on);
  $('#dpLive').className = 'chip ' + (c.on ? 'g' : ''); $('#dpLive').textContent = c.on ? (c.onlyGame ? 'An · nur im Spiel' : 'An') : 'Aus';
  $('#dpAfter').style.filter = dpFilter(c.on ? c : DP_PRESETS.std[2]);
  const st = [];
  st.push(DP.nvidia ? `<div class="dstat ok">${ico('check')}<div><b>NVIDIA erkannt.</b> Farbintensität wird direkt im Treiber gesetzt.</div></div>`
    : `<div class="dstat warn">${ico('alert')}<div><b>Farbintensität nur mit NVIDIA.</b> Bei AMD: Radeon Software → Anzeige → Sättigung. Helligkeit, Kontrast und Gamma funktionieren überall.</div></div>`);
  if (DP.gammaOk === false && !DP.unlocked) st.push(`<div class="dstat warn">${ico('alert')}<div><b>Windows begrenzt die Stärke.</b> Schalte den vollen Gamma-Bereich frei, danach einmal neu starten.<br><button class="btn sm" onclick="dpUnlock()">${ico('zap')}Freischalten</button></div></div>`);
  else if (DP.gammaOk === false) st.push(`<div class="dstat warn">${ico('alert')}<div><b>Wert zu stark.</b> Starte den PC neu, falls du den Bereich gerade freigeschaltet hast, oder nimm etwas zurück.</div></div>`);
  st.push(`<div class="dstat">${ico('monitor')}<div>Wirkt sofort, ohne FiveM-Neustart. HDR oder der Windows-Nachtmodus können die Werte überschreiben.</div></div>`);
  $('#dpStatus').innerHTML = st.join('');
}
let dpT = 0, dpBusy = false, dpAgain = false;
function dpPush() {
  clearTimeout(dpT);
  dpT = setTimeout(async () => {
    if (dpBusy) { dpAgain = true; return; }
    dpBusy = true;
    try { const r = await api('display.set', { cfg: DP.cfg }); DP.gammaOk = r.gammaOk; DP.vibOk = r.vibOk; if (r.keyOk === false) toast('Taste belegt', 'Andere Taste wählen', false); dpState(); }
    catch { }
    dpBusy = false; if (dpAgain) { dpAgain = false; dpPush(); }
  }, 70);
}
function dpToggle() { DP.cfg.on = !DP.cfg.on; dpState(); dpPush(); toast(DP.cfg.on ? 'Farbprofil an' : 'Farbprofil aus', DP.cfg.on && DP.cfg.onlyGame ? 'Wirkt, sobald FiveM im Vordergrund ist' : ''); }
function dpReset() { Object.assign(DP.cfg, DP_PRESETS.std[2]); dpUI(); dpPush(); }
async function dpUnlock() { const r = await api('display.unlock'); DP.unlocked = !!r.ok; dpState(); toast('Gamma-Bereich freigeschaltet', 'Starte den PC einmal neu'); }
document.addEventListener('click', e => {
  const p = e.target.closest('[data-dp]'); if (p) { Object.assign(DP.cfg, DP_PRESETS[p.dataset.dp][2]); if (p.dataset.dp !== 'std') DP.cfg.on = true; dpUI(); dpPush(); return; }
  const k = e.target.closest('[data-dk]'); if (k) { DP.cfg.key = +k.dataset.dk; dpUI(); dpPush(); return; }
  if (e.target.closest('#dpOnly')) { DP.cfg.onlyGame = !DP.cfg.onlyGame; dpState(); dpPush(); }
});
document.addEventListener('input', e => {
  const r = e.target.closest('[data-dr]'); if (!r) return;
  const k = r.dataset.dr, v = +r.value; DP.cfg[k] = v; DP.cfg.on = true;
  r.style.setProperty('--p', (v - r.min) / (r.max - r.min) * 100 + '%'); r.nextElementSibling.textContent = dpFmt(k, v);
  $$('.dpre').forEach(x => { const pv = DP_PRESETS[x.dataset.dp][2]; x.classList.toggle('on', Object.keys(pv).every(q => pv[q] === DP.cfg[q])); });
  dpState(); dpPush();
});
async function initFx(s) {
  const x = s.xhair || {};
  try { if (x.json) XH.cfg = { ...XH_DEF, ...JSON.parse(x.json) }; } catch { }
  Object.assign(XH, { on: !!x.on, onlyGame: x.onlyGame === true, key: x.key ?? 0x79, offsetX: x.offsetX || 0, offsetY: x.offsetY || 0 });
  xhUI(); xhPush();
  try {
    const d = await api('display.get');
    Object.assign(DP.cfg, d.cfg); DP.nvidia = d.nvidia; DP.unlocked = d.unlocked; DP.gammaOk = d.gammaOk;
  } catch { }
  dpUI();
}

/* ======================= F8 COMMANDS ======================= */
// Only commands from the official FiveM docs / production console whitelist.
// A click sends the command straight into FiveM's F8 console (OrbitTweak types it for you).
const F8 = [
  ['Anzeige', 'monitor', [
    { k: 'cl_drawfps', n: 'FPS-Anzeige', d: 'Zeigt die FPS oben in der Ecke.', t: 'bool', def: false },
    { k: 'cl_drawperf', n: 'Performance-Anzeige', d: 'FPS, Ping, Paketverlust, CPU/GPU-Last und GPU-Temperatur.', t: 'bool', def: false },
    { k: 'profile_reticuleSize', n: 'GTA-Fadenkreuz Größe', d: '0 = kleinstmöglich. Ganz ausblenden kann es nur der Server.', t: 'range', min: 0, max: 10, def: 0 },
  ]],
  ['Kamera', 'eye', [
    { k: 'cam_disableCameraShake', n: 'Kamerawackeln aus', d: 'Kein Wackeln bei Explosionen und Treffern.', t: 'bool', def: false },
    { k: 'cam_enableHandbrakeCamera', n: 'Handbremsen-Kameraschwenk', d: 'Aus: Die Kamera schwenkt beim Driften nicht mehr mit.', t: 'bool', def: true },
    { k: 'cam_vehicleFirstPersonFOV', n: 'Fahrzeug-Ego-Sicht (FOV)', d: 'Sichtfeld in der Ego-Ansicht im Auto. -1 = Standard.', t: 'range', min: -1, max: 130, def: -1 },
  ]],
  ['Performance', 'zap', [
    { k: 'str_maxVehicleTextureRes', n: 'Fahrzeug-Texturen begrenzen', d: 'Weniger Speicher, weniger Texture-Loss. Autos etwas unschärfer.', t: 'seg', o: [256, 512, 1024], def: 1024 },
    { k: 'voice_enableNoiseSuppression', n: 'Voice: Rauschunterdrückung', d: 'Filtert Hintergrundgeräusche im Voice-Chat.', t: 'bool', def: true },
  ]],
  ['Audio', 'activity', [
    { k: 'profile_musicVolumeInMp', n: 'Musik im Multiplayer', d: '0 = aus, 10 = 100 %.', t: 'range', min: 0, max: 10, def: 10 },
    { k: 'profile_sfxVolume', n: 'Effekt-Lautstärke', d: 'Schüsse, Autos, Umgebung. 10 = 100 %.', t: 'range', min: 0, max: 10, def: 10 },
  ]],
];
const F8ALL = () => F8.flatMap(x => x[2]);
const f8Val = {}, f8St = {};
const f8Get = c => { const v = f8Val[c.k]; if (v == null) return c.def; return c.t === 'bool' ? v === 'true' || v === true : +v; };
const f8Cmd = (c, v = f8Get(c)) => `${c.k} ${v}`;
function f8Status(k) {
  const s = f8St[k];
  return s === 'sending' ? '<span class="chip a">Sende…</span>' : s === 'sent' ? '<span class="chip g">Live gesetzt</span>' : s === 'queued' ? '<span class="chip w">Beim Spielstart</span>' : s === 'error' ? '<span class="chip r">Fehler</span>' : '';
}
function renderF8() {
  $('#f8Auto').classList.toggle('on', S.settings.f8Auto !== false);
  $('#f8list').innerHTML = F8.map(([cat, icon, items]) => `<div class="card"><div class="lbl">${ico(icon)}${cat}</div>${items.map(c => {
    const v = f8Get(c);
    const ctl = c.t === 'bool' ? `<div class="tg ${v ? 'on' : ''}" data-f8b="${c.k}"></div>`
      : c.t === 'seg' ? `<div class="seg" data-f8="${c.k}">${c.o.map(o => `<button class="${v === o ? 'on' : ''}" data-v="${o}">${o}</button>`).join('')}</div>`
      : `<div class="rng"><input type="range" min="${c.min}" max="${c.max}" value="${v}" style="--p:${(v - c.min) / (c.max - c.min) * 100}%" data-f8r="${c.k}"><b>${v}</b></div>`;
    return `<div class="f8row"><div class="top"><div><b>${c.n}</b><p>${c.d}</p></div>${ctl}</div><div class="f8meta"><code id="f8c-${c.k}">${esc(f8Cmd(c))}</code><span id="f8s-${c.k}">${f8Status(c.k)}</span><button class="ib" title="Kopieren" onclick="copyCmd($('#f8c-${c.k}').textContent)">${ico('copy')}</button></div></div>`;
  }).join('')}</div>`).join('');
}
async function f8Send(c, v) {
  f8Val[c.k] = String(v);
  const set = (st) => { f8St[c.k] = st; const el = $('#f8s-' + c.k); if (el) el.innerHTML = f8Status(c.k); };
  const code = $('#f8c-' + c.k); if (code) code.textContent = f8Cmd(c, v);
  set('sending');
  try {
    const r = await api('f8.send', { cmds: [f8Cmd(c, v)] });
    if (r.sent) { set('sent'); toast(`${c.n}: ${c.t === 'bool' ? (v ? 'an' : 'aus') : v}`, 'Live an FiveM gesendet'); }
    else if (r.error && r.error !== 'FiveM läuft nicht') { set('error'); toast('Nicht gesendet', r.error, false); }
    else { set('queued'); toast('Gespeichert', 'FiveM läuft nicht. Wird beim nächsten Spielstart automatisch gesetzt.'); }
  } catch (e) { set('error'); toast('Nicht gesendet', e.message, false); }
}
window.f8Quick = async (k, v) => { const c = F8ALL().find(x => x.k === k); await f8Send(c, v); if (curPage === 'f8') renderF8(); };
document.addEventListener('click', e => {
  const t = e.target.closest('[data-f8b]');
  if (t) { const c = F8ALL().find(x => x.k === t.dataset.f8b), v = !f8Get(c); t.classList.toggle('on', v); f8Send(c, v); return; }
  const b = e.target.closest('[data-f8] button');
  if (b) { const c = F8ALL().find(x => x.k === b.parentElement.dataset.f8); b.parentElement.querySelectorAll('button').forEach(x => x.classList.toggle('on', x === b)); f8Send(c, +b.dataset.v); return; }
  if (e.target.closest('#f8Auto')) { S.settings.f8Auto = S.settings.f8Auto === false; api('settings.set', { key: 'f8Auto', value: S.settings.f8Auto }); renderF8(); }
});
document.addEventListener('input', e => {
  const r = e.target.closest('[data-f8r]'); if (!r) return;
  const c = F8ALL().find(x => x.k === r.dataset.f8r);
  r.style.setProperty('--p', (r.value - r.min) / (r.max - r.min) * 100 + '%'); r.nextElementSibling.textContent = r.value;
  $('#f8c-' + c.k).textContent = f8Cmd(c, +r.value);
});
document.addEventListener('change', e => { const r = e.target.closest('[data-f8r]'); if (r) f8Send(F8ALL().find(x => x.k === r.dataset.f8r), +r.value); });
async function loadF8() { try { Object.assign(f8Val, await api('f8.values')); } catch { } }

/* ======================= LIVE DISPLAY MODE ======================= */
async function gwRefresh() {
  try {
    const g = await api('gw.info');
    const el = $('#gwState');
    el.className = 'chip ' + (g.found ? (g.mode === 'windowed' ? 'w' : 'g') : '');
    el.textContent = !g.found ? 'FiveM nicht gestartet' : g.mode === 'windowed' ? `Fenster · ${g.w}×${g.h}` : g.mode === 'borderless' ? `Randlos/Vollbild · ${g.w}×${g.h}` : `Vollbild · ${g.w}×${g.h}`;
    $('#gwAuto').classList.toggle('on', !!S.settings.autoBorderless);
  } catch { }
}
async function gwSet(mode) {
  const r = await api('gw.' + mode).catch(e => ({ error: e.message }));
  if (r.error) toast('Geht gerade nicht', r.error, false);
  else toast(mode === 'borderless' ? 'Randloses Vollbild aktiv' : 'Fenstermodus aktiv', 'Sofort, ohne Neustart');
  gwRefresh();
}
document.addEventListener('click', e => {
  if (!e.target.closest('#gwAuto')) return;
  S.settings.autoBorderless = !S.settings.autoBorderless;
  api('settings.set', { key: 'autoBorderless', value: S.settings.autoBorderless });
  gwRefresh();
  if (S.settings.autoBorderless) toast('Automatisch randlos', 'Sobald FiveM im Fenstermodus läuft');
});
setInterval(() => { if (curPage === 'gfx') gwRefresh(); }, 2500);

/* ======================= FIVEM LOOK ======================= */
function sceneClone(suffix) {
  const src = $('#xhScene .gscene'); if (!src) return '';
  return src.outerHTML.replace(/\b(gsky|gsun|groad)\b/g, `$1${suffix}`);
}
function timeOfDay(d = new Date()) { const h = d.getHours(); return h >= 6 && h < 17 ? 'day' : h >= 17 && h < 21 ? 'dusk' : 'night'; }
function banner() {
  const d = new Date(), h = d.getHours(), tod = timeOfDay(d);
  $('#bnScene').className = 'bn-scene';
  $('#greet').textContent = h < 5 ? 'Gute Nacht' : h < 11 ? 'Guten Morgen' : h < 17 ? 'Guten Tag' : h < 22 ? 'Guten Abend' : 'Gute Nacht';
  $('#bnTime').textContent = `Los Santos · ${d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}`;
}
function initLook() {
  $('#bnScene').innerHTML = `<div class="own-bg" style="background-image:url('${LA[timeOfDay()]}')"></div>`; $('#spScene').innerHTML = $('#bnScene').innerHTML;
  banner(); setInterval(() => { banner(); if (!ownUrl() && !S.settings.bgPreset) applyBg(); }, 30000);
}
function passed(title, sub) {
  const p = $('#passed'); $('#paT').textContent = title; $('#paS').textContent = sub || '';
  p.classList.remove('show'); void p.offsetWidth; p.classList.add('show');
  clearTimeout(p._t); p._t = setTimeout(() => p.classList.remove('show'), 3300);
}
window.passed = passed;

/* ======================= MINIMAP ======================= */
const MM_STYLES = [
  ['round', 'Rund', '<circle cx="12" cy="12" r="7"/>'],
  ['square', 'Quadrat', '<rect x="5" y="5" width="14" height="14" rx="3"/>'],
  ['frame', 'Rahmen', '<rect x="3" y="6" width="18" height="12" rx="2"/>'],
  ['glow', 'Neon', '<rect x="3" y="6" width="18" height="12" rx="2"/><rect x="1" y="4" width="22" height="16" rx="3" opacity=".4"/>'],
  ['corners', 'Ecken', '<path d="M3 9V6h4M17 6h4v3M21 15v3h-4M7 18H3v-3"/>'],
];
const MM = { cfg: { style: 'round', color: '#FF7A3D', thick: 3, mask: 92, glow: true }, on: false, x: 0, y: 0, w: 0, h: 0, res: { w: 1920, h: 1080 }, align: false, P: 26 };
function mmDefaults(push = true) {
  const H = MM.res.h;
  Object.assign(MM, { x: Math.round(H * .026), y: Math.round(H * .045), w: Math.round(H * .243), h: Math.round(H * .157) });
  if (push) { mmUI(); mmPush(); }
}
function mmMapMock(x, w, h) {
  x.fillStyle = '#86977A'; x.fillRect(0, 0, w, h);
  x.fillStyle = '#6E8FB0'; x.beginPath(); x.moveTo(w * .7, 0); x.lineTo(w, 0); x.lineTo(w, h * .55); x.quadraticCurveTo(w * .8, h * .4, w * .7, 0); x.fill();
  x.strokeStyle = '#D9D4C5'; x.lineWidth = Math.max(2, h / 40);
  [[0, h * .45, w, h * .5], [w * .3, 0, w * .38, h], [0, h * .9, w * .8, h * .2], [w * .62, h, w * .7, h * .35]].forEach(([a, b, c, d]) => { x.beginPath(); x.moveTo(a, b); x.lineTo(c, d); x.stroke(); });
  x.fillStyle = '#fff'; x.beginPath(); x.moveTo(w / 2, h / 2 - 7); x.lineTo(w / 2 + 5, h / 2 + 6); x.lineTo(w / 2, h / 2 + 3); x.lineTo(w / 2 - 5, h / 2 + 6); x.fill();
}
function mmDraw(align = MM.align) {
  const { P, w, h } = MM, c = MM.cfg, cv = document.createElement('canvas'); cv.width = w + 2 * P; cv.height = h + 2 * P;
  const x = cv.getContext('2d'), mask = `rgba(9,9,11,${c.mask / 100})`, t = c.thick;
  const glow = () => { if (c.glow || c.style === 'glow') { x.shadowColor = c.color; x.shadowBlur = c.style === 'glow' ? 18 : 10; } };
  const rr = (X, Y, W, H, r) => { x.beginPath(); x.roundRect(X, Y, W, H, r); };
  if (c.style === 'round' || c.style === 'square') {
    const s = h, cx = P + w / 2, cy = P + h / 2;
    x.beginPath(); x.rect(P, P, w, h);
    if (c.style === 'round') x.arc(cx, cy, s / 2 - t / 2, 0, Math.PI * 2, true); else x.roundRect(cx - s / 2 + t / 2, P + t / 2, s - t, s - t, 12);
    x.fillStyle = mask; x.fill('evenodd');
    glow(); x.strokeStyle = c.color; x.lineWidth = t; x.beginPath();
    if (c.style === 'round') x.arc(cx, cy, s / 2 - t / 2, 0, Math.PI * 2); else x.roundRect(cx - s / 2 + t / 2, P + t / 2, s - t, s - t, 12);
    x.stroke();
  } else if (c.style === 'corners') {
    glow(); x.strokeStyle = c.color; x.lineWidth = t; x.lineCap = 'round'; const L = Math.min(w, h) * .22;
    [[P, P + L, P, P, P + L, P], [P + w - L, P, P + w, P, P + w, P + L], [P + w, P + h - L, P + w, P + h, P + w - L, P + h], [P + L, P + h, P, P + h, P, P + h - L]]
      .forEach(([a, b, c2, d, e, f]) => { x.beginPath(); x.moveTo(a, b); x.lineTo(c2, d); x.lineTo(e, f); x.stroke(); });
  } else {
    glow(); x.strokeStyle = c.color; x.lineWidth = t; rr(P - t / 2, P - t / 2, w + t, h + t, 8); x.stroke();
    if (c.style === 'glow') { x.shadowBlur = 0; x.globalAlpha = .45; x.lineWidth = 1; rr(P - t - 3, P - t - 3, w + 2 * t + 6, h + 2 * t + 6, 11); x.stroke(); x.globalAlpha = 1; }
  }
  if (align) { x.shadowBlur = 0; x.fillStyle = 'rgba(255,0,200,.16)'; x.fillRect(P, P, w, h); x.setLineDash([6, 4]); x.strokeStyle = '#FF2BD6'; x.lineWidth = 2; x.strokeRect(P + 1, P + 1, w - 2, h - 2); }
  return cv;
}
function mmRender() {
  const img = mmDraw(), cv = $('#mmCanvas'), P = MM.P;
  cv.width = img.width; cv.height = img.height;
  const x = cv.getContext('2d'); x.save(); x.translate(P, P); mmMapMock(x, MM.w, MM.h); x.restore(); x.drawImage(img, 0, 0);
  cv.style.width = Math.min(img.width * 1.4, 520) + 'px';
}
let mmT = 0;
function mmPush() {
  mmRender(); clearTimeout(mmT);
  mmT = setTimeout(async () => {
    const png = mmDraw().toDataURL('image/png').split(',')[1];
    const r = await api('minimap.set', { on: MM.on, x: MM.x - MM.P, y: MM.y - MM.P, png, json: JSON.stringify({ ...MM.cfg, x: MM.x, y: MM.y, w: MM.w, h: MM.h }) }).catch(() => null);
    if (r && r.client && r.client.h) { MM.res = r.client; $('#mmRes').textContent = `${r.client.w}×${r.client.h}`; if (MM.fresh) { MM.fresh = false; mmDefaults(); } }
  }, 120);
}
function mmUI() {
  $('#mmStyles').innerHTML = MM_STYLES.map(([k, n, svg]) => `<button class="xst ${MM.cfg.style === k ? 'on' : ''}" data-mms="${k}"><svg viewBox="0 0 24 24">${svg}</svg>${n}</button>`).join('');
  const cols = [getComputedStyle(document.documentElement).getPropertyValue('--a').trim().toUpperCase(), '#FFFFFF', '#3DE0FF', '#3DFF8B', '#FFE03D', '#FF3B5C', '#B78CFF'];
  $('#mmColors').innerHTML = [...new Set(cols)].map(c => `<button class="xcol ${MM.cfg.color.toUpperCase() === c ? 'on' : ''}" data-mmc="${c}" style="background:${c}"></button>`).join('') + `<label class="xcustom" title="Eigene Farbe"><input type="color" id="mmPick" value="${MM.cfg.color}"></label>`;
  const H = MM.res.h, sl = [['thick', 'Dicke', 1, 8, MM.cfg.thick], ['mask', 'Abdeckung', 40, 100, MM.cfg.mask, '%'], ['x', 'Links', 0, Math.round(H * .2), MM.x], ['y', 'Unten', 0, Math.round(H * .2), MM.y], ['w', 'Breite', 100, Math.round(H * .5), MM.w], ['h', 'Höhe', 60, Math.round(H * .4), MM.h]];
  $('#mmSliders').innerHTML = `<div class="xs tgl"><span>Leuchten</span><div class="tg ${MM.cfg.glow ? 'on' : ''}" data-mmb="glow"></div></div>` +
    sl.map(([k, n, mi, ma, v, u = '']) => `<div class="xs"><span>${n}</span><input type="range" min="${mi}" max="${ma}" value="${v}" style="--p:${(v - mi) / (ma - mi) * 100}%" data-mmr="${k}"><b>${v}${u}</b></div>`).join('');
  $('#mmAlign').classList.toggle('on', MM.align);
  $('#mmMain').querySelector('span').textContent = MM.on ? 'Ausschalten' : 'Einschalten';
  $('#mmMain').classList.toggle('p', !MM.on);
  $('#navMm').classList.toggle('hidden', !MM.on);
  mmRender();
}
function mmToggle() { MM.on = !MM.on; mmUI(); mmPush(); toast(MM.on ? 'Minimap-Overlay an' : 'Minimap-Overlay aus', MM.on ? 'Erscheint, sobald FiveM vorne ist · Server-Regeln beachten' : ''); }
window.mmToggle = mmToggle; window.mmDefaults = mmDefaults;
document.addEventListener('click', e => {
  const s = e.target.closest('[data-mms]'); if (s) { MM.cfg.style = s.dataset.mms; mmUI(); mmPush(); return; }
  const c = e.target.closest('[data-mmc]'); if (c) { MM.cfg.color = c.dataset.mmc; mmUI(); mmPush(); return; }
  const b = e.target.closest('[data-mmb]'); if (b) { MM.cfg[b.dataset.mmb] = !MM.cfg[b.dataset.mmb]; mmUI(); mmPush(); return; }
  if (e.target.closest('#mmAlign')) { MM.align = !MM.align; if (MM.align && !MM.on) { MM.on = true; } api('minimap.preview', { on: MM.align }); mmUI(); mmPush(); if (MM.align) toast('Ausrichten-Modus', 'Starte FiveM und schieb den pinken Rahmen mit den Reglern genau auf die Minimap'); }
});
document.addEventListener('input', e => {
  const r = e.target.closest('[data-mmr]');
  if (r) { const k = r.dataset.mmr, v = +r.value; if (k in MM.cfg) MM.cfg[k] = v; else MM[k] = v; r.style.setProperty('--p', (v - r.min) / (r.max - r.min) * 100 + '%'); r.nextElementSibling.textContent = v + (k === 'mask' ? '%' : ''); mmPush(); return; }
  if (e.target.id === 'mmPick') { MM.cfg.color = e.target.value.toUpperCase(); mmPush(); }
});
// 2.9.0: the frame overlay was replaced by real minimap mods – make sure an old overlay is off
function initMinimap(s) { if (s.minimap && s.minimap.on) api('minimap.set', { on: false, x: 0, y: 0 }).catch(() => { }); }

/* ======================= CLIPPER ======================= */
const CL_RECS = {
  nvidia: ['NVIDIA', 'Alt+F10', 'In der NVIDIA App (Alt+Z) unter Aufnahme „Instant Replay“ einschalten. Clips landen in Videos.'],
  gamebar: ['Game Bar', 'Win+Alt+G', 'Windows Game Bar mit Hintergrundaufnahme. Clips landen in Videos\\Captures.'],
  amd: ['AMD', 'Strg+Shift+S', 'In AMD Adrenalin „Instant Replay“ aktivieren.'],
  obs: ['OBS', 'Strg+Alt+F12', 'In OBS den Replay-Puffer starten und unter Einstellungen → Hotkeys „Wiederholung speichern“ auf Strg+Alt+F12 legen.'],
};
const CL = { key: 0x78, recorder: 'nvidia', flash: true };
function clFlashPng() {
  const cv = document.createElement('canvas'); cv.width = 380; cv.height = 58; const x = cv.getContext('2d');
  x.fillStyle = 'rgba(10,10,12,.9)'; x.beginPath(); x.roundRect(0, 0, 380, 58, 12); x.fill();
  const a = getComputedStyle(document.documentElement).getPropertyValue('--a').trim();
  x.fillStyle = a; x.fillRect(0, 10, 4, 38);
  x.fillStyle = '#FF3B3B'; x.beginPath(); x.arc(30, 29, 7, 0, Math.PI * 2); x.fill();
  x.fillStyle = '#fff'; x.font = '700 18px Geist, Inter, sans-serif'; x.fillText('CLIP GESPEICHERT', 50, 27);
  x.fillStyle = '#A1A1AA'; x.font = '500 12px Inter, sans-serif'; x.fillText(`OrbitTweak · ${CL_RECS[CL.recorder][0]}`, 50, 44);
  return cv.toDataURL('image/png').split(',')[1];
}
async function clipPush() {
  const r = await api('clip.cfg', { key: CL.key, recorder: CL.recorder, flash: CL.flash, png: clFlashPng() }).catch(() => null);
  if (r && r.keyOk === false) toast('Taste belegt', 'Andere Clip-Taste wählen', false);
}
function clipUI() {
  $('#clRecs').innerHTML = Object.entries(CL_RECS).map(([k, [n, combo]]) => `<button class="cl-rec ${CL.recorder === k ? 'on' : ''}" data-rec="${k}"><b>${n}</b><span>${combo}</span></button>`).join('');
  $('#clRecTxt').innerHTML = esc(CL_RECS[CL.recorder][2]) + (CL.recorder === 'gamebar' ? `<br><button class="btn sm mt8" onclick="clipGameBar()">${ico('zap')}Hintergrundaufnahme einschalten</button>` : '');
  $('#clKey').innerHTML = KEYS.map(([n, v]) => `<button class="${CL.key === v ? 'on' : ''}" data-clk="${v}">${n}</button>`).join('');
  $('#clFlash').classList.toggle('on', CL.flash);
}
async function clipGameBar() { await api('clip.gamebar'); toast('Hintergrundaufnahme an', 'Hinweis: der Tweak „Xbox Game DVR aus“ ist damit wieder rückgängig'); }
window.clipGameBar = clipGameBar;
async function clipTest() { await api('clip.save'); toast('Clip gespeichert', `${CL_RECS[CL.recorder][0]} · ${CL_RECS[CL.recorder][1]}`); setTimeout(clipList, 4000); }
window.clipTest = clipTest;
async function clipOpen(rel) { api('clip.open', { rel }); }
window.clipOpen = clipOpen;
let clips = [];
async function clipList() {
  try { clips = await api('clip.list'); } catch { clips = []; }
  $('#clCount').textContent = clips.length;
  $('#clGrid').innerHTML = clips.length ? clips.map((c, i) => {
    const src = 'https://clips.orbit/' + c.rel.split('/').map(encodeURIComponent).join('/');
    const vid = `<img alt="" data-cth="${i}"><div class="noprev">${ico('film')}</div><div class="playi">${ico('play')}</div>`;
    return `<div class="clipc" data-clip="${i}"><div class="v">${vid}</div><div class="m"><b>${esc(c.name)}</b><span>${esc(c.folder || '')} · ${bytes(c.size)} · ${new Date(c.date).toLocaleDateString('de-DE')}</span></div></div>`;
  }).join('') : `<div class="empty">Noch keine Clips gefunden. Drück im Spiel deine Clip-Taste.</div>`;
  $$('#clGrid img[data-cth]').forEach(img => thumb('clip.thumb', { rel: clips[+img.dataset.cth].rel }, img));
}
window.clipList = clipList;

document.addEventListener('click', e => {
  const r = e.target.closest('[data-rec]'); if (r) { CL.recorder = r.dataset.rec; clipUI(); clipPush(); return; }
  const k = e.target.closest('[data-clk]'); if (k) { CL.key = +k.dataset.clk; clipUI(); clipPush(); return; }
  if (e.target.closest('#clFlash')) { CL.flash = !CL.flash; clipUI(); clipPush(); return; }
  const c = e.target.closest('[data-clip]'); if (c) {
    const cl = clips[+c.dataset.clip], src = 'https://clips.orbit/' + cl.rel.split('/').map(encodeURIComponent).join('/');
    modal(`<div class="kicker">Clip</div><h1 style="margin-top:8px;font-size:20px">${esc(cl.name)}</h1><div class="player mt">${cl.ext === '.mkv' ? '<p>MKV kann hier nicht abgespielt werden.</p>' : `<video src="${src}" controls autoplay></video>`}</div>
      <div class="act"><button class="btn" onclick="closeModal()">Schließen</button><button class="btn" onclick="clipOpen('${esc(cl.rel)}')">${ico('folder')}Im Ordner zeigen</button><button class="btn d" onclick="clipDel('${esc(cl.rel)}')">${ico('trash')}Löschen</button></div>`);
    $('#mbox').style.width = '760px';
  }
});
async function clipDel(rel) { const r = await api('clip.delete', { rel }); closeModal(); $('#mbox').style.width = ''; toast(r.ok ? 'Clip gelöscht' : 'Löschen fehlgeschlagen', r.error || '', r.ok); clipList(); }
window.clipDel = clipDel;
function initClip(s) { Object.assign(CL, s.clip || {}); clipUI(); clipPush(); }

/* ======================= MODS ======================= */
const MOD_CATS = { minimap: ['Minimap', 'map', 'Eigene Karten & Farben'], kill: ['Kill-Effekte', 'crosshair', 'Effekte bei Treffern & Kills'], gfx: ['Grafik-Packs', 'sun', 'Licht, Wetter, Farben'], sound: ['Sounds', 'activity', 'Waffen & Fahrzeuge'], other: ['Sonstiges', 'puzzle', 'Alles andere'] };
function modCatsUI_unused() { $('#modCats').innerHTML = Object.entries(MOD_CATS).map(([k, [n, i, d]]) => `<div class="card hov mcat" onclick="modInstall('${k}')"><div class="qi">${ico(i)}</div><b>${n}</b><span>${d}</span><div class="sub" style="font-size:11.5px;margin-top:8px;color:var(--a)">+ Installieren</div></div>`).join(''); }
async function modInstall(cat, ok, reshade) {
  if (!S.settings.modsAck && !ok) return modal(`<div class="kicker">Hinweis</div><h1 style="margin-top:10px">Mods auf eigene <span class="grad">Verantwortung</span></h1>
    <p>Viele FiveM-Server verbieten Mods (Pure-Mode). Mit Mods kannst du dort nicht joinen oder wirst gekickt, manche Server bannen dafür. Installiere nur Mods aus vertrauenswürdigen Quellen.</p>
    <div class="act"><button class="btn" onclick="closeModal()">Abbrechen</button><button class="btn p lg" onclick="modAck('${cat}',${reshade ? 'true' : 'false'})">${ico('check')}Verstanden</button></div>`);
  const r = await api('mods.install', { cat }).catch(e => ({ error: e.message }));
  if (r.cancelled) return;
  if (r.error) toast('Nicht installiert', r.error, false); else { toast(`${r.count} Mod(s) installiert`, 'Wirkt beim nächsten FiveM-Start'); passed('Mod installiert', `${MOD_CATS[cat]?.[0] || 'Mod'} · ${r.count} Datei(en)`); }
  modList();
}
function modAck(cat, reshade) { closeModal(); S.settings.modsAck = true; api('settings.set', { key: 'modsAck', value: true }); if (reshade) rsInstall(); else if (cat !== '__dl' && cat !== '__browse') modInstall(cat, true); }
window.modInstall = modInstall; window.modAck = modAck;
async function modList() {
  let r; try { r = await api('mods.list'); } catch { r = { found: false, items: [] }; }
  const items = r.items || [];
  $('#modCount').textContent = items.length;
  const row = m => `<div class="mod"><div class="ti" style="width:36px;height:36px;border-radius:11px;display:grid;place-items:center;background:rgba(var(--a-rgb),.1);color:var(--a)">${ico(MOD_CATS[m.cat]?.[1] || 'puzzle')}</div><div class="tx"><b>${esc(m.name)}</b><p>${MOD_CATS[m.cat]?.[0] || 'Sonstiges'} · ${bytes(m.size)}</p></div><span class="chip ${m.on ? 'g' : ''}">${m.on ? 'Aktiv' : 'Aus'}</span><div class="tg ${m.on ? 'on' : ''}" data-mod="${esc(m.name)}"></div><button class="ib" title="Löschen" data-moddel="${esc(m.name)}">${ico('trash')}</button></div>`;
  $('#modList').innerHTML = !r.found ? '<div class="empty">FiveM nicht gefunden. Pfad im Launcher wählen.</div>' : items.length ? items.map(row).join('') : '<div class="empty">Noch keine Mods installiert. Wähl oben eine Kategorie.</div>';
  const mm = items.filter(m => m.cat === 'minimap');
  if ($('#mmMods')) $('#mmMods').innerHTML = mm.map(row).join('');
}
document.addEventListener('click', async e => {
  const t = e.target.closest('[data-mod]');
  if (t) { const on = !t.classList.contains('on'); const r = await api('mods.toggle', { name: t.dataset.mod, on }); if (r.error) toast('Geht gerade nicht', r.error, false); else toast(on ? 'Mod aktiviert' : 'Mod deaktiviert', t.dataset.mod); modList(); return; }
  const d = e.target.closest('[data-moddel]');
  if (d && await confirmBox('Mod löschen?', d.dataset.moddel + ' wird entfernt.')) { const r = await api('mods.delete', { name: d.dataset.moddel }); if (r.error) toast('Nicht gelöscht', r.error, false); modList(); }
});

/* ======================= SNAP TAP / CONTROLLER / NET ======================= */
document.addEventListener('click', e => {
  if (!e.target.closest('#snapTg')) return;
  S.settings.snapTap = !S.settings.snapTap; $('#snapTg').classList.toggle('on', S.settings.snapTap);
  api('settings.set', { key: 'snapTap', value: S.settings.snapTap });
  toast(S.settings.snapTap ? 'Snap Tap an' : 'Snap Tap aus', S.settings.snapTap ? 'Nur aktiv, solange FiveM vorne ist · Server-Regeln beachten' : '');
});
const GP_NAMES = ['A', 'B', 'X', 'Y', 'LB', 'RB', 'LT', 'RT', 'Back', 'Start', 'LS', 'RS', '↑', '↓', '←', '→', 'Guide'];
let gpRaf = 0;
function gpStart() {
  cancelAnimationFrame(gpRaf);
  $('#gpBtns').innerHTML = GP_NAMES.map((n, i) => `<i data-gb="${i}">${n}</i>`).join('');
  $('#gpLT').style.transition = $('#gpRT').style.transition = 'none';
  const loop = () => {
    if (curPage !== 'input' || $('#padPad').classList.contains('hidden')) return;
    const gp = [...(navigator.getGamepads ? navigator.getGamepads() : [])].find(Boolean);
    $('#gpNone').classList.toggle('hidden', !!gp); $('#gpLive').classList.toggle('hidden', !gp);
    if (gp) {
      const ax = gp.axes, mv = (el, x, y) => el.style.transform = `translate(${(x || 0) * 40}px,${(y || 0) * 40}px)`;
      mv($('#gpL'), ax[0], ax[1]); mv($('#gpR'), ax[2], ax[3]);
      $('#gpLT').style.width = (gp.buttons[6]?.value || 0) * 100 + '%'; $('#gpRT').style.width = (gp.buttons[7]?.value || 0) * 100 + '%';
      $$('#gpBtns i').forEach(i => i.classList.toggle('on', !!gp.buttons[+i.dataset.gb]?.pressed));
      const l = Math.hypot(ax[0] || 0, ax[1] || 0), r = Math.hypot(ax[2] || 0, ax[3] || 0), idle = l < .3 && r < .3;
      $('#gpDrift').textContent = `${gp.id.split('(')[0].trim().slice(0, 28)} · Drift L ${l.toFixed(2)} · R ${r.toFixed(2)}${idle ? (Math.max(l, r) > .08 ? ' · Drift erkannt!' : ' · OK') : ''}`;
    }
    gpRaf = requestAnimationFrame(loop);
  };
  loop();
}
function gpRumble() { const gp = [...navigator.getGamepads()].find(Boolean); gp?.vibrationActuator?.playEffect('dual-rumble', { duration: 700, strongMagnitude: 1, weakMagnitude: .6 }).catch(() => { }); }
window.gpRumble = gpRumble;
let ntT = 'cfx';
document.addEventListener('click', e => { const b = e.target.closest('[data-nt]'); if (!b) return; ntT = b.dataset.nt; $$('#ntTarget button').forEach(x => x.classList.toggle('on', x === b)); $('#ntHost').classList.toggle('hidden', ntT !== 'custom'); });
async function netTest() {
  const target = ntT === 'custom' ? $('#ntHost').value.trim() : ntT;
  if (ntT === 'custom' && !/^[\w.\-]+:\d{1,5}$/.test(target)) return toast('Adresse prüfen', 'Format IP:Port, z. B. 123.45.67.89:30120', false);
  const btn = $('#ntBtn'); btn.disabled = true; $('#ntTxt').textContent = 'Messe…'; $('#ntBars').innerHTML = '';
  try {
    const r = await api('net.test', { target });
    if (!r.ok) { $('#ntAvg').textContent = '--'; $('#ntJit').textContent = '--'; $('#ntLoss').textContent = '100%'; $('#ntTxt').textContent = 'Keine Verbindung'; return; }
    countTo($('#ntAvg'), r.avg, ' ms'); $('#ntJit').textContent = r.jitter + ' ms'; $('#ntLoss').textContent = r.loss + '%';
    const mx = Math.max(...r.samples, 1);
    $('#ntBars').innerHTML = r.samples.map((s, i) => `<i style="height:${Math.max(8, s / mx * 100)}%;animation-delay:${i * 25}ms" title="${s} ms"></i>`).join('') + Array.from({ length: r.loss / 5 }, () => '<i class="lost" style="height:100%"></i>').join('');
    const good = r.avg < 60 && r.jitter < 6 && r.loss === 0, ok = r.avg < 110 && r.jitter < 15 && r.loss <= 5;
    $('#ntTxt').innerHTML = good ? '<span style="color:var(--good)">Top für Hitreg</span>' : ok ? '<span style="color:var(--warn)">Spielbar, leichte Schwankungen</span>' : '<span style="color:var(--bad)">Schlecht für Hitreg: Netzwerk-Tweaks anwenden, LAN statt WLAN</span>';
  } catch (e) { toast('Test fehlgeschlagen', e.message, false); }
  finally { btn.disabled = false; }
}
window.netTest = netTest;

/* ======================= THUMBNAIL QUEUE ======================= */
// one thumbnail at a time, so big video folders never block the app
const thumbQ = []; let thumbBusy = false;
function thumb(cmd, args, img) {
  thumbQ.push([cmd, args, img]); if (!thumbBusy) thumbNext();
}
async function thumbNext() {
  const job = thumbQ.shift(); if (!job) { thumbBusy = false; return; }
  thumbBusy = true; const [cmd, args, img] = job;
  if (img.isConnected) {
    try { const r = await api(cmd, args); if (r && r.url) { img.src = r.url; img.classList.add('ok'); } else img.classList.add('none'); } catch { img.classList.add('none'); }
  }
  setTimeout(thumbNext, 15);
}

/* ======================= GTA BACKGROUND ======================= */
const LA = { day: 'img/la-day.jpg', dusk: 'img/la-sunset.jpg', night: 'img/la-night.jpg' };
const LA_ALL = [['auto', 'Tageszeit', null], ['la-day', 'Tag', 'img/la-day.jpg'], ['la-sunset', 'Abendsonne', 'img/la-sunset.jpg'], ['la-dusk', 'Sonnenuntergang', 'img/la-dusk.jpg'], ['la-night', 'Nacht', 'img/la-night.jpg'], ['la-night2', 'Downtown', 'img/la-night2.jpg']];
const ownUrl = () => null;
const bgUrl = () => ownUrl() || (LA_ALL.find(x => x[0] === S.settings.bgPreset)?.[2]) || LA[timeOfDay()];
function applyBg() {
  const u = bgUrl();
  const own = `<div class="own-bg" style="background-image:url('${u}')"></div>`;
  $('#bnScene').innerHTML = own;
  $('#spScene').innerHTML = own;
  xhScene(timeOfDay());
  $$('.dpimg').forEach(d => d.style.backgroundImage = u ? `url('${u}')` : '');
  $$('.dpre i').forEach(d => d.style.backgroundImage = u ? `url('${u}')` : '');
  $$('.rsp i').forEach(i => i.style.backgroundImage = `url('${u}')`);
}
function xhScene(k) {
  const sc = $('#xhScene'); let el = sc.querySelector('.own-bg');
  if (!el) { sc.insertAdjacentHTML('afterbegin', '<div class="own-bg"></div>'); el = sc.querySelector('.own-bg'); }
  el.style.backgroundImage = `url('${k === 'own' ? ownUrl() : LA[k] || LA.day}')`;
  $$('#xhBg button').forEach(b => b.classList.toggle('on', b.dataset.bg === k));
  xhPlace();
}
async function bgGrid() {
  const g = $('#bgGrid'), cur = S.settings.bgPreset || 'auto';
  g.innerHTML = LA_ALL.map(([k, n, src]) => `<button class="bgc ${cur === k ? 'on' : ''}" data-bgp="${k}"><img alt="" class="ok" src="${src || LA[timeOfDay()]}"><span>${n}</span></button>`).join('');
}
document.addEventListener('click', async e => {
  const pr = e.target.closest('[data-bgp]');
  if (pr) {
    S.settings.bgPreset = pr.dataset.bgp === 'auto' ? null : pr.dataset.bgp; api('settings.set', { key: 'bgPreset', value: S.settings.bgPreset || '' });
    applyBg(); bgGrid(); return;
  }
  const b = e.target.closest('[data-bgi]'); if (!b) return;
  const it = $('#bgGrid')._items[+b.dataset.bgi];
  b.classList.add('busy');
  const r = await api('bg.set', it.k === 'rel' ? { rel: it.v } : { path: it.v });
  b.classList.remove('busy');
  if (r.error) return toast('Bild nicht übernommen', r.error, false);
  S.settings.bgVersion = r.v; applyBg(); bgGrid(); toast('GTA-Hintergrund gesetzt', 'Startseite, Ladebildschirm und Vorschauen nutzen jetzt dein Bild');
});
async function bgPick() { const r = await api('bg.pick'); if (r.cancelled) return; if (r.error) return toast('Bild nicht übernommen', r.error, false); S.settings.bgVersion = r.v; applyBg(); toast('GTA-Hintergrund gesetzt', ''); }
async function bgClear() { await api('bg.clear'); S.settings.bgVersion = 0; applyBg(); bgGrid(); }
window.bgPick = bgPick; window.bgClear = bgClear;

/* ======================= SETTINGS TABS ======================= */
function setTab(t) {
  $$('#setTabs .tab').forEach(b => b.classList.toggle('on', b.dataset.st === t));
  $$('.stab').forEach(s => s.classList.toggle('hidden', s.id !== 'st-' + t));
  if (t === 'design') bgGrid();
}
window.setTab = setTab;
document.addEventListener('click', e => { const t = e.target.closest('#setTabs .tab'); if (t) setTab(t.dataset.st); });

/* ======================= RESHADE ======================= */
let RS = { installed: false, presets: [], preset: null };
async function rsLoad() {
  try { RS = await api('reshade.status'); } catch { return; }
  if (!RS.found) { $('#rsState').textContent = 'FiveM nicht gefunden'; return; }
  $('#rsState').className = 'chip ' + (RS.installed ? 'g' : RS.other ? 'w' : '');
  $('#rsState').textContent = RS.installed ? `Installiert · v${RS.version}` : RS.other ? 'Andere dxgi.dll gefunden' : 'Nicht installiert';
  $('#rsBtn').classList.toggle('hidden', RS.installed);
  $('#rsDel').classList.toggle('hidden', !RS.installed);
  const sel = RS.preset || 'vivid';
  $('#rsPresets').innerHTML = (RS.presets || []).map(p => `<button class="rsp ${RS.installed && sel === p.id ? 'on' : ''}" data-rsp="${p.id}"><i class="rsf-${p.id}"></i><b>${p.name}</b><span>${p.desc}</span></button>`).join('');
  const u = bgUrl(); $$('.rsp i').forEach(i => i.style.backgroundImage = `url('${u}')`);
}
async function rsInstall() {
  if (!S.settings.modsAck) return modInstall('gfx', false, true);
  $('#rsBtn').disabled = true; $('#rsBar').classList.remove('hidden'); $('#rsProg').classList.remove('hidden');
  const r = await api('reshade.install', { preset: RS.preset || 'vivid' }).catch(e => ({ error: e.message }));
  $('#rsBtn').disabled = false;
  if (r.error) { toast('ReShade nicht installiert', r.error, false); $('#rsProg').textContent = r.error; }
  else { passed('ReShade installiert', 'Im Spiel mit Pos1 öffnen'); $('#rsBar').classList.add('hidden'); $('#rsProg').classList.add('hidden'); }
  rsLoad();
}
async function rsUninstall() { if (!await confirmBox('ReShade entfernen?', 'ReShade, Shader und Presets werden aus FiveM gelöscht.')) return; const r = await api('reshade.uninstall'); toast(r.error ? 'Nicht entfernt' : 'ReShade entfernt', r.error || '', !r.error); rsLoad(); }
window.rsInstall = rsInstall; window.rsUninstall = rsUninstall;
document.addEventListener('click', async e => {
  const p = e.target.closest('[data-rsp]'); if (!p) return;
  RS.preset = p.dataset.rsp; $$('.rsp').forEach(x => x.classList.toggle('on', x === p));
  if (!RS.installed) return toast('Preset gewählt', 'Wird bei der Installation gesetzt');
  const r = await api('reshade.preset', { id: RS.preset }); toast(r.error ? 'Nicht gesetzt' : 'Preset aktiv', r.error || 'Ab dem nächsten FiveM-Start, im Spiel mit Pos1 umschaltbar', !r.error);
});

/* ======================= MOD COLLECTION ======================= */
const COLL = [
  ['minimap', 'Satellit-Minimap', 'Echte Satellitenbilder statt Straßenkarte', 'fivem satellite minimap rpf'],
  ['minimap', 'Dunkle Minimap', 'Dark-Mode-Karte, schont die Augen nachts', 'fivem dark minimap rpf'],
  ['minimap', 'Atlas-Minimap', 'Klassischer Atlas-Look mit Straßennamen', 'fivem atlas minimap rpf'],
  ['minimap', 'Farbige Minimap', 'Kräftige Farben, Straßen gut lesbar', 'fivem colored minimap rpf'],
  ['minimap', 'Postal-Map', 'Postleitzahlen für RP-Server', 'fivem postal map minimap rpf'],
  ['kill', 'Kill-Effekte', 'Bildschirm-Effekte bei Kills', 'fivem kill effect rpf'],
  ['kill', 'Blood FX', 'Realistischeres Blut bei Treffern', 'fivem blood fx rpf'],
  ['kill', 'Tracer & Mündungsfeuer', 'Sichtbare Schüsse, mehr Feedback', 'fivem tracer muzzle flash rpf'],
  ['gfx', 'Grafik-Packs', 'Licht, Farben, Atmosphäre', 'fivem graphics pack rpf'],
  ['gfx', 'Wetter & Timecycle', 'Andere Tageszeiten-Stimmung', 'fivem timecycle weather mod rpf'],
  ['sound', 'Waffen-Sounds', 'Wuchtigere Schüsse', 'fivem gunshot sounds rpf'],
  ['sound', 'Motor-Sounds', 'Echte Motorsounds für Autos', 'fivem engine sound pack rpf'],
];
function collHtml(filter) {
  return COLL.filter(c => !filter || c[0] === filter).map(([cat, n, d, q]) => `<div class="card hov ccard"><div class="qi">${ico(MOD_CATS[cat][1])}</div><b>${n}</b><span>${d}</span><div class="row" style="margin-top:12px;gap:6px;flex-wrap:wrap"><button class="btn sm p" onclick="modBrowse('https://www.gta5-mods.com/search/${encodeURIComponent(q.replace(/ ?(fivem|rpf) ?/g, ' ').trim())}','${cat}')">${ico('globe')}GTA5-Mods</button><button class="btn sm" onclick="modBrowse('https://forum.cfx.re/search?q=${encodeURIComponent(q)}','${cat}')">${ico('globe')}Cfx.re</button></div></div>`).join('');
}
async function dlList(target, filter) {
  let l = []; try { l = await api('mods.downloads'); } catch { }
  if (filter) l = l.filter(x => x.cat === filter || x.cat === 'other');
  const el = $(target); if (!el) return;
  el.innerHTML = l.length ? l.map(f => `<div class="mod"><div class="ti" style="width:36px;height:36px;border-radius:11px;display:grid;place-items:center;background:rgba(var(--a-rgb),.1);color:var(--a)">${ico(MOD_CATS[f.cat]?.[1] || 'puzzle')}</div><div class="tx"><b>${esc(f.name)}</b><p>${bytes(f.size)} · ${new Date(f.date).toLocaleDateString('de-DE')} · ${f.rpfs > 0 ? f.rpfs + ' Mod-Datei(en)' : 'Archiv'}</p></div><select class="in sel" data-dlcat="${esc(f.name)}">${Object.entries(MOD_CATS).map(([k, [n]]) => `<option value="${k}" ${k === (filter || f.cat) ? 'selected' : ''}>${n}</option>`).join('')}</select><button class="btn p sm" data-dl="${esc(f.name)}">${ico('download')}Installieren</button></div>`).join('')
    : '<div class="empty">Noch nichts gefunden. Lade eine Mod herunter, sie erscheint hier automatisch.</div>';
}
document.addEventListener('click', async e => {
  const b = e.target.closest('[data-dl]'); if (!b) return;
  if (!S.settings.modsAck) return modInstall('__dl');
  const name = b.dataset.dl, cat = b.parentElement.querySelector('[data-dlcat]').value;
  b.disabled = true; b.innerHTML = `${ico('refresh', 'i spin')}Installiere…`;
  const r = await api('mods.installDl', { name, cat }).catch(er => ({ error: er.message }));
  if (r.error) { toast('Nicht installiert', r.error, false); b.disabled = false; b.innerHTML = `${ico('download')}Installieren`; }
  else { passed('Mod installiert', `${MOD_CATS[cat][0]} · ${r.count} Datei(en)`); modList(); }
});
function modBrowse(url, cat) {
  if (!S.settings.modsAck) return modInstall('__browse');
  api('mods.browse', { url, cat, accent: getComputedStyle(document.documentElement).getPropertyValue('--a').trim() });
  toast('Mod-Browser geöffnet', 'Klick auf Download, die Mod wird automatisch installiert');
}
window.modBrowse = modBrowse;
function modsRefresh() {
  if (curPage === 'mods') { $('#modColl').innerHTML = collHtml(); dlList('#modDl'); modList(); rsLoad(); }
  if (curPage === 'minimap') { $('#mmColl').innerHTML = collHtml('minimap'); dlList('#mmDl', 'minimap'); modList(); }
}
setInterval(() => { if (curPage === 'mods') dlList('#modDl'); if (curPage === 'minimap') dlList('#mmDl', 'minimap'); }, 6000);

/* ======================= CURRENT SERVER ======================= */
let CUR = null;
function curHtml(c) {
  const s = c.server || {}, initial = (s.name || '?').replace(/\^\d/g, '').trim()[0] || '?';
  const stat = (k, v) => v == null || v === '' ? '' : `<div><span>${k}</span><b>${esc(v)}</b></div>`;
  const tags = (s.allTags && s.allTags.length ? s.allTags : s.tags || []).slice(0, 10);
  return `<div class="cs-bn" ${s.banner ? `style="background-image:url('${esc(s.banner)}')"` : ''}></div>
    <div class="cs-body">
      <div class="cs-top">
        <div class="cs-icon" ${s.icon ? `style="background-image:url('${esc(s.icon)}')"` : ''}>${s.icon ? '' : esc(initial)}</div>
        <div class="cs-main">
          <div class="row" style="gap:8px">${c.connected ? '<span class="chip g live">Verbunden</span>' : '<span class="chip">Zuletzt gespielt</span>'}${s.locale ? `<span class="chip">${esc(s.locale)}</span>` : ''}${s.online === false ? '<span class="chip r">Offline</span>' : ''}</div>
          <h2 class="cs-name">${fivemName(s.name || c.url)}</h2>
          <p class="cs-desc">${esc(s.desc || '')}</p>
        </div>
      </div>
      <div class="cs-tags">${tags.map(t => `<span class="chip">${esc(t)}</span>`).join('')}</div>
      <div class="cs-stats">${stat('Spieler', s.max ? `${s.players}/${s.max}` : null)}${stat('Ping', s.ping != null ? s.ping + ' ms' : null)}${stat('Build', s.build ? 'b' + s.build : null)}${stat('OneSync', s.onesync ? 'An' : null)}${stat('Ressourcen', s.resources || null)}${stat('Owner', s.owner)}</div>
      <div class="cs-act">
        ${c.fav ? `<button class="btn sm" disabled>${ico('check')}In Favoriten</button>` : `<button class="btn p sm" onclick="curFav()">${ico('plus')}Zu Favoriten</button>`}
        ${s.discord ? `<button class="btn sm dc" onclick="api('open',{target:'${esc(s.discord)}'})"><svg class="i" viewBox="0 0 24 24"><path d="M8 12h.01M16 12h.01M7.5 7.5c3-1.3 6-1.3 9 0M7.5 16.5c3 1.3 6 1.3 9 0M15.5 17l1.5 3c2-.7 3.5-1.7 5-3-.4-4.2-1.4-8-3.5-11-1.4-.7-2.9-1.2-4.5-1.5L13.5 6M8.5 17 7 20c-2-.7-3.5-1.7-5-3 .4-4.2 1.4-8 3.5-11C6.9 5.3 8.4 4.8 10 4.5L10.5 6"/></svg>Discord</button>` : ''}
        ${s.website ? `<button class="btn sm" onclick="api('open',{target:'${esc(s.website)}'})">${ico('globe')}Website</button>` : ''}
        <button class="btn sm" onclick="copyCmd('${esc(s.connect || c.url)}')">${ico('link')}Link kopieren</button>
        ${c.connected ? '' : `<button class="btn sm" onclick="launch('${esc(s.connect || c.url)}')">${ico('play')}Joinen</button>`}
      </div>
    </div>`;
}
async function curServer(force) {
  let c; try { c = await api('server.current', { force: !!force }); } catch { return; }
  const show = c && c.found;
  ['#curSrv', '#curSrv2'].forEach(id => { const el = $(id); if (!el) return; el.classList.toggle('hidden', !show); if (show && JSON.stringify(c) !== JSON.stringify(CUR)) el.innerHTML = curHtml(c); });
  if (show && c.connected && (!CUR || CUR.key !== c.key || !CUR.connected)) feed('Verbunden mit ' + (c.server?.name || c.url).replace(/\^\d/g, ''));
  CUR = show ? c : null;
}
async function curFav() {
  if (!CUR) return;
  try { await api('servers.add', { q: CUR.url }); toast('Zu Favoriten hinzugefügt', (CUR.server?.name || '').replace(/\^\d/g, '')); S.servers = []; CUR = null; curServer(); }
  catch (e) { toast('Nicht hinzugefügt', e.message, false); }
}
window.curFav = curFav;
setInterval(() => { if (curPage === 'home' || curPage === 'servers') curServer(); }, 15000);

/* ======================= CONTROLLER DISPLAY ======================= */
const PAD = { cfg: { on: false, layout: 'auto', body: 'black', color: 'orig', corner: 'br', size: 75, opacity: 90, key: 0, accent: '#FF7A3D' }, r: null, state: null };
const PAD_COLORS = ['#FF3B4E', '#FF7A18', '#F5C451', '#3DDC6B', '#3D8BFF', '#8B5CF6', '#F472B6', '#FFFFFF', '#18181B'];
const accHex = () => getComputedStyle(document.documentElement).getPropertyValue('--a').trim();
function padDraw() { if (PAD.r) PAD.r.draw(PAD.state, { ...PAD.cfg, accent: PAD.cfg.color === 'theme' || PAD.cfg.color === 'orig' ? accHex() : PAD.cfg.color }); }
function padUI() {
  const c = PAD.cfg;
  [['#padLayout', c.layout], ['#padBody', c.body], ['#padOpacity', String(c.opacity)]].forEach(([s, v]) => $$(s + ' button').forEach(b => b.classList.toggle('on', b.dataset.v === v)));
  $$('#padCorner button').forEach(b => b.classList.toggle('on', b.dataset.v === c.corner));
  $('#padColors').innerHTML = `<button class="btn sm ${c.color === 'orig' ? 'p' : ''}" data-pc="orig">Original</button><button class="btn sm ${c.color === 'theme' ? 'p' : ''}" data-pc="theme">Theme</button>` +
    PAD_COLORS.map(x => `<button class="xcol ${c.color.toUpperCase() === x ? 'on' : ''}" data-pc="${x}" style="background:${x}"></button>`).join('') + `<label class="xcustom" title="Eigene Farbe"><input type="color" id="padPick" value="${c.color.startsWith('#') ? c.color : '#FF7A3D'}"></label>`;
  $('#padSize').value = c.size; $('#padSize').style.setProperty('--p', (c.size - 40) / 120 * 100 + '%'); $('#padSizeV').textContent = c.size + '%';
  $('#padKey').innerHTML = KEYS.map(([n, v]) => `<button class="${c.key === v ? 'on' : ''}" data-pk="${v}">${n}</button>`).join('');
  $('#padMain').querySelector('span').textContent = c.on ? 'Overlay ausschalten' : 'Overlay einschalten';
  $('#padMain').classList.toggle('p', !c.on);
  $('#navPad').classList.toggle('hidden', !c.on);
  $('#padBg').style.backgroundImage = `url('${bgUrl()}')`;
  padDraw();
}
let padT = 0, padArtKey = null;
function padPush() {
  clearTimeout(padT);
  padT = setTimeout(async () => {
    const args = { cfg: { ...PAD.cfg, accent: PAD.cfg.color === 'orig' || PAD.cfg.color === 'theme' ? accHex() : PAD.cfg.color } };
    if (padArtKey !== PAD.cfg.body) {   // art only changes with the body colour
      padArtKey = PAD.cfg.body;
      args.art = { xbox: await PadArt.png('xbox', PAD.cfg.body), ps: await PadArt.png('ps', PAD.cfg.body) };
      args.dyn = { xbox: PadArt.dyn('xbox'), ps: PadArt.dyn('ps') };
    }
    const r = await api('pad.set', args).catch(() => null);
    if (r && r.keyOk === false) toast('Taste belegt', 'Andere Taste wählen', false);
  }, 80);
}
function padToggle() { PAD.cfg.on = !PAD.cfg.on; padUI(); padPush(); toast(PAD.cfg.on ? 'Controller-Overlay an' : 'Controller-Overlay aus', PAD.cfg.on ? 'Sichtbar in der gewählten Bildschirmecke' : ''); }
window.padToggle = padToggle;
document.addEventListener('click', e => {
  const s = e.target.closest('#padLayout button,#padBody button,#padOpacity button,#padCorner button');
  if (s) { const k = { padLayout: 'layout', padBody: 'body', padOpacity: 'opacity', padCorner: 'corner' }[s.parentElement.id]; PAD.cfg[k] = k === 'opacity' ? +s.dataset.v : s.dataset.v; padUI(); padPush(); return; }
  const c = e.target.closest('[data-pc]'); if (c) { PAD.cfg.color = c.dataset.pc; padUI(); padPush(); return; }
  const k = e.target.closest('[data-pk]'); if (k) { PAD.cfg.key = +k.dataset.pk; padUI(); padPush(); }
});
document.addEventListener('input', e => {
  if (e.target.id === 'padSize') { PAD.cfg.size = +e.target.value; e.target.style.setProperty('--p', (PAD.cfg.size - 40) / 120 * 100 + '%'); $('#padSizeV').textContent = PAD.cfg.size + '%'; padPush(); }
  if (e.target.id === 'padPick') { PAD.cfg.color = e.target.value.toUpperCase(); padDraw(); padPush(); }
});
document.addEventListener('change', e => { if (e.target.id === 'padPick') padUI(); });
function padState(d) {
  PAD.state = d;
  const n = $('#padName'); if (n) { n.textContent = d && d.c ? d.name : 'Kein Controller'; n.className = 'chip ' + (d && d.c ? 'g' : ''); }
  padDraw();
}
async function initPad(s) {
  Object.assign(PAD.cfg, s.pad || {});
  PAD.r = new PadArt.Pad($('#padPrev'));
  padUI(); padPush();
}

/* ======================= BOOT ======================= */
async function boot() {
  const t0 = performance.now();
  initLook();
  const hideSplash = () => $('#splash').classList.add('out');
  setTimeout(hideSplash, 5000);
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
  moveInd(); enterPage($('#p-home')); if (document.fonts) document.fonts.ready.then(moveInd);
  $('#uname').textContent = d.user;
  $('#verTxt').textContent = 'v' + d.version; $('#verCur').textContent = 'v' + d.version;
  $('#adminTxt').textContent = d.admin ? 'Admin ✓' : 'Kein Admin';
  $('#mCpuName').textContent = d.sys.cpu;
  $('#mGpu').textContent = d.sys.gpu.replace(/^NVIDIA (GeForce )?/i, '').replace(/^AMD /i, '').replace(/^Intel\(R\) /i, '');
  $('#mGpu').title = d.sys.gpu;
  $('#mOs').textContent = `${d.sys.ramGb} GB RAM`;
  if (d.settings.lastBoost) $('#lastBoost').textContent = 'Letzter Boost: ' + new Date(d.settings.lastBoost).toLocaleDateString('de-DE');
  $('#launchSub').textContent = d.fivem.found ? 'mit Pre-Launch-Boost' : 'FiveM nicht gefunden';
  feed(d.fivem.found ? 'FiveM erkannt: ' + d.fivem.path : 'FiveM nicht gefunden: Pfad im Launcher wählen');
  renderSettings(); renderLauncher(); initFx(d.settings); initMinimap(d.settings); initClip(d.settings); if (d.settings.bgVersion) { api('bg.clear').catch(() => { }); S.settings.bgVersion = 0; } applyBg(); initPad(d.settings);
  $('#snapTg').classList.toggle('on', !!d.settings.snapTap);
  await loadTweaks();
  refreshCache(); ping(); setInterval(ping, 8000); curServer();
  api('health').then(h => { S.health = h; $('#hsHealth').textContent = h.score + '%'; }).catch(() => { });
  if (S.settings.autoUpdate !== false) setTimeout(() => checkUpdate(false), 1500);
  setInterval(() => { if (S.settings.autoUpdate !== false) checkUpdate(false); }, 2 * 3600 * 1000);
  setTimeout(hideSplash, Math.max(0, 1300 - (performance.now() - t0)));
  if (S.settings.firstRun) setTimeout(() => wizard(0), 1500);
}

/* ======================= MOCK BACKEND (browser preview) ======================= */
const Mock = {
  settings: { accent: 'inferno', autoInstall: false, restoreBeforeBoost: true, purgeStandby: true, autoPriority: true, autoRamClean: false, autoUpdate: true, firstRun: false, lastBoost: null, xhairAck: false, xhair: { on: false, onlyGame: false, key: 0x79, offsetX: 0, offsetY: 0, json: null }, display: { on: false, onlyGame: true, vibrance: 0, brightness: 0, contrast: 0, gamma: 100, key: 0 } },
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
      case 'xhair.set': return { keyOk: true };
      case 'clip': return { ok: false };
      case 'net.test': await sleep(900); return { ok: true, avg: 23, min: 19, max: 31, jitter: 2.4, loss: 0, samples: Array.from({ length: 20 }, () => 19 + Math.round(Math.random() * 12)) };
      case 'minimap.set': return { ok: true, client: { w: 1920, h: 1080 } };
      case 'minimap.preview': return null;
      case 'clip.cfg': return { keyOk: true, combo: 'Alt+F10' };
      case 'clip.save': case 'clip.open': case 'clip.gamebar': return { ok: true };
      case 'clip.delete': return { ok: true };
      case 'clip.list': return ['Grand Theft Auto V 2026.11.13 - 21.14.08.02', 'FiveM 2026.11.13 - 20.51.44.01', 'Gangwar Clutch', 'Grand Theft Auto V 2026.11.12 - 23.02.10.05'].map((n, i) => ({ name: n, rel: 'Grand Theft Auto V/' + n + '.mp4', folder: i === 2 ? 'Captures' : 'Grand Theft Auto V', size: 48e6 + i * 9e6, date: new Date(Date.now() - i * 864e5).toISOString(), ext: '.mp4' }));
      case 'mods.list': return { found: true, items: [{ name: 'round_minimap_dark.rpf', on: true, size: 3.4e6, cat: 'minimap' }, { name: 'killfx_red_flash.rpf', on: true, size: 1.2e6, cat: 'kill' }, { name: 'nve_lite.rpf', on: false, size: 84e6, cat: 'gfx' }] };
      case 'pad.set': return { keyOk: true };
      case 'pad.watch': return { state: { c: true, t: 'ps', name: 'PlayStation 5 DualSense', b: { a: true, rb: true, up: true }, lx: .5, ly: -.3, rx: 0, ry: .6, lt: .4, rt: 1 } };
      case 'mods.browse': return null;
      case 'mods.install': return { count: 1 };
      case 'server.current': return { found: true, connected: true, key: 'cfx:abc123', url: 'cfx.re/join/abc123', fav: false, server: { name: '^1Nova^7City ^3| Roleplay [GER]', desc: 'Die lebendigste Stadt Deutschlands · Whitelist · Eigene Wirtschaft', allTags: ['roleplay', 'deutsch', 'economy', 'whitelist', 'jobs', 'gangs', 'custom cars'], locale: 'DE', players: 187, max: 256, ping: 22, build: '3095', onesync: true, resources: 412, owner: 'NovaTeam', discord: 'https://discord.gg/novacity', website: 'https://novacity.de', connect: 'cfx.re/join/abc123', online: true } };
      case 'mods.downloads': return [{ name: 'satellite_minimap_v2.zip', size: 18e6, date: new Date().toISOString(), rpfs: 1, cat: 'minimap' }, { name: 'KillFX_RedPulse.rar', size: 2.4e6, date: new Date(Date.now() - 864e5).toISOString(), rpfs: -1, cat: 'kill' }];
      case 'mods.installDl': await sleep(600); return { count: 1 };
      case 'reshade.status': return { found: true, installed: false, presets: [{ id: 'vivid', name: 'Lebendig', desc: 'Kräftige Farben, knackig scharf' }, { id: 'pvp', name: 'Klar (PvP)', desc: 'Maximale Sichtbarkeit, scharfe Kanten' }, { id: 'cine', name: 'Cinematic', desc: 'Filmlook mit Vignette' }, { id: 'night', name: 'Nachtsicht', desc: 'Dunkle Ecken sichtbar' }, { id: 'techni', name: 'Technicolor', desc: 'Satte Retro-Farben' }] };
      case 'reshade.install': for (let i = 1; i <= 5; i++) { onEvent('reshade', { pct: i * 20, text: 'Schritt ' + i }); await sleep(250); } return {};
      case 'reshade.preset': case 'reshade.uninstall': return {};
      case 'thumb': case 'clip.thumb': return { url: null };
      case 'bg.list': return [];
      case 'bg.set': case 'bg.pick': case 'bg.clear': return { v: 0 };
      case 'downloads.open': return null;
      case 'mods.toggle': case 'mods.delete': case 'mods.folder': return {};
      case 'f8.send': await sleep(500); return { sent: true };
      case 'f8.values': return { cl_drawfps: 'true' };
      case 'gw.info': return { found: true, mode: 'windowed', w: 1600, h: 900 };
      case 'gw.borderless': case 'gw.windowed': return { ok: true };
      case 'display.get': return { cfg: M.settings.display, nvidia: true, unlocked: false, gammaOk: null };
      case 'display.set': M.settings.display = a.cfg; return { active: a.cfg.on, gammaOk: true, vibOk: true, keyOk: true };
      case 'display.preview': return { active: false };
      case 'display.unlock': return { ok: true };
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

