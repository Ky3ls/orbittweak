'use strict';
/* 2.31 – Netz-Check: a minute of pings to the router, 1.1.1.1 and the FiveM server shows WHERE a lag starts: in the
   home network or WLAN, at the provider, or at the server and the route there. One dialog: a row per stage with its
   live trace (one scale for all three), its numbers, at the end the verdict, what helps and the text to copy.
   Opened from Server (the header button, a click on a favourite's ping), Diagnose (the row "Verbindung") and Strg+K.
   Host: MainWindow.NetCheck.cs, Core/NetCheck.cs (measuring), Core/NetCheckLogic.cs (numbers, verdict, copy text). */

const NC = {
  st: 'idle',          // idle | prep | live | done | error
  run: 0, gen: 0,      // gen: every start, stop and close makes older answers stale
  sel: '',             // the chosen server key ('' = router and internet only)
  plan: null,          // the start answer: { wifi, link, seconds, targets }
  pts: { gw: [], net: [], srv: [] }, live: {}, at: 0, left: 60, seconds: 60, srvMode: 'icmp',
  res: null, err: '', early: [],
};
const NC_ROWS = [['gw', 'Router'], ['net', 'Internet'], ['srv', 'Server']];
const NC_LV = { ok: ['g', 'Ruhig'], warn: ['w', 'Schwankt'], bad: ['r', 'Problem'], silent: ['r', 'Antwortet nicht'], none: ['', 'Nicht gemessen'] };
const NC_W = 600;   // the traces' own width (viewBox), stretched to the column

/* ---------------------------------------------------------------- numbers in German ---------------------------------------------------------------- */
const ncDec = v => { const s = (Math.round(v * 10) / 10).toFixed(1).replace('.', ','); return s.endsWith(',0') ? s.slice(0, -2) : s; };
/** Like the host: "<1" below 1 ms, one decimal below 10, whole numbers above. */
const ncMs = v => v == null ? '–' : v < 1 ? '<1' : v < 10 ? ncDec(v) : String(Math.round(v));
const ncPct = v => v == null ? '–' : v <= 0 ? '0' : v < 10 ? ncDec(v) : String(Math.round(v));
const ncPlain = s => String(s || '').replace(/\^\d/g, '').replace(/\s+/g, ' ').trim();

/* ---------------------------------------------------------------- which server ---------------------------------------------------------------- */
/** The server FiveM is on (or was last), then the favourites. */
function ncChoices() {
  const out = [], seen = new Set();
  if (typeof CUR !== 'undefined' && CUR && CUR.key) { out.push({ key: CUR.key, name: ncPlain(CUR.server?.name) || CUR.url, sub: CUR.connected ? 'verbunden' : 'zuletzt gespielt' }); seen.add(CUR.key); }
  for (const s of S.servers || []) {
    if (!s.code || seen.has(s.code)) continue;
    seen.add(s.code); out.push({ key: s.code, name: ncPlain(s.name) || s.addr || s.code, sub: s.online ? 'Favorit' : 'Favorit, gerade offline' });
  }
  return out;
}
function ncDefault(list) {
  if (typeof CUR !== 'undefined' && CUR?.key) return CUR.key;
  const on = list.find(c => (S.servers || []).some(s => s.code === c.key && s.online));
  return (on || list[0] || { key: '' }).key;
}

/* ---------------------------------------------------------------- open, start, stop ---------------------------------------------------------------- */
function ncOpen(key) {
  if ((NC.st === 'prep' || NC.st === 'live') && ncShown()) return;   // a test is running in front already
  const list = ncChoices();
  NC.sel = typeof key === 'string' && key ? key : ncDefault(list);
  NC.st = 'idle'; NC.res = null; NC.err = ''; NC.plan = null; ncClear();
  ncDraw();
  // from Diagnose or Strg+K the favourites may not be loaded yet: through the Server page's own loader (like Strg+K),
  // so that page is drawn with them too (it only loads by itself while the list is empty)
  if (!(S.servers || []).length && typeof loadServers === 'function') Promise.resolve(loadServers()).catch(() => { }).then(() => {
    if (!(S.servers || []).length) return;
    if (NC.st === 'idle' && ncShown()) { if (!NC.sel) NC.sel = ncDefault(ncChoices()); ncDraw(); }
  });
}
window.ncOpen = ncOpen;
const ncShown = () => !!$('#nc-box') && $('#modal').classList.contains('show');
function ncClear() { NC.pts = { gw: [], net: [], srv: [] }; NC.live = {}; NC.at = 0; NC.left = NC.seconds = 60; NC.srvMode = 'icmp'; NC.early = []; NC.run = 0; }

async function ncStart() {
  const sel = $('#nc-sel'); if (sel) NC.sel = sel.value;
  const ch = ncChoices().find(c => c.key === NC.sel);
  NC.st = 'prep'; NC.res = null; NC.err = ''; NC.plan = null; ncClear(); ncDraw();
  const gen = ++NC.gen;
  let r;
  try { r = await api('netcheck.start', { server: NC.sel || '', name: ch ? ch.name : '' }); }
  catch (e) { r = { ok: false, error: e.message }; }
  if (gen !== NC.gen) return;   // closed or started again meanwhile (the host was told)
  if (!r || !r.ok) { NC.st = 'error'; NC.err = (r && r.error) || 'Unbekannter Fehler'; ncDraw(); return; }
  NC.run = r.run; NC.plan = r; NC.seconds = r.seconds || 60; NC.left = NC.seconds; NC.st = 'live';
  if (!ncShown()) { ncStopRun(); NC.st = 'idle'; return; }
  ncDraw();
  const early = NC.early; NC.early = [];
  early.forEach(ncEvent);   // results that came before the answer (the host starts measuring at once)
}
/** Tells the host to stop (also a start that is still looking up its server). */
function ncStopRun() {
  NC.gen++;
  if (NC.st === 'live' || NC.st === 'prep') api('netcheck.stop', { run: NC.run || 0 }).catch(() => { });
  NC.run = 0;
}
function ncClose() { ncStopRun(); NC.st = 'idle'; closeModal(); }
function ncCancel() { ncStopRun(); NC.st = 'idle'; ncClear(); ncDraw(); }

function ncEvent(d) {
  if (!d) return;
  if (NC.st === 'prep') { NC.early.push(d); return; }
  if (NC.st !== 'live' || d.run !== NC.run) return;
  if (!ncShown()) { ncStopRun(); NC.st = 'idle'; return; }   // another dialog took the place: stop measuring
  if (d.phase === 'live') {
    for (const id of d.reset || []) NC.pts[id] = [];
    for (const p of d.points || []) (NC.pts[p.t] ||= []).push([p.at, p.ms]);
    NC.live = d.live || {}; NC.at = d.at || 0; NC.left = d.left ?? NC.left;
    if (d.srvMode) NC.srvMode = d.srvMode;
    ncTick();
  } else if (d.phase === 'done') { NC.res = d.result; NC.st = 'done'; NC.run = 0; ncDraw(); }
  else if (d.phase === 'error') { NC.st = 'error'; NC.err = d.error || 'Der Netz-Check wurde unterbrochen.'; NC.run = 0; ncDraw(); }
}
EVENT_EXT.netcheck = ncEvent;

async function ncCopy() {
  const text = NC.res?.report; if (!text) return;
  let ok = false;
  try { ok = !!(await api('clip', { text }))?.ok; } catch { }
  if (!ok && navigator.clipboard) try { await navigator.clipboard.writeText(text); ok = true; } catch { }
  toast(ok ? 'Ergebnis kopiert' : 'Nicht kopiert', ok ? 'Füg es bei deinem Anbieter, bei den Server-Admins oder im Support ein' : 'Die Zwischenablage ist gerade belegt, versuch es gleich noch einmal', ok);
  const b = $('#nc-copy');
  if (ok && b) { b.innerHTML = ico('check') + 'Kopiert'; setTimeout(() => { if ($('#nc-copy') === b) b.innerHTML = ico('copy') + 'Ergebnis kopieren'; }, 2200); }
}

/* ---------------------------------------------------------------- drawing ---------------------------------------------------------------- */
/** The stages as they are known now: from the result, from the start answer, or what will be measured. */
function ncTargets() {
  if (NC.st === 'done' && NC.res) return NC.res.targets;
  if (NC.plan && NC.st === 'live') return NC.plan.targets;
  const ch = ncChoices().find(c => c.key === NC.sel);
  return NC_ROWS.filter(([id]) => id !== 'srv' || NC.sel).map(([id, label]) => ({ id, label, name: id === 'srv' ? (ch ? ch.name : NC.sel.replace(/^(cfx|ip):/, '')) : label, host: id === 'net' ? '1.1.1.1' : '', measured: true, mode: 'icmp', port: 0 }));
}
const ncLink = () => NC.res ? (NC.res.wifi === true ? 'WLAN' : NC.res.wifi === false ? 'Kabel' : '') : NC.plan ? (NC.plan.link || '') : '';
function ncIcon(id) { const wifi = NC.res ? NC.res.wifi : NC.plan?.wifi; return id === 'gw' ? (wifi === false ? 'lan' : 'wifi') : id === 'net' ? 'globe' : 'server'; }

function ncWho(t) {
  const done = NC.st === 'done';
  let sub;
  if (t.id === 'gw') sub = t.measured === false ? (t.note || 'Kein Router gefunden') : t.host || 'Dein Router';
  else if (t.id === 'net') sub = (t.host || '1.1.1.1') + ' (Cloudflare)';
  else sub = t.measured === false ? 'nicht gemessen' : (NC.srvMode === 'tcp' || t.mode === 'tcp') ? `Port ${t.port || 30120}, antwortet nicht auf Ping` : t.host || 'Server';
  let chip = '';
  if (done) {
    // after the row where it starts, a stage with the same trouble is "also affected" (it is in the path), not a second cause
    const order = ['gw', 'net', 'srv'], after = NC.res.start && order.indexOf(t.id) > order.indexOf(NC.res.start);
    const [c, txt] = t.start ? [NC.res.mild ? 'w' : 'r', 'Hier fängt es an']
      : after && /^(warn|bad|silent)$/.test(t.level) ? [NC_LV[t.level][0], 'Auch betroffen']
      // (its own numbers are worse than what holds up to the end of the route: only what holds counts, like in the verdict)
      : /^(warn|bad)$/.test(t.level) && /^(ok|warn)$/.test(t.holds || '') && t.holds !== t.level ? (t.holds === 'ok' ? ['', 'Nur hier unruhig'] : NC_LV.warn)
      // (ignores pings, but the verdict does not put the trouble there: no fault; with no answer at all it is the fault)
      : t.level === 'silent' && (t.id === 'gw' || NC.res.kind !== 'none') ? ['', 'Antwortet nicht']
      : NC_LV[t.level] || NC_LV.none;
    chip = `<span class="chip ${c} nc-chip">${txt}</span>`;
  }
  const name = t.id === 'srv' ? t.name || 'Server' : t.label;
  return `<div class="nc-who" title="${esc(t.id === 'srv' ? `Server ${name}${t.host ? ' · ' + t.host : ''}` : `${t.label}${t.host ? ' · ' + t.host : ''}`)}">
    <span class="nc-key">${ico(ncIcon(t.id))}</span>
    <div class="nc-wt"><b>${esc(name)}</b><small>${done ? chip : esc(sub)}</small></div>
  </div>`;
}
function ncRowHtml(t) {
  const sk = t.note ? 'Nicht gemessen: ' + t.note : 'Nicht gemessen';
  const skip = t.measured === false ? `<span class="nc-skip" title="${esc(sk)}">${esc(sk)}</span>` : '';
  const cls = NC.st === 'done' && t.start ? ` nc-start${NC.res.mild ? ' nc-mild' : ''}` : '';
  return `<div class="nc-row${cls}" data-nc="${t.id}" style="--c:var(--nc-${t.id})">${ncWho(t)}
    <div class="nc-tr"><svg viewBox="0 0 ${NC_W} 100" preserveAspectRatio="none" aria-hidden="true"><path class="nc-base" d="M0 99.5H${NC_W}"/><path class="nc-line" id="nc-l-${t.id}"/><path class="nc-lost" id="nc-x-${t.id}"/></svg>${skip}</div>
    <b class="nc-n" id="nc-m-${t.id}">–</b><b class="nc-n" id="nc-j-${t.id}">–</b><b class="nc-n" id="nc-v-${t.id}">–</b><b class="nc-n" id="nc-s-${t.id}">–</b>
  </div>`;
}

function ncHead() {
  const st = NC.st, r = NC.res, link = ncLink();
  let ic = 'nc-pulse', cls = '', kick = 'Netz-Check', title, sub;
  if (st === 'idle' || st === 'prep') {
    title = 'Wo fängt der Lag an?';
    sub = NC.sel ? 'Eine Minute lang zwei kleine Pings pro Sekunde zu deinem Router, ins Internet und zu deinem Server. Danach siehst du, ob es im WLAN, bei deinem Anbieter oder beim Server hakt.'
      : 'Eine Minute lang zwei kleine Pings pro Sekunde zu deinem Router und ins Internet. Danach siehst du, ob es schon im WLAN oder erst bei deinem Anbieter hakt. Mit einem Server sagt der Test mehr.';
  } else if (st === 'live') {
    kick += link ? ' · ' + link : '';
    title = 'Misst deine Verbindung';
    sub = 'Du kannst dabei weiterspielen: am meisten sagt der Test, während der Lag gerade auftritt.';
  } else if (st === 'error') {
    ic = 'alert'; cls = 'bad'; title = 'Test nicht möglich'; sub = NC.err;
  } else {
    kick += ` · ${Math.round(r.seconds)} Sekunden${link ? ' · ' + link : ''}`;
    title = r.title; sub = r.text;
    [ic, cls] = r.kind === 'ok' ? ['check', 'ok'] : r.kind === 'short' ? ['info', ''] : r.mild ? ['alert', 'warn'] : ['alert', 'bad'];
  }
  return `<div class="nc-head"><div class="nc-ic ${cls}">${ico(ic)}</div><div class="nc-ht"><div class="kicker">${esc(kick)}</div><h1 id="nc-title">${esc(title)}</h1><p>${esc(sub)}</p></div></div>`;
}

function ncRoute() {
  if (NC.st === 'error') return '';
  const tg = ncTargets(), n = NC.st === 'done' || NC.st === 'live';
  return `<div class="nc-route" id="nc-route">
    <div class="nc-th"><span>Strecke</span><span>${n ? 'Verlauf, eine Skala bis <b id="nc-ymax"></b> ms' : 'Verlauf'}</span><span>Ping</span><span>Jitter</span><span>Verlust</span><span>Spitzen</span></div>
    ${tg.map(ncRowHtml).join('')}
    <div class="nc-axis"><span></span><span class="nc-ticks"><i>0 s</i><i>15</i><i>30</i><i>45</i><i>60 s</i></span><span class="nc-lg"><i class="nc-lk"></i>keine Antwort</span></div>
    <div class="nc-cross" id="nc-cross" hidden></div><div class="nc-tip" id="nc-tip" hidden></div>
  </div>`;
}

function ncMore() {
  if (NC.st === 'done') {
    const r = NC.res;
    return `<div class="nc-more">
      <div class="nc-adv"><h3>Was du tun kannst</h3><ul>${r.advice.map(a => `<li>${esc(a)}</li>`).join('')}</ul></div>
      ${r.notes.length ? `<div class="nc-notes">${r.notes.map(n => `<p>${ico('info')}<span>${esc(n)}</span></p>`).join('')}</div>` : ''}
    </div>`;
  }
  if (NC.st === 'live')
    return `<p class="nc-hint">${ico('info')}<span>Ping ist der Median, Jitter der mittlere Unterschied zweier Pings nacheinander, Spitzen sind Ausreißer auf mehr als das Doppelte (und mindestens 30 ms darüber). Ein Strich unten heißt: keine Antwort.</span></p>`;
  if (NC.st === 'idle' || NC.st === 'prep')
    return `<p class="nc-hint">${ico('shield')}<span>Die Pings sind winzig und bremsen nichts. Der Test ändert nichts an deinem PC und berührt FiveM nicht. Ins Netz gehen nur die Pings, bei einem Join-Code eine Abfrage der Server-Adresse bei Cfx.re und, falls dein Server Pings ignoriert, ein kurzer Verbindungsaufbau pro Sekunde zu seinem Port.</span></p>`;
  return '';
}

function ncAct() {
  const st = NC.st;
  if (st === 'idle' || st === 'prep') {
    const list = ncChoices(), has = list.some(c => c.key === NC.sel);
    const opts = list.map(c => `<option value="${esc(c.key)}"${c.key === NC.sel ? ' selected' : ''}>${esc(c.name)} (${esc(c.sub)})</option>`).join('')
      + (NC.sel && !has ? `<option value="${esc(NC.sel)}" selected>${esc(NC.sel.replace(/^(cfx|ip):/, ''))}</option>` : '')
      + `<option value=""${NC.sel ? '' : ' selected'}>Ohne Server, nur Router und Internet</option>`;
    return `<label class="nc-pick"><span>Server</span><select class="in nc-sel" id="nc-sel"${st === 'prep' ? ' disabled' : ''}>${opts}</select></label>
      <button class="btn lg" type="button" id="nc-close">${st === 'prep' ? 'Abbrechen' : 'Schließen'}</button>
      <button class="btn p lg" type="button" id="nc-go"${st === 'prep' ? ' disabled' : ''}>${st === 'prep' ? ico('refresh', 'i spin') + 'Wird vorbereitet…' : ico('nc-pulse') + 'Test starten'}</button>`;
  }
  if (st === 'live')
    return `<div class="nc-cd"><span id="nc-left">Noch ${NC.left} s</span><i class="nc-bar"><i id="nc-prog"></i></i></div>
      <button class="btn lg" type="button" id="nc-stop">Abbrechen</button>`;
  if (st === 'error')
    return `<button class="btn lg" type="button" id="nc-close">Schließen</button><button class="btn p lg" type="button" id="nc-again">${ico('refresh')}Nochmal versuchen</button>`;
  return `<button class="btn lg" type="button" id="nc-close">Schließen</button><button class="btn lg" type="button" id="nc-again">${ico('refresh')}Nochmal testen</button>
    <button class="btn p lg" type="button" id="nc-copy">${ico('copy')}Ergebnis kopieren</button>`;
}

/** Draws the whole dialog for the current state (state changes only; the live values go through ncTick). */
function ncDraw() {
  const html = `<div class="nc-box nc-${NC.st}" id="nc-box">${ncHead()}${ncRoute()}${ncMore()}</div><div class="act nc-act">${ncAct()}</div>`;
  const shown = ncShown(), inside = shown && $('#mbox').contains(document.activeElement);
  if (shown) $('#mbox').innerHTML = html;
  else { $('#mbox').style.width = ''; modal(html); }   // (a dialog closed a moment ago may still have its own width set)
  ncTick();
  // keyboard: the redraw replaced the focused button, so the state's main action takes the focus (also when it opens)
  if (!shown || inside) ['#nc-go:not([disabled])', '#nc-stop', '#nc-copy', '#nc-again', '#nc-close'].map(q => $(q)).find(Boolean)?.focus({ preventScroll: true });
}

/* ---------------------------------------------------------------- the traces ---------------------------------------------------------------- */
/** One scale for all three rows, so the rows compare at a glance (rounded up to a clean step, at most 500 ms). */
function ncScale(lists) {
  let m = 0;
  for (const l of lists) for (const p of l) if (p[1] != null && p[1] > m) m = p[1];
  return [20, 50, 100, 150, 200, 300, 500].find(s => s >= m * 1.06) || 500;
}
/** [line, lost marks] as SVG paths; a lost ping breaks the line and gets a short mark at the foot. */
function ncPaths(list, ymax, secs) {
  let d = '', x = '', pen = false;
  for (const [at, v] of list.slice().sort((a, b) => a[0] - b[0])) {
    const px = (Math.min(at, secs) / secs * NC_W).toFixed(1);
    if (v == null) { x += `M${px} 100V82`; pen = false; continue; }
    const py = (97 - Math.min(v, ymax) / ymax * 91).toFixed(1);
    d += pen ? `L${px} ${py}` : `M${px} ${py}h.01`;
    pen = true;
  }
  return [d, x];
}
function ncSeries() {
  if (NC.st === 'done' && NC.res) { const o = {}; for (const t of NC.res.targets) o[t.id] = t.trace || []; return o; }
  return NC.pts;
}
function ncStats(id) {
  if (NC.st === 'done' && NC.res) return NC.res.targets.find(t => t.id === id)?.stats || null;
  return NC.live[id] || null;
}
function ncTick() {
  if (!$('#nc-box')) return;
  const ser = ncSeries(), secs = NC.seconds || 60;
  const ids = NC_ROWS.map(r => r[0]).filter(id => $('#nc-l-' + id));
  if (NC.st === 'live' || NC.st === 'done') {
    const ymax = ncScale(ids.map(id => ser[id] || []));
    const yb = $('#nc-ymax'); if (yb) yb.textContent = ymax;
    for (const id of ids) {
      const [d, x] = ncPaths(ser[id] || [], ymax, secs);
      $('#nc-l-' + id).setAttribute('d', d); $('#nc-x-' + id).setAttribute('d', x);
      const s = ncStats(id), any = s && s.answered > 0;
      const set = (k, v, u) => { const el = $(`#nc-${k}-${id}`); if (el) el.innerHTML = v === '–' ? '–' : `${v}<small>${u}</small>`; };
      set('m', any ? ncMs(s.median) : '–', ' ms'); set('j', any && s.jitter != null ? ncMs(s.jitter) : '–', ' ms');
      set('v', s && s.sent ? ncPct(s.loss) : '–', ' %'); set('s', any ? String(s.spikes) : '–', '');
    }
    // the server switched to its port while measuring: say so in its row
    if (NC.st === 'live' && NC.srvMode === 'tcp') { const sm = $('[data-nc="srv"] .nc-wt small'); if (sm && !sm.dataset.tcp) { sm.dataset.tcp = 1; sm.textContent = 'antwortet nicht auf Ping, misst Port ' + (NC.plan?.targets.find(t => t.id === 'srv')?.port || 30120); } }
  }
  if (NC.st === 'live') {
    const l = $('#nc-left'); if (l) l.textContent = NC.left > 0 ? `Noch ${NC.left} s` : 'Wertet aus…';
    const p = $('#nc-prog'); if (p) p.style.transform = `scaleX(${Math.min(1, NC.at / secs)})`;
  }
}

/* ---------------------------------------------------------------- hover: the values at one moment ---------------------------------------------------------------- */
function ncHover(e) {
  const route = $('#nc-route'), cross = $('#nc-cross'), tip = $('#nc-tip');
  if (!route || !cross || !tip || (NC.st !== 'live' && NC.st !== 'done')) return;
  const tr = route.querySelector('.nc-tr'); if (!tr) return;
  const rr = route.getBoundingClientRect(), r = tr.getBoundingClientRect();
  const f = (e.clientX - r.left) / r.width;
  if (f < 0 || f > 1 || e.clientY < rr.top || e.clientY > rr.bottom) { cross.hidden = tip.hidden = true; return; }
  const secs = NC.seconds || 60, t = f * secs, ser = ncSeries();
  const rows = ncTargets().filter(x => x.measured !== false).map(x => {
    let best = null;
    for (const p of ser[x.id] || []) if (!best || Math.abs(p[0] - t) < Math.abs(best[0] - t)) best = p;
    const ok = best && Math.abs(best[0] - t) <= (x.mode === 'tcp' || (x.id === 'srv' && NC.srvMode === 'tcp') ? .6 : .3);
    const v = !ok ? 'kein Wert' : best[1] == null ? 'keine Antwort' : ncMs(best[1]) + ' ms';
    return `<span><i style="background:var(--nc-${x.id})"></i>${esc(x.id === 'srv' ? 'Server' : x.label)}<b>${v}</b></span>`;
  });
  if (!rows.length) return;
  const x = e.clientX - rr.left;
  cross.style.left = x + 'px'; cross.hidden = false;
  tip.innerHTML = `<em>${ncDec(Math.round(t * 2) / 2)} s</em>${rows.join('')}`; tip.hidden = false;
  const w = tip.offsetWidth;
  tip.style.left = (x + 14 + w > rr.width ? x - 14 - w : x + 14) + 'px';
}
document.addEventListener('pointermove', e => {
  if (NC.st !== 'live' && NC.st !== 'done') return;
  if (e.target.closest && e.target.closest('#nc-route')) ncHover(e);
  else { const c = $('#nc-cross'); if (c && !c.hidden) { c.hidden = true; $('#nc-tip').hidden = true; } }
});

/* ---------------------------------------------------------------- the dialog's buttons and keys ---------------------------------------------------------------- */
document.addEventListener('click', e => {
  const b = e.target.closest && e.target.closest('#nc-go, #nc-close, #nc-stop, #nc-again, #nc-copy, [data-ncgo]');
  if (!b) return;
  if (b.matches('[data-ncgo]')) return ncOpen();
  ({ 'nc-go': ncStart, 'nc-close': ncClose, 'nc-stop': ncCancel, 'nc-again': ncStart, 'nc-copy': ncCopy })[b.id]?.();
});
document.addEventListener('change', e => {
  if (e.target.id !== 'nc-sel') return;
  NC.sel = e.target.value;
  const row = $('#nc-route'); if (row) { row.outerHTML = ncRoute(); ncTick(); }
  const head = $('#nc-box .nc-head'); if (head) head.outerHTML = ncHead();
});
document.addEventListener('keydown', e => { if (e.key === 'Escape' && ncShown() && !e.defaultPrevented) ncClose(); });

/* ---------------------------------------------------------------- the ways in ---------------------------------------------------------------- */
// Diagnose: the row "Verbindung" (cable or WLAN) gets a button for the full test
{ const draw = renderHealth; renderHealth = function () { const r = draw.apply(this, arguments); ncDiag(); return r; }; }
function ncDiag() {
  for (const row of $$('#hList .hc')) {
    if (row.querySelector('.tx b')?.textContent !== 'Verbindung' || row.querySelector('[data-ncgo]')) continue;
    const html = `<button class="btn sm o nc-hgo" type="button" data-ncgo title="Netz-Check: zeigt in einer Minute, ob ein Lag im WLAN, beim Anbieter oder beim Server anfängt">${ico('nc-pulse')}Testen</button>`;
    const at = row.querySelector(':scope > .btn, :scope > .hc-ok');
    if (at) at.insertAdjacentHTML('beforebegin', html); else row.insertAdjacentHTML('beforeend', html);
  }
}
// Server: a favourite's ping opens the test for that server
document.addEventListener('click', e => {
  const pg = e.target.closest && e.target.closest('#srvList .srv:not(.skel) .pg'); if (!pg) return;
  const rows = [...$('#srvList').children].filter(r => r.classList.contains('srv') && !r.classList.contains('skel'));
  const s = (S.servers || [])[rows.indexOf(pg.closest('.srv'))];
  if (s) ncOpen(s.code);
});
document.addEventListener('mouseover', e => {
  const pg = e.target.closest && e.target.closest('#srvList .srv:not(.skel) .pg');
  if (pg && !pg.dataset.nc) { pg.dataset.nc = 1; pg.title = 'Ping · klicken für den Netz-Check: zeigt in einer Minute, wo ein Lag anfängt'; }
});

/* ---------------------------------------------------------------- browser preview ---------------------------------------------------------------- */
/* The mock plays a real-shaped minute (?nc=wifi|ok|provider|server|hidden|error, ?ncspeed=10 for a faster minute; the
   screenshot rig sets NC_MOCK directly). The verdicts below are what Core/NetCheckLogic.cs says about exactly these
   samples (made once with the real logic), the live numbers are worked out here like the host does. */
const NC_MOCK = (() => {
  const q = location.search;
  return { scenario: (q.match(/[?&]nc=(\w+)/) || [])[1] || 'wifi', speed: +((q.match(/[?&]ncspeed=(\d+)/) || [])[1] || 1) };
})();
window.NC_MOCK = NC_MOCK;
(() => {
  if (HOST) return;
  let timer = 0, runs = 0;
/*NC-MOCKGEN*/
  /** The mock's measurements: the same numbers on every run (a seeded generator), shaped like real cases. */
  function ncMockGen(sc) {
    let s = { wifi: 11, ok: 23, provider: 37, server: 41, hidden: 53 }[sc] || 11;
    const rnd = () => { s = s + 0x6D2B79F5 | 0; let t = Math.imul(s ^ s >>> 15, 1 | s); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; };
    const wob = a => (rnd() * 2 - 1) * a, ms = v => Math.max(0, Math.round(v)), out = { gw: [], net: [], srv: [] };
    if (sc === 'server') {
      // calm up to the internet; the server ignores pings (measured by its port from 3 s on) and drops packets
      for (let i = 0; i < 120; i++) { out.gw.push([i / 2, ms(.8 + wob(.6))]); out.net.push([i / 2, ms(10 + wob(1.4))]); }
      const lost = [10, 11, 25, 40, 41], spikes = [15, 33, 47];
      for (let k = 0; k < 57; k++) out.srv.push([3 + k, lost.includes(k) ? null : Math.round((spikes.includes(k) ? 160 + rnd() * 60 : 36 + wob(3)) * 10) / 10]);
      return out;
    }
    for (let i = 0; i < 120; i++) {
      const at = i / 2;
      if (sc === 'ok') {
        out.gw.push([at, ms(.7 + wob(.6))]); out.net.push([at, ms(9 + wob(1.3))]); out.srv.push([at, ms(27 + wob(1.8))]);
      } else if (sc === 'hidden') {
        // the server keeps its address secret: router and internet only (calm, over WLAN)
        out.gw.push([at, ms(2.5 + wob(1.2))]); out.net.push([at, ms(12 + wob(1.6))]);
      } else if (sc === 'provider') {
        // the line fills up twice (a big download in the house): from the internet on the ping climbs, a few packets drop
        const bloat = i >= 24 && i <= 40 ? 55 + rnd() * 60 : i >= 70 && i <= 84 ? 45 + rnd() * 50 : 0, gone = [30, 33, 78, 79, 80].includes(i);
        out.gw.push([at, ms(1 + wob(.7))]);
        out.net.push([at, gone ? null : ms(13 + bloat + wob(1.5))]);
        out.srv.push([at, gone ? null : ms(32 + bloat + wob(2))]);
      } else {
        // WLAN: background scans every ten seconds or so, felt on every stage, and a few packets lost over the air
        const burst = [8, 9, 30, 31, 52, 70, 71, 92, 110, 111].includes(i), gone = [20, 61, 100, 101].includes(i);
        const air = burst ? 150 + rnd() * 80 : 2.6 + wob(1.6) + (rnd() < .08 ? rnd() * 12 : 0);
        out.gw.push([at, gone ? null : ms(air)]);
        out.net.push([at, gone ? null : ms(air + 11 + wob(1.2))]);
        out.srv.push([at, gone ? null : ms(air + 30 + wob(2))]);
      }
    }
    return out;
  }
/*/NC-MOCKGEN*/
  /** The host's numbers for the samples so far (Core/NetCheckLogic.cs Stats, for the live rows). */
  function stats(list, iv) {
    const rtt = list.slice().sort((a, b) => a[0] - b[0]).map(p => p[1]).filter(v => v != null), sent = list.length, ans = rtt.length;
    const out = { sent, answered: ans, loss: sent ? Math.round((sent - ans) * 1000 / sent) / 10 : 0, median: null, p95: null, jitter: null, spikes: 0, high: 0, max: null, seconds: sent * iv / 1000 };
    if (!ans) return out;
    const so = rtt.slice().sort((a, b) => a - b), n = so.length, med = n % 2 ? so[n >> 1] : (so[n / 2 - 1] + so[n / 2]) / 2, lim = med + Math.max(30, med);
    let jit = 0; for (let i = 1; i < n; i++) jit += Math.abs(rtt[i] - rtt[i - 1]);
    let sp = 0, inS = false; for (const v of rtt) { const hi = v > lim; if (hi && !inS) sp++; inS = hi; }
    return { ...out, median: Math.round(med * 10) / 10, p95: so[Math.min(n - 1, Math.max(0, Math.ceil(.95 * n) - 1))], jitter: n > 1 ? Math.round(jit / (n - 1) * 10) / 10 : null, spikes: sp, high: rtt.filter(v => v > lim).length, max: so[n - 1] };
  }
  MOCK_EXT['netcheck.start'] = async a => {
    clearInterval(timer);
    const sc = NC_MOCK.scenario;
    await sleep(sc === 'error' ? 400 : 650);
    if (sc === 'error') return { ok: false, error: 'Keine Netzwerkverbindung gefunden. Prüf, ob dein PC per WLAN oder Kabel verbunden ist.' };
    const key = NC_CANNED[sc] ? sc : 'wifi', run = ++runs, wifi = key === 'wifi' || key === 'hidden', srv = !!a.server, tcp = key === 'server', hidden = key === 'hidden';
    const name = ncPlain(a.name) || String(a.server || '').replace(/^(cfx|ip):/, '');
    const targets = [
      { id: 'gw', label: 'Router', name: 'Router', host: '192.168.178.1', mode: 'icmp', port: 0, measured: true, note: '' },
      { id: 'net', label: 'Internet', name: 'Internet', host: '1.1.1.1', mode: 'icmp', port: 0, measured: true, note: '' },
    ];
    if (srv) targets.push({ id: 'srv', label: 'Server', name, host: hidden ? '' : '203.0.113.42', mode: 'icmp', port: hidden ? 443 : 30120, measured: !hidden, note: hidden ? 'Adresse geheim' : '' });
    const ids = srv && !hidden ? ['gw', 'net', 'srv'] : ['gw', 'net'];
    const data = ncMockGen(key), sent = { gw: 0, net: 0, srv: 0 };
    if (tcp) data.srv = [...Array(6)].map((_, i) => [i / 2, null]).concat(data.srv);   // six silent pings, then the port
    let t = 0, switched = false;
    timer = setInterval(() => {
      t += .5;
      const points = [], reset = [];
      for (const id of ids) {
        const l = data[id];
        while (sent[id] < l.length && l[sent[id]][0] + (l[sent[id]][1] == null ? 1 : l[sent[id]][1] / 1000) <= t) { const [at, ms] = l[sent[id]++]; points.push({ t: id, at, ms }); }
      }
      if (tcp && srv && !switched && t >= 3) { switched = true; reset.push('srv'); data.srv = data.srv.slice(6); sent.srv = 0; for (let i = points.length - 1; i >= 0; i--) if (points[i].t === 'srv') points.splice(i, 1); }
      const live = {};
      const shown = id => data[id].slice(0, sent[id]);
      for (const id of ids) live[id] = stats(shown(id), id === 'srv' && switched ? 1000 : 500);
      const at = Math.min(60, t);
      onEvent('netcheck', { run, phase: 'live', at, left: Math.max(0, Math.ceil(60 - at)), points, live, srvMode: srv ? (switched ? 'tcp' : 'icmp') : '', reset });
      if (t >= 61) {
        clearInterval(timer);
        const r = JSON.parse(JSON.stringify(NC_CANNED[key]));
        const stamp = new Date().toLocaleString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
        r.report = r.report.replace('22.11.2026, 14:05', stamp).replace(/NovaCity \| Roleplay \[GER\]/g, name || 'Server');
        for (const tg of r.targets) { tg.trace = data[tg.id] || []; if (tg.id === 'srv') tg.name = name; }
        if (!srv) {   // measured without a server: roughly what the host says then
          r.targets = r.targets.filter(x => x.id !== 'srv');
          r.text = r.text.replace('bis zum Server', 'bis ins Internet').replace(' zum Server', ' ins Internet');
          r.notes = r.notes.filter(n => !/Port|Server antwortet|geheim/.test(n)).concat('Ohne Server gemessen: der Test zeigt nur Router und Internet.');
          if (r.kind === 'server') Object.assign(r, { kind: 'ok', start: '', title: 'Deine Verbindung war ruhig', text: 'In diesen 60 Sekunden gab es bis ins Internet keinen nennenswerten Verlust und kaum Schwankungen.', advice: ['Ruckelt FiveM trotzdem', 'Am meisten sagt der Test, wenn du ihn genau dann startest, wenn der Lag auftritt.'] });
          r.advice = r.advice.map(x => x.startsWith('Ruckelt FiveM trotzdem') ? 'Ruckelt FiveM trotzdem, kann es am Weg zum Server, am Server selbst oder an FPS-Einbrüchen auf deinem PC liegen. Das zeigt dieser Test ohne Server nicht.' : x);
        }
        onEvent('netcheck', { run, phase: 'done', result: r });
      }
    }, 500 / Math.max(1, NC_MOCK.speed));
    return { ok: true, run, seconds: 60, wifi, link: wifi ? 'WLAN' : 'Kabel', targets };
  };
  MOCK_EXT['netcheck.stop'] = async () => { clearInterval(timer); return { ok: true }; };
  const NC_CANNED = /*NC-CANNED*/{"wifi":{"kind":"home","mild":false,"start":"gw","title":"Das Problem fängt in deinem WLAN an","text":"Schon zwischen deinem PC und deinem Router gab es 3,3 % Verlust und 6 Ping-Spitzen bis 224 ms, und das zieht sich bis zum Server durch.","advice":["Ein LAN-Kabel ist der größte Hebel: damit fällt dieser Teil der Strecke praktisch weg.","Geht kein Kabel: verbinde dich mit dem 5-GHz-Netz deines Routers, falls er eins hat, und rück näher an den Router. Jede Wand dazwischen kostet.","Laden andere Geräte im WLAN gerade viel (Streams, Updates), teilen sie sich die Funkzeit mit dir."],"notes":[],"wifi":true,"seconds":60,"targets":[{"id":"gw","label":"Router","name":"Router","host":"192.168.178.1","mode":"icmp","port":0,"measured":true,"level":"bad","why":"3,3 % Verlust und 6 Ping-Spitzen bis 224 ms","start":true,"holds":"bad","stats":{"sent":120,"answered":116,"loss":3.3,"median":3,"p95":192,"jitter":22.1,"spikes":6,"high":10,"max":224,"seconds":60}},{"id":"net","label":"Internet","name":"Internet","host":"1.1.1.1","mode":"icmp","port":0,"measured":true,"level":"bad","why":"3,3 % Verlust und 6 Ping-Spitzen bis 236 ms","start":false,"holds":"bad","stats":{"sent":120,"answered":116,"loss":3.3,"median":14,"p95":203,"jitter":22.3,"spikes":6,"high":10,"max":236,"seconds":60}},{"id":"srv","label":"Server","name":"NovaCity | Roleplay [GER]","host":"203.0.113.42","mode":"icmp","port":30120,"measured":true,"level":"bad","why":"3,3 % Verlust und 6 Ping-Spitzen bis 252 ms","start":false,"holds":"bad","stats":{"sent":120,"answered":116,"loss":3.3,"median":33,"p95":220,"jitter":22.6,"spikes":6,"high":10,"max":252,"seconds":60}}],"report":"OrbitTweak Netz-Check · 22.11.2026, 14:05 Uhr · 60 Sekunden · Verbindung: WLAN\nErgebnis: Das Problem fängt in deinem WLAN an\nSchon zwischen deinem PC und deinem Router gab es 3,3 % Verlust und 6 Ping-Spitzen bis 224 ms, und das zieht sich bis zum Server durch.\n\nRouter (192.168.178.1): Ping 3 ms (Median) · 95 % bis 192 ms · Jitter 22 ms · Verlust 3,3 % (4 von 120) · 6 Spitzen (höchster Wert 224 ms) · 5 Sekunden stark erhöht ← hier fängt es an\nInternet (1.1.1.1): Ping 14 ms (Median) · 95 % bis 203 ms · Jitter 22 ms · Verlust 3,3 % (4 von 120) · 6 Spitzen (höchster Wert 236 ms) · 5 Sekunden stark erhöht\nServer NovaCity | Roleplay [GER] (cfx.re/join/abc123 · 203.0.113.42): Ping 33 ms (Median) · 95 % bis 220 ms · Jitter 23 ms · Verlust 3,3 % (4 von 120) · 6 Spitzen (höchster Wert 252 ms) · 5 Sekunden stark erhöht\n\nGemessen: zwei Pings pro Sekunde je Strecke (Server ohne Ping-Antwort: ein Verbindungsaufbau pro Sekunde), Zeitlimit 1000 ms. Jitter = mittlerer Unterschied aufeinanderfolgender Pings, Spitze = mehr als doppelter Median und mindestens 30 ms darüber."},"ok":{"kind":"ok","mild":false,"start":"","title":"Deine Verbindung war ruhig","text":"In diesen 60 Sekunden gab es bis zum Server keinen nennenswerten Verlust und kaum Schwankungen.","advice":["Ruckelt FiveM trotzdem, liegt es eher am Server selbst (zum Beispiel an seiner Leistung) oder an FPS-Einbrüchen auf deinem PC. Das zeigt dieser Test nicht.","Am meisten sagt der Test, wenn du ihn genau dann startest, wenn der Lag auftritt."],"notes":[],"wifi":false,"seconds":60,"targets":[{"id":"gw","label":"Router","name":"Router","host":"192.168.178.1","mode":"icmp","port":0,"measured":true,"level":"ok","why":"","start":false,"holds":"ok","stats":{"sent":120,"answered":120,"loss":0,"median":1,"p95":1,"jitter":0.4,"spikes":0,"high":0,"max":1,"seconds":60}},{"id":"net","label":"Internet","name":"Internet","host":"1.1.1.1","mode":"icmp","port":0,"measured":true,"level":"ok","why":"","start":false,"holds":"ok","stats":{"sent":120,"answered":120,"loss":0,"median":9,"p95":10,"jitter":0.8,"spikes":0,"high":0,"max":10,"seconds":60}},{"id":"srv","label":"Server","name":"NovaCity | Roleplay [GER]","host":"203.0.113.42","mode":"icmp","port":30120,"measured":true,"level":"ok","why":"","start":false,"holds":"ok","stats":{"sent":120,"answered":120,"loss":0,"median":27,"p95":29,"jitter":1.4,"spikes":0,"high":0,"max":29,"seconds":60}}],"report":"OrbitTweak Netz-Check · 22.11.2026, 14:05 Uhr · 60 Sekunden · Verbindung: Kabel\nErgebnis: Deine Verbindung war ruhig\nIn diesen 60 Sekunden gab es bis zum Server keinen nennenswerten Verlust und kaum Schwankungen.\n\nRouter (192.168.178.1): Ping 1 ms (Median) · 95 % bis 1 ms · Jitter <1 ms · Verlust 0 % (0 von 120) · 0 Spitzen (höchster Wert 1 ms)\nInternet (1.1.1.1): Ping 9 ms (Median) · 95 % bis 10 ms · Jitter <1 ms · Verlust 0 % (0 von 120) · 0 Spitzen (höchster Wert 10 ms)\nServer NovaCity | Roleplay [GER] (cfx.re/join/abc123 · 203.0.113.42): Ping 27 ms (Median) · 95 % bis 29 ms · Jitter 1,4 ms · Verlust 0 % (0 von 120) · 0 Spitzen (höchster Wert 29 ms)\n\nGemessen: zwei Pings pro Sekunde je Strecke (Server ohne Ping-Antwort: ein Verbindungsaufbau pro Sekunde), Zeitlimit 1000 ms. Jitter = mittlerer Unterschied aufeinanderfolgender Pings, Spitze = mehr als doppelter Median und mindestens 30 ms darüber."},"provider":{"kind":"provider","mild":false,"start":"net","title":"Das Problem fängt hinter deinem Router an","text":"Bis zu deinem Router ist alles ruhig, erst dahinter gab es 4,2 % Verlust und 14 Sekunden lang stark erhöhten Ping bis 126 ms: das ist die Strecke deines Internetanbieters.","advice":["Läuft gerade ein großer Download, Upload oder Stream, auch auf einem anderen Gerät bei dir? Der füllt die Leitung, und der Ping steigt.","Teste zu einer anderen Uhrzeit noch einmal: abends ist die Leitung bei vielen Anbietern voller.","Bleibt es so, schick deinem Anbieter dieses Ergebnis (Ergebnis kopieren)."],"notes":[],"wifi":false,"seconds":60,"targets":[{"id":"gw","label":"Router","name":"Router","host":"192.168.178.1","mode":"icmp","port":0,"measured":true,"level":"ok","why":"","start":false,"holds":"ok","stats":{"sent":120,"answered":120,"loss":0,"median":1,"p95":2,"jitter":0.5,"spikes":0,"high":0,"max":2,"seconds":60}},{"id":"net","label":"Internet","name":"Internet","host":"1.1.1.1","mode":"icmp","port":0,"measured":true,"level":"bad","why":"4,2 % Verlust und 14 Sekunden lang stark erhöhten Ping bis 126 ms","start":true,"holds":"bad","stats":{"sent":120,"answered":115,"loss":4.2,"median":14,"p95":92,"jitter":6.9,"spikes":2,"high":27,"max":126,"seconds":60}},{"id":"srv","label":"Server","name":"NovaCity | Roleplay [GER]","host":"203.0.113.42","mode":"icmp","port":30120,"measured":true,"level":"bad","why":"4,2 % Verlust und 14 Sekunden lang stark erhöhten Ping bis 147 ms","start":false,"holds":"bad","stats":{"sent":120,"answered":115,"loss":4.2,"median":33,"p95":111,"jitter":7.5,"spikes":2,"high":27,"max":147,"seconds":60}}],"report":"OrbitTweak Netz-Check · 22.11.2026, 14:05 Uhr · 60 Sekunden · Verbindung: Kabel\nErgebnis: Das Problem fängt hinter deinem Router an\nBis zu deinem Router ist alles ruhig, erst dahinter gab es 4,2 % Verlust und 14 Sekunden lang stark erhöhten Ping bis 126 ms: das ist die Strecke deines Internetanbieters.\n\nRouter (192.168.178.1): Ping 1 ms (Median) · 95 % bis 2 ms · Jitter <1 ms · Verlust 0 % (0 von 120) · 0 Spitzen (höchster Wert 2 ms)\nInternet (1.1.1.1): Ping 14 ms (Median) · 95 % bis 92 ms · Jitter 6,9 ms · Verlust 4,2 % (5 von 120) · 2 Spitzen (höchster Wert 126 ms) · 14 Sekunden stark erhöht ← hier fängt es an\nServer NovaCity | Roleplay [GER] (cfx.re/join/abc123 · 203.0.113.42): Ping 33 ms (Median) · 95 % bis 111 ms · Jitter 7,5 ms · Verlust 4,2 % (5 von 120) · 2 Spitzen (höchster Wert 147 ms) · 14 Sekunden stark erhöht\n\nGemessen: zwei Pings pro Sekunde je Strecke (Server ohne Ping-Antwort: ein Verbindungsaufbau pro Sekunde), Zeitlimit 1000 ms. Jitter = mittlerer Unterschied aufeinanderfolgender Pings, Spitze = mehr als doppelter Median und mindestens 30 ms darüber."},"server":{"kind":"server","mild":false,"start":"srv","title":"Das Problem liegt beim Server oder auf dem Weg dorthin","text":"Bis ins Internet ist alles ruhig, erst auf dem Weg zum Server gab es 8,8 % Verlust und einen Jitter von 21 ms.","advice":["Auf deinem PC kannst du daran nichts ändern, das kann auch kein Tool.","Sag den Server-Admins Bescheid und schick ihnen dieses Ergebnis (Ergebnis kopieren).","Laggen andere Spieler auch, liegt es am Server. Hast nur du den Lag, liegt es eher an der Strecke deines Anbieters zum Server."],"notes":["Dein Server antwortet nicht auf Ping. Gemessen wurde deshalb der Verbindungsaufbau zu Port 30120, einmal pro Sekunde."],"wifi":false,"seconds":60,"targets":[{"id":"gw","label":"Router","name":"Router","host":"192.168.178.1","mode":"icmp","port":0,"measured":true,"level":"ok","why":"","start":false,"holds":"ok","stats":{"sent":120,"answered":120,"loss":0,"median":1,"p95":1,"jitter":0.4,"spikes":0,"high":0,"max":1,"seconds":60}},{"id":"net","label":"Internet","name":"Internet","host":"1.1.1.1","mode":"icmp","port":0,"measured":true,"level":"ok","why":"","start":false,"holds":"ok","stats":{"sent":120,"answered":120,"loss":0,"median":10,"p95":11,"jitter":0.9,"spikes":0,"high":0,"max":11,"seconds":60}},{"id":"srv","label":"Server","name":"NovaCity | Roleplay [GER]","host":"203.0.113.42","mode":"tcp","port":30120,"measured":true,"level":"bad","why":"8,8 % Verlust und einen Jitter von 21 ms","start":true,"holds":"bad","stats":{"sent":57,"answered":52,"loss":8.8,"median":36,"p95":165.9,"jitter":20.8,"spikes":3,"high":3,"max":215.6,"seconds":57}}],"report":"OrbitTweak Netz-Check · 22.11.2026, 14:05 Uhr · 60 Sekunden · Verbindung: Kabel\nErgebnis: Das Problem liegt beim Server oder auf dem Weg dorthin\nBis ins Internet ist alles ruhig, erst auf dem Weg zum Server gab es 8,8 % Verlust und einen Jitter von 21 ms.\n\nRouter (192.168.178.1): Ping 1 ms (Median) · 95 % bis 1 ms · Jitter <1 ms · Verlust 0 % (0 von 120) · 0 Spitzen (höchster Wert 1 ms)\nInternet (1.1.1.1): Ping 10 ms (Median) · 95 % bis 11 ms · Jitter <1 ms · Verlust 0 % (0 von 120) · 0 Spitzen (höchster Wert 11 ms)\nServer NovaCity | Roleplay [GER] (cfx.re/join/abc123 · 203.0.113.42): Ping 36 ms (Median) · 95 % bis 166 ms · Jitter 21 ms · Verlust 8,8 % (5 von 57) · 3 Spitzen (höchster Wert 216 ms) · 3 Sekunden stark erhöht · gemessen über Port 30120 ← hier fängt es an\n\nHinweis: Dein Server antwortet nicht auf Ping. Gemessen wurde deshalb der Verbindungsaufbau zu Port 30120, einmal pro Sekunde.\n\nGemessen: zwei Pings pro Sekunde je Strecke (Server ohne Ping-Antwort: ein Verbindungsaufbau pro Sekunde), Zeitlimit 1000 ms. Jitter = mittlerer Unterschied aufeinanderfolgender Pings, Spitze = mehr als doppelter Median und mindestens 30 ms darüber."},"hidden":{"kind":"ok","mild":false,"start":"","title":"Deine Verbindung war ruhig","text":"In diesen 60 Sekunden gab es bis ins Internet keinen nennenswerten Verlust und kaum Schwankungen.","advice":["Ruckelt FiveM trotzdem, kann es am Weg zum Server, am Server selbst oder an FPS-Einbrüchen auf deinem PC liegen. Das zeigt dieser Test ohne Server nicht.","Am meisten sagt der Test, wenn du ihn genau dann startest, wenn der Lag auftritt."],"notes":["Der Server hält seine Adresse geheim, deshalb wurden nur Router und Internet gemessen.","Du bist per WLAN verbunden, im Test war es ruhig. Schwankt es zu anderen Zeiten, bleibt ein LAN-Kabel die stabilste Lösung."],"wifi":true,"seconds":60,"targets":[{"id":"gw","label":"Router","name":"Router","host":"192.168.178.1","mode":"icmp","port":0,"measured":true,"level":"ok","why":"","start":false,"holds":"ok","stats":{"sent":120,"answered":120,"loss":0,"median":2,"p95":4,"jitter":0.8,"spikes":0,"high":0,"max":4,"seconds":60}},{"id":"net","label":"Internet","name":"Internet","host":"1.1.1.1","mode":"icmp","port":0,"measured":true,"level":"ok","why":"","start":false,"holds":"ok","stats":{"sent":120,"answered":120,"loss":0,"median":12,"p95":13,"jitter":1,"spikes":0,"high":0,"max":14,"seconds":60}},{"id":"srv","label":"Server","name":"NovaCity | Roleplay [GER]","host":"","mode":"icmp","port":443,"measured":false,"level":"none","why":"","start":false,"holds":"none","stats":{"sent":0,"answered":0,"loss":0,"median":null,"p95":null,"jitter":null,"spikes":0,"high":0,"max":null,"seconds":0}}],"report":"OrbitTweak Netz-Check · 22.11.2026, 14:05 Uhr · 60 Sekunden · Verbindung: WLAN\nErgebnis: Deine Verbindung war ruhig\nIn diesen 60 Sekunden gab es bis ins Internet keinen nennenswerten Verlust und kaum Schwankungen.\n\nRouter (192.168.178.1): Ping 2 ms (Median) · 95 % bis 4 ms · Jitter <1 ms · Verlust 0 % (0 von 120) · 0 Spitzen (höchster Wert 4 ms)\nInternet (1.1.1.1): Ping 12 ms (Median) · 95 % bis 13 ms · Jitter 1 ms · Verlust 0 % (0 von 120) · 0 Spitzen (höchster Wert 14 ms)\nServer NovaCity | Roleplay [GER] (cfx.re/join/abc123): nicht gemessen\n\nHinweis: Der Server hält seine Adresse geheim, deshalb wurden nur Router und Internet gemessen.\nHinweis: Du bist per WLAN verbunden, im Test war es ruhig. Schwankt es zu anderen Zeiten, bleibt ein LAN-Kabel die stabilste Lösung.\n\nGemessen: zwei Pings pro Sekunde je Strecke (Server ohne Ping-Antwort: ein Verbindungsaufbau pro Sekunde), Zeitlimit 1000 ms. Jitter = mittlerer Unterschied aufeinanderfolgender Pings, Spitze = mehr als doppelter Median und mindestens 30 ms darüber."}}/*/NC-CANNED*/;
})();
