'use strict';
/* =====================================================================
   SPIELZEIT (2.27): play time per day, week and in total, the servers, records and the last rounds.
   The host follows the game and keeps the rounds (MainWindow.Stats.cs, Core/PlayStats.cs); "stats.get" brings everything
   this page shows, "stats.clear" / "stats.remove" delete. Also here: one small figure on the start page and the
   "Runde vorbei" note when a round of five minutes or more ends.
   ===================================================================== */

const ST = { d: null, at: 0, err: null, game: false, n: 14, all: false, busy: null, again: false, anim: false };
try { if (localStorage.getItem('st.range') === '30') ST.n = 30; } catch { }
const ST_WD = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];
const ST_WDL = ['Sonntag', 'Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag'];
const ST_MO = ['Jan.', 'Feb.', 'März', 'Apr.', 'Mai', 'Juni', 'Juli', 'Aug.', 'Sept.', 'Okt.', 'Nov.', 'Dez.'];
const ST_SHORT = 12;   // rounds shown before "Alle zeigen"

/* ---------- formats: one style for every duration on the page, "2 Std. 14 Min." ---------- */
const stHM = s => { const m = Math.max(0, Math.floor((s || 0) / 60)); return [Math.floor(m / 60), m % 60]; };
function stDur(s) { const [h, m] = stHM(s); return h ? `${h.toLocaleString('de-DE')}\u00A0Std.${m ? ` ${m}\u00A0Min.` : ''}` : `${m}\u00A0Min.`; }   // a number never parts from its unit
/** The same as a large figure: the digits big and light, the units small. */
function stBig(s) { const [h, m] = stHM(s); return h ? `${h.toLocaleString('de-DE')}<small>Std.</small>${m ? `${m}<small>Min.</small>` : ''}` : `${m}<small>Min.</small>`; }
const stPad = n => String(n).padStart(2, '0');
const stClock = t => { const d = new Date(t); return stPad(d.getHours()) + ':' + stPad(d.getMinutes()); };
const stMidnight = t => { const d = new Date(t); d.setHours(0, 0, 0, 0); return d.getTime(); };
const stAddDays = (t, n) => { const d = new Date(t); d.setDate(d.getDate() + n); return d.getTime(); };
const stKey = t => { const d = new Date(t); return `${d.getFullYear()}-${stPad(d.getMonth() + 1)}-${stPad(d.getDate())}`; };
const stParse = k => { const [y, m, d] = k.split('-').map(Number); return new Date(y, m - 1, d).getTime(); };
const stDate = (t, year) => { const d = new Date(t); return `${d.getDate()}. ${ST_MO[d.getMonth()]}${year && d.getFullYear() !== new Date().getFullYear() ? ' ' + d.getFullYear() : ''}`; };
/** "Heute", "Gestern", else "Mo, 16. Nov." (long: "Montag, 16. Nov."). */
function stDayName(t, long) {
  const d0 = stMidnight(t), t0 = stMidnight(Date.now());
  if (d0 === t0) return 'Heute';
  if (d0 === stAddDays(t0, -1)) return 'Gestern';
  return `${(long ? ST_WDL : ST_WD)[new Date(t).getDay()]}, ${stDate(t, true)}`;
}
const stCount = (n, one, many) => `${n.toLocaleString('de-DE')} ${n === 1 ? one : many}`;
const stPct = s => s > 0 && s < .005 ? '< 1 %' : Math.round(s * 100) + ' %';
/** Seconds the open round has run since the numbers were fetched (the page counts on by itself in between). */
const stExtra = () => ST.d && ST.d.current ? Math.max(0, (performance.now() - ST.at) / 1000) : 0;
/** When counting began (the first round, or the one being played when it is the very first). Before that day nothing
    was counted: "noch nicht gezählt", never "nicht gespielt". */
const stFrom = () => ST.d ? (ST.d.since ?? (ST.d.current ? ST.d.current.start : null)) : null;
const stPre = t => { const f = stFrom(); return f != null && t < stMidnight(f); };

/* ---------- loading ---------- */
async function stLoad(animate) {
  ST.anim = ST.anim || !!animate;   // a load already on its way draws with the animation too
  // one on its way may have asked before a round was removed or the history cleared: one more right after it
  if (ST.busy) { ST.again = true; return ST.busy; }
  ST.busy = (async () => {
    const game = !!S.fmGame;   // what this answer knows about the game (see the 2 s look below)
    try { ST.d = await api('stats.get'); ST.at = performance.now(); ST.err = null; }
    catch (e) { ST.err = (e && e.message) || 'Unbekannter Fehler'; }
    ST.game = game;
    stHome();
    if (curPage === 'stats') { const a = ST.anim; ST.anim = false; stRender(a); }
  })().finally(() => { ST.busy = null; if (ST.again) { ST.again = false; stLoad(false); } });
  return ST.busy;
}

/* ---------- the page ---------- */
const stAnim = () => !resting() && !matchMedia('(prefers-reduced-motion: reduce)').matches;

function stRender(animate) {
  const el = $('#p-stats'); if (!el) return;
  const d = ST.d;
  const mode = !d ? (ST.err ? 'err' : 'wait') : d.rounds === 0 && !d.current ? 'empty' : 'full';
  if (el.dataset.mode !== mode) { el.dataset.mode = mode; el.innerHTML = stShell(mode); animate = true; }
  if (mode === 'err') { $('#stErr').textContent = ST.err; return; }
  if (mode !== 'full') return;
  animate = animate && stAnim();
  stLiveFill(); stSumFill(); stChartFill(animate); stSrvFill(animate); stRecFill(); stHoursFill(animate); stRoundsFill();
}

function stShell(mode) {
  const head = `<div class="ph"><div><div class="kicker">Spielzeit</div><h1>Deine Zeit in <span class="grad">Los Santos</span></h1>
    <p class="pd">Wie lange du spielst, auf welchen Servern und mit wie vielen Kills. OrbitTweak zählt von selbst mit, sobald FiveM läuft.</p></div></div>`;
  if (mode === 'wait') return head;
  if (mode === 'err') return head + `<div class="card st-msg" style="--i:0"><span class="st-ei">${ico('alert')}</span><div><b>Spielzeit gerade nicht abrufbar</b><p id="stErr"></p></div></div>`;
  if (mode === 'empty') return head + stEmptyHtml();
  return head + `<div class="st-grid">
    <div class="card st-live st-full hidden" id="stLive" style="--i:0"></div>
    <div class="card st-sum st-full" id="stSum" style="--i:1"></div>
    <div class="card st-chart" style="--i:2">
      <div class="st-h"><div><b>Verlauf</b><p id="stAvg"></p></div><div class="seg" id="stRange"><button data-n="14">14 Tage</button><button data-n="30">30 Tage</button></div></div>
      <div class="st-plot"><div class="st-y" id="stY"></div><div class="st-area"><div class="st-gls" id="stGl"></div><div class="st-bars" id="stBars"></div></div></div>
      <div class="st-x"><span></span><div class="st-xs" id="stX"></div></div>
      <div class="st-tip"></div>
    </div>
    <div class="card st-srv" style="--i:3"><div class="st-h"><div><b>Deine Server</b><p id="stSrvSub"></p></div></div><div id="stSrv"></div></div>
    <div class="card st-rec" id="stRec" style="--i:4"></div>
    <div class="card st-hours" style="--i:5">
      <div class="st-h"><div><b>Wann du spielst</b><p id="stHrsSub"></p></div></div>
      <div class="st-hrs" id="stHrs"></div><div class="st-hx">${[0, 6, 12, 18, 24].map(h => `<span style="left:${h / 24 * 100}%">${h === 24 ? '24 Uhr' : h}</span>`).join('')}</div>
      <div class="st-tip"></div>
    </div>
    <div class="card st-rounds st-full" style="--i:6">
      <div class="st-h"><div><b>Letzte Runden</b><p id="stRSub"></p></div><button class="btn sm gh" id="stClear" type="button">${ico('trash')}Verlauf löschen</button></div>
      <div id="stList"></div>
    </div>
  </div>`;
}

function stEmptyHtml() {
  const h = [34, 58, 22, 0, 66, 48, 82, 40, 0, 28, 54, 72, 38, 0];
  const t0 = stMidnight(Date.now());
  const bars = h.map((v, i) => i === h.length - 1 ? '<i class="next"></i>' : `<i style="height:${v ? v + '%' : '2px'}"></i>`).join('');
  const labels = h.map((_, i) => `<span>${i === h.length - 1 ? 'Heute' : ST_WD[new Date(stAddDays(t0, i - h.length + 1)).getDay()]}</span>`).join('');
  return `<div class="card st-empty" style="--i:0">
    <div class="st-el">
      <span class="st-ei">${ico('chart')}</span>
      <h2>Noch keine Runde gezählt</h2>
      <p>Starte FiveM wie immer. Solange OrbitTweak dabei offen ist, auch minimiert, zählt es ab deiner nächsten Runde von selbst mit.</p>
      <ul>
        <li>${ico('clock')}Spielzeit pro Tag, Woche und insgesamt</li>
        <li>${ico('server')}Deine Server und ihr Anteil an der Zeit</li>
        <li>${ico('trophy')}Rekorde: längste Runde, meiste Kills, Tage am Stück</li>
      </ul>
      <button class="btn" type="button" onclick="go('launcher')">${ico('rocket')}Zum Launcher</button>
    </div>
    <div class="st-er" aria-hidden="true"><div class="st-ghost">${bars}</div><div class="st-gx">${labels}</div></div>
  </div>
  <p class="st-note">Runden unter einer Minute zählen nicht, etwa wenn das Spiel beim Laden abstürzt. Kills zählen, wenn die Kill-Erkennung im Clipper an ist.</p>`;
}

/* the round being played: a slim strip on top that counts on by the minute */
function stLiveFill() {
  const c = ST.d.current, el = $('#stLive');
  el.classList.toggle('hidden', !c); if (!c) return;
  const srv = c.server || c.addr;
  const chips = [c.kills ? `<span class="chip">${ico('target')}${stCount(c.kills, 'Kill', 'Kills')}</span>` : '', c.clips ? `<span class="chip">${ico('film')}${stCount(c.clips, 'Clip', 'Clips')}</span>` : ''].join('');
  el.innerHTML = `<span class="st-dot"></span><div class="st-lt"><b>Gerade im Spiel</b><span class="st-dim"> · </span><span id="stLiveT">${stDur(c.secs + stExtra())}</span>${srv ? `<span class="st-dim"> auf </span><b>${esc(srv)}</b>` : ''}</div>
    <span class="sp"></span>${chips}<span class="st-since">seit ${stClock(c.start)}</span>`;
}

function stSumFill() {
  const d = ST.d, x = stExtra();
  const today = d.todayRounds ? stCount(d.todayRounds, 'Runde', 'Runden') + (d.current ? ', eine läuft gerade' : '') : 'Heute noch nicht gespielt';
  const lastMon = stAddDays(stMidnight(Date.now()), -((new Date().getDay() + 6) % 7) - 7);   // Monday of last week
  const week = d.lastWeek >= 60 ? `Letzte Woche: ${stDur(d.lastWeek)}` : stPre(lastMon) ? 'Letzte Woche noch nicht gezählt' : 'Letzte Woche nicht gespielt';
  const total = d.since ? `${stCount(d.rounds, 'Runde', 'Runden')} seit ${stDate(d.since, true)}` : stCount(d.rounds, 'Runde', 'Runden');
  $('#stSum').innerHTML = [['Heute', 'clock', d.today, today], ['Diese Woche', 'chart', d.week, week], ['Gesamt', 'timer', d.total, total]]
    .map(([k, icon, v, s], i) => `<div class="st-m"><div class="lbl">${ico(icon)}${k}</div><b class="st-v" id="stV${i}">${stBig(v + x)}</b><span class="st-s">${esc(s)}</span></div>`).join('');
}

/* the days: plain DOM bars, today in the accent. They rise into place with a transform; a refresh only changes heights */
function stChartFill(animate) {
  const days = ST.d.days.slice(-ST.n), x = stExtra(), todayKey = stKey(Date.now());
  const vals = days.map((d, i) => d.secs + (i === days.length - 1 ? x : 0));
  const maxH = Math.max(...vals, 0) / 3600, step = maxH <= 3 ? 1 : maxH <= 8 ? 2 : 4, top = Math.max(step, Math.ceil(maxH / step) * step);
  $$('#stRange button').forEach(b => { const on = +b.dataset.n === ST.n; b.classList.toggle('on', on); b.setAttribute('aria-pressed', on); });
  const played = vals.filter(v => v >= 60);
  $('#stAvg').textContent = played.length ? `Ø ${stDur(played.reduce((a, b) => a + b, 0) / played.length)} an ${stCount(played.length, 'Spieltag', 'Spieltagen')}` : 'In diesem Zeitraum nicht gespielt';
  let gl = '', yl = '';
  for (let v = step; v <= top + 1e-9; v += step) { const p = (v / top * 100).toFixed(2); gl += `<i style="bottom:${p}%"></i>`; yl += `<span style="bottom:${p}%">${v} Std.</span>`; }
  $('#stGl').innerHTML = '<i class="base" style="bottom:0"></i>' + gl; $('#stY').innerHTML = yl;
  const bars = $('#stBars'), sig = ST.n + '|' + days[0].day + '|' + days.length;
  const h = v => v < 30 ? '' : Math.max(1.2, v / (top * 3600) * 100).toFixed(2) + '%';
  // the value of each day for screen readers (the tooltip is for the mouse)
  const lab = i => `${stDayName(stParse(days[i].day), true)}: ${vals[i] >= 60 ? stDur(vals[i]) : stPre(stParse(days[i].day)) ? 'noch nicht gezählt' : 'nicht gespielt'}`;
  bars.closest('.st-chart').style.setProperty('--gap', ST.n > 14 ? '4px' : '8px');
  if (bars.dataset.sig === sig && !animate) {   // same days: only the heights change (the open round grows)
    [...bars.children].forEach((c, i) => { c.classList.toggle('zero', vals[i] < 30); c.firstChild.style.height = h(vals[i]); c.setAttribute('aria-label', lab(i)); });
  } else {
    bars.dataset.sig = sig;
    bars.innerHTML = days.map((d, i) => `<div class="st-col${d.day === todayKey ? ' today' : ''}${vals[i] < 30 ? ' zero' : ''}" data-k="${i}" role="img" aria-label="${esc(lab(i))}"><i class="st-bar" style="height:${h(vals[i])};--k:${i}"></i></div>`).join('');
    stRise(bars, animate);
  }
  // labels: two lines for 14 days; for 30 only Mondays and today (each with room to the next). "Heute" needs
  // about 36 px: where the days stand closer, today keeps its weekday (its bar is marked anyway)
  const ti = days.findIndex(d => d.day === todayKey), pitch = bars.clientWidth / days.length;
  $('#stX').innerHTML = days.map((d, i) => {
    const t = stParse(d.day), wd = new Date(t).getDay(), date = new Date(t).getDate() + '.';
    if (i === ti) return `<span class="today"><b>${ST.n === 14 && pitch < 36 ? ST_WD[wd] : 'Heute'}</b>${ST.n === 14 ? date : ''}</span>`;
    if (ST.n === 14) return `<span><b>${ST_WD[wd]}</b>${date}</span>`;
    return wd === 1 && (ti < 0 || Math.abs(ti - i) > 2) ? `<span><b>Mo</b>${date}</span>` : '<span></span>';
  }).join('');
}

/** Bars come up from the base line one after the other (transform only), unless the page rests. */
function stRise(box, animate) {
  if (!animate) { box.classList.remove('st-rise', 'st-go'); return; }
  box.classList.remove('st-go'); box.classList.add('st-rise'); void box.offsetWidth;
  requestAnimationFrame(() => requestAnimationFrame(() => box.classList.add('st-go')));
  clearTimeout(box._t); box._t = setTimeout(() => box.classList.remove('st-rise', 'st-go'), 1600);
}

function stSrvFill(animate) {
  const list = ST.d.servers;
  $('#stSrvSub').textContent = ST.d.since ? `Nach Spielzeit, seit ${stDate(ST.d.since, true)}` : 'Nach Spielzeit';
  $('#stSrv').innerHTML = list.map((s, i) => {
    const name = s.name || s.addr, ini = name ? (name.match(/[A-Za-z0-9ÄÖÜäöü]/) || ['?'])[0].toUpperCase() : '';
    return `<div class="st-sr${name ? '' : ' unk'}${i === 0 ? ' first' : ''}" title="${s.name ? esc(s.name) + ' · ' : ''}${esc(stCount(s.rounds, 'Runde', 'Runden'))}${s.addr ? ' · ' + esc(s.addr) : ''}">
      <span class="st-si">${name ? esc(ini) : ico('server')}</span>
      <span class="st-sn">${esc(name || 'Server nicht erkannt')}</span><span class="st-st">${stDur(s.secs)}</span>
      <span class="st-sb"><i style="--s:${Math.max(.004, s.share).toFixed(4)}"></i></span><span class="st-sp">${stPct(s.share)}</span>
    </div>`;
  }).join('') || `<p class="st-none">Noch kein Server erkannt</p>`;
  stRise($('#stSrv'), animate);
}

function stRecFill() {
  const d = ST.d, r = d.records;
  const streakSub = r.bestStreak > r.streak ? `Rekord: ${stCount(r.bestStreak, 'Tag', 'Tage')}` : r.streak > 1 ? 'Dein bisher längster Lauf' : 'Jeder Tag mit Spiel in Folge zählt';
  const tiles = [
    ['Längste Runde', 'timer', r.longest ? stBig(r.longest.secs) : '–', r.longest ? stDate(r.longest.at, true) + (r.longest.server ? ' · ' + r.longest.server : '') : ''],
    ['Meiste Kills', 'target', r.mostKills ? `${r.mostKills.kills}<small>${r.mostKills.kills === 1 ? 'Kill' : 'Kills'}</small>` : '–',
      r.mostKills ? `In einer Runde · ${stDate(r.mostKills.at, true)}` : 'Zählt, wenn die Kill-Erkennung im Clipper an ist'],
    ['Tage am Stück', 'flame', `${r.streak}<small>${r.streak === 1 ? 'Tag' : 'Tage'}</small>`, streakSub],
    ['Ø pro Runde', 'activity', stBig(r.avg), `Aus ${stCount(d.rounds, 'Runde', 'Runden')}`],
  ];
  $('#stRec').innerHTML = tiles.map(([k, icon, v, s]) => `<div class="st-t"><div class="lbl">${ico(icon)}${k}</div><b class="st-v${v === '–' ? ' none' : ''}">${v}</b><span class="st-s" title="${esc(s)}">${esc(s)}</span></div>`).join('');
}

function stHoursFill(animate) {
  const h = ST.d.hours, max = Math.max(...h, 0);
  let a = 0, best = -1;
  for (let i = 0; i < 24; i++) { const s = h[i] + h[(i + 1) % 24] + h[(i + 2) % 24]; if (s > best) { best = s; a = i; } }
  const peak = i => max > 0 && ((i - a + 24) % 24) < 3;
  $('#stHrsSub').textContent = max > 0 ? `Meist zwischen ${a} und ${(a + 3) % 24} Uhr · letzte 30 Tage` : 'In den letzten 30 Tagen nicht gespielt';
  $('#stHrs').innerHTML = h.map((v, i) => `<i data-h="${i}" class="${peak(i) ? 'pk' : ''}${v < 30 ? ' zero' : ''}" style="height:${v < 30 ? '' : Math.max(4, v / max * 100).toFixed(1) + '%'};--k:${i}"></i>`).join('');
  stRise($('#stHrs'), animate);
}

function stRoundsFill() {
  const list = ST.d.recent, show = ST.all ? list : list.slice(0, ST_SHORT);
  $('#stRSub').textContent = !list.length ? 'Noch keine Runde beendet' : list.length >= 50 ? 'Die letzten 50 Runden' : stCount(list.length, 'Runde', 'Runden');
  $('#stClear').classList.toggle('hidden', !list.length);
  let html = '', day = '';
  for (const r of show) {
    const k = stKey(r.start);
    if (k !== day) { html += `${day ? '</div>' : ''}<div class="st-dg"><div class="st-dh">${esc(stDayName(r.start, true))}</div>`; day = k; }
    const srv = r.server || r.addr;
    const chips = (r.kills ? `<span class="chip">${stCount(r.kills, 'Kill', 'Kills')}</span>` : '') + (r.clips ? `<span class="chip">${stCount(r.clips, 'Clip', 'Clips')}</span>` : '');
    html += `<div class="st-rr"><span class="st-rt">${stClock(r.start)}–${stClock(r.end)}</span><span class="st-rs${srv ? '' : ' unk'}"${srv ? ` title="${esc(srv)}"` : ''}>${esc(srv || 'Server nicht erkannt')}</span>
      <span class="st-rc">${chips}</span><span class="st-rl">${stDur(r.secs)}</span><button class="st-rx" type="button" data-rm="${r.id}" title="Runde entfernen" aria-label="Runde entfernen">${ico('x')}</button></div>`;
  }
  if (day) html += '</div>';
  if (list.length > ST_SHORT) html += `<div class="st-more"><button class="btn sm gh" type="button" data-all>${ST.all ? 'Weniger zeigen' : `Alle ${list.length} Runden zeigen`}</button></div>`;
  $('#stList').innerHTML = html || `<p class="st-none">Die laufende Runde erscheint hier, sobald sie vorbei ist.</p>`;
}

/** Once a minute is enough for the clock: the strip and the three big numbers count on while a round runs. */
function stTick() {
  const d = ST.d; if (!d || !d.current) return;
  const x = stExtra(), t = $('#stLiveT');
  if (t) t.textContent = stDur(d.current.secs + x);
  [d.today, d.week, d.total].forEach((v, i) => { const el = $('#stV' + i); if (el) el.innerHTML = stBig(v + x); });
}

/* ---------- the tooltip over a bar ---------- */
function stTip(el) {
  const card = el && el.closest('.card'), tip = card && card.querySelector('.st-tip');
  $$('#p-stats .st-tip.show').forEach(t => { if (t !== tip) t.classList.remove('show'); });
  if (!tip) return;
  let html;
  if (el.dataset.k != null) {
    const days = ST.d.days.slice(-ST.n), d = days[+el.dataset.k]; if (!d) return;
    const v = d.secs + (+el.dataset.k === days.length - 1 ? stExtra() : 0);
    html = `<b>${v >= 60 ? stDur(v) : stPre(stParse(d.day)) ? 'Noch nicht gezählt' : 'Nicht gespielt'}</b><span>${esc(stDayName(stParse(d.day)))}${d.rounds ? ' · ' + stCount(d.rounds, 'Runde', 'Runden') : ''}</span>`;
  } else {
    const i = +el.dataset.h, v = ST.d.hours[i];
    html = `<b>${v >= 60 ? stDur(v) : 'Kaum gespielt'}</b><span>${i}–${(i + 1) % 24 || 24} Uhr · letzte 30 Tage</span>`;
  }
  tip.innerHTML = html;
  const r = el.getBoundingClientRect(), b = (el.querySelector('.st-bar') || el).getBoundingClientRect(), c = card.getBoundingClientRect();
  const w = tip.offsetWidth, x = Math.min(c.width - w / 2 - 10, Math.max(w / 2 + 10, r.left + r.width / 2 - c.left));
  const y = Math.min(b.top, r.bottom - 2) - c.top - 10;
  tip.style.transform = `translate(${x.toFixed(1)}px,${y.toFixed(1)}px) translate(-50%,-100%)`;
  tip.classList.add('show');
}

/* ---------- actions ---------- */
async function stClear() {
  const n = ST.d ? ST.d.recent.length : 0;
  const ok = await confirmBox('Verlauf löschen?', `Alle gezählten Runden, Rekorde und Server-Zeiten werden gelöscht. Das lässt sich nicht rückgängig machen.${ST.d && ST.d.current ? ' Die laufende Runde zählt weiter.' : ''}`, { ok: 'Verlauf löschen', danger: true });
  if (!ok) return;
  try {
    const r = await api('stats.clear');
    toast('Verlauf gelöscht', r && r.removed ? stCount(r.removed, 'Runde', 'Runden') + ' entfernt' : '');
  } catch (e) { toast('Nicht gelöscht', e.message, false); }
  ST.all = false;
  stLoad(true);
}

async function stRemove(id) {
  const r = ST.d && ST.d.recent.find(x => String(x.id) === String(id)); if (!r) return;
  const where = r.server || r.addr, day = stDayName(r.start, true);
  const when = day === 'Heute' ? 'von heute' : day === 'Gestern' ? 'von gestern' : 'vom ' + day;
  const ok = await confirmBox('Runde entfernen?', `Die Runde ${when}, ${stClock(r.start)}–${stClock(r.end)} (${stDur(r.secs)})${where ? ' auf ' + where : ''} zählt danach nirgends mehr mit.`, { ok: 'Entfernen', danger: true });
  if (!ok) return;
  try { const res = await api('stats.remove', { id: r.id }); if (!res || !res.ok) toast('Nicht entfernt', 'Die Runde gibt es nicht mehr', false); }
  catch (e) { toast('Nicht entfernt', e.message, false); }
  stLoad(false);
}

{
  const page = $('#p-stats');
  page.addEventListener('click', e => {
    const n = e.target.closest('#stRange button');
    if (n) { ST.n = +n.dataset.n; try { localStorage.setItem('st.range', String(ST.n)); } catch { } stChartFill(stAnim()); return; }
    if (e.target.closest('#stClear')) return stClear();
    const rm = e.target.closest('[data-rm]'); if (rm) return stRemove(rm.dataset.rm);
    if (e.target.closest('[data-all]')) { ST.all = !ST.all; stRoundsFill(); }
  });
  page.addEventListener('pointerover', e => { const t = e.target.closest('[data-k],[data-h]'); if (t) stTip(t); });
  page.addEventListener('pointerout', e => {
    if (e.target.closest('[data-k],[data-h]') && !(e.relatedTarget && e.relatedTarget.closest && e.relatedTarget.closest('[data-k],[data-h]'))) $$('#p-stats .st-tip.show').forEach(t => t.classList.remove('show'));
  });
}

/* ---------- the start page: one small figure in the banner, a click away from this page ---------- */
function stHome() {
  const box = $('#p-home .bn-r'); if (!box) return;
  let b = $('#stHome');
  const d = ST.d, x = stExtra(), today = d ? d.today + x : 0, week = d ? d.week + x : 0;
  if (!d || (today < 60 && week < 60)) { if (b) b.classList.add('hidden'); return; }
  if (!b) {
    box.insertAdjacentHTML('beforeend', '<button class="bn-chip st-hm" id="stHome" type="button"></button>');
    b = $('#stHome'); b.onclick = () => go('stats');
    // the banner's text changes with the window, the theme and the name: keep the figure clear of it
    if (window.ResizeObserver) {
      let raf = 0; const ro = new ResizeObserver(() => { cancelAnimationFrame(raf); raf = requestAnimationFrame(stHomeFit); });
      [$('#banner'), $('#banner .bn-txt'), box].forEach(el => el && ro.observe(el));
    }
    if (document.fonts) document.fonts.ready.then(stHomeFit);
  }
  const [icon, long, short, v] = today >= 60 ? ['clock', 'Heute gespielt', 'Heute', today] : ['chart', 'Diese Woche', 'Woche', week];
  b.innerHTML = `${ico(icon)}<span class="l">${long}</span><span class="s">${short}</span><b>${stDur(v)}</b>`;
  b.title = `${long}: ${stDur(v)} – zur Spielzeit`;
  b.classList.remove('hidden');
  stHomeFit();
}

/** The figure never sits on the greeting: first the short label, and where even that does not fit it stays away. */
function stHomeFit() {
  const b = $('#stHome'), row = b && b.parentElement, ban = $('#banner');
  if (!b || !ban || b.classList.contains('hidden')) return;
  b.classList.remove('tight', 'off');
  if (!b.offsetParent) return;   // the start page is not shown: measured when it is
  const hits = () => {
    // the chips in the corner together (they may stick out of their row when it runs out of room)
    const cs = [...row.children].map(c => c.getBoundingClientRect()).filter(q => q.width > 0), bx = ban.getBoundingClientRect();
    if (!cs.length) return false;
    const r = { left: Math.min(...cs.map(q => q.left)), right: Math.max(...cs.map(q => q.right)), top: Math.min(...cs.map(q => q.top)), bottom: Math.max(...cs.map(q => q.bottom)) };
    if (r.left < bx.left + 16) return true;
    return $$('#banner .bn-txt > *').some(el => {
      const rg = document.createRange(); rg.selectNodeContents(el);
      return [...rg.getClientRects()].some(q => q.width > 0 && q.right > r.left - 12 && q.left < r.right + 12 && q.bottom > r.top - 6 && q.top < r.bottom + 6);
    });
  };
  if (hits()) { b.classList.add('tight'); if (hits()) b.classList.add('off'); }
}

/* ---------- hooks: page, host event, timers ---------- */
PAGE_SHOW.stats = () => stLoad(true);   // the host answers in a few ms: fresh numbers, then the bars rise
{ const prev = PAGE_SHOW.home; PAGE_SHOW.home = () => { if (prev) prev(); stLoad(false); }; }

/** A round of five minutes or more ended (game closed or server changed): a short note, a band after a long one. */
EVENT_EXT.round = d => {
  const where = d.server ? ` auf ${d.server}` : '';
  const more = [d.kills ? stCount(d.kills, 'Kill', 'Kills') : '', d.clips ? stCount(d.clips, 'Clip', 'Clips') : ''].filter(Boolean).join(' · ');
  const text = `${stDur(d.secs)}${where}${more ? ' · ' + more : ''}`;
  if (d.secs >= 3600) passed('Runde vorbei', text); else toast('Runde vorbei', text);
  stLoad(false);
};

setInterval(() => {
  if (!ST.d || !ST.d.current || document.hidden) return;
  if (curPage === 'stats') stTick(); else if (curPage === 'home') stHome();
}, 30000);
setInterval(() => { if (!document.hidden && (curPage === 'stats' || curPage === 'home')) stLoad(false); }, 5 * 60000);
// the game was started or closed: fresh numbers right away (the host sends "round" only for rounds of five minutes or more)
setInterval(() => { if (ST.d && !ST.busy && !!S.fmGame !== ST.game && !document.hidden && (curPage === 'stats' || curPage === 'home')) stLoad(false); }, 2000);
// the start page figure: once the app is ready (without a key the host says no; it is tried again on the next visit)
[1500, 6000, 15000].forEach(ms => setTimeout(() => { if (!ST.d) stLoad(false); }, ms));

/* ======================= MOCK (browser preview): about three weeks of rounds, the same shapes as the host ======================= */
const ST_MOCK = HOST ? null : (() => {
  const rnd = seeded(2711), now = Date.now(), t0 = stMidnight(now), MIN = 60e3;
  const SRV = [['cfx:nv7k2p', 'NovaCity | Roleplay [GER]'], ['cfx:bl4e9x', 'BlueLine PvP Arena'], ['cfx:gv3m1q', 'GreenValley RP'], [null, null]];
  const rounds = []; let id = 1;
  const off = new Set([3, 8, 9, 15, 19]);   // days without play
  for (let back = 21; back >= 1; back--) {
    if (off.has(back)) continue;
    const n = 1 + (rnd() < .45 ? 1 : 0) + (rnd() < .15 ? 1 : 0);
    let t = stAddDays(t0, -back) + Math.round((17.5 + rnd() * 3.5) * 60) * MIN;
    for (let k = 0; k < n; k++) {
      const p = rnd(), s = p < .55 ? SRV[0] : p < .8 ? SRV[1] : p < .95 ? SRV[2] : SRV[3], pvp = s === SRV[1];
      const len = Math.round(22 + rnd() * (pvp ? 70 : 190)) * MIN;
      const kills = pvp ? Math.round(4 + rnd() * 18) : rnd() < .4 ? Math.round(rnd() * 4) : 0;
      const clips = kills ? Math.round(kills / (2 + rnd() * 3)) : 0;
      rounds.push({ id: id++, start: t, end: t + len, key: s[0], server: s[1], kills, clips, build: 'b3095' });
      t += len + Math.round(8 + rnd() * 40) * MIN;
    }
  }
  // today: one round a few hours ago, and the one being played since 72 minutes
  rounds.push({ id: id++, start: now - 250 * MIN, end: now - 187 * MIN, key: SRV[1][0], server: SRV[1][1], kills: 9, clips: 3, build: 'b3095' });
  return { rounds, open: { id: id++, start: now - 72 * MIN, key: SRV[0][0], server: SRV[0][1], kills: 3, clips: 1, build: 'b3095' } };
})();

/** What Core/PlayStats.cs Summarize gives the page, worked out the same way (for the browser preview only). */
function stMockSummary(list, open, now) {
  const all = list.map(r => ({ ...r })).concat(open ? [{ ...open, end: now }] : []).sort((a, b) => a.start - b.start);
  const secs = r => Math.max(0, (r.end - r.start) / 1000), addr = k => !k ? null : k.startsWith('cfx:') ? 'cfx.re/join/' + k.slice(4) : k.startsWith('ip:') ? k.slice(3) : k;
  const item = r => ({ id: r.id, start: r.start, end: r.end, secs: Math.round(secs(r)), server: r.server, addr: addr(r.key), kills: r.kills, clips: r.clips, build: r.build });
  const t0 = stMidnight(now), from = stAddDays(t0, -29), per = {}, hours = Array(24).fill(0);
  for (const r of all) {
    for (let t = r.start; t < r.end;) {
      const stop = Math.min(stAddDays(stMidnight(t), 1), r.end), k = stKey(t);
      (per[k] ||= { secs: 0, rounds: 0 }).secs += (stop - t) / 1000; per[k].rounds++; t = stop;
    }
    for (let t = r.start; t < r.end;) {
      const d = new Date(t), nx = new Date(t); nx.setMinutes(0, 0, 0); nx.setHours(d.getHours() + 1);
      const stop = Math.min(nx.getTime(), r.end);
      if (stMidnight(t) >= from) hours[d.getHours()] += (stop - t) / 1000;
      t = stop;
    }
  }
  const day = t => (per[stKey(t)] || { secs: 0 }).secs, span = (a, b) => { let s = 0; for (let t = a; t <= b; t = stAddDays(t, 1)) s += day(t); return s; };
  const week0 = stAddDays(t0, -((new Date(t0).getDay() + 6) % 7));
  const played = t => day(t) >= 60;
  let streak = 0, t = played(t0) ? t0 : stAddDays(t0, -1);
  while (played(t)) { streak++; t = stAddDays(t, -1); }
  let best = 0, run = 0, prev = null;
  for (const k of Object.keys(per).filter(k => per[k].secs >= 60).sort()) { const d = stParse(k); run = prev != null && stAddDays(prev, 1) === d ? run + 1 : 1; best = Math.max(best, run); prev = d; }
  const total = all.reduce((a, r) => a + secs(r), 0);
  const groups = {};
  for (const r of all) (groups[r.key || (r.server ? 'name:' + r.server : '')] ||= []).push(r);
  const servers = Object.values(groups).map(g => {
    const s = g.reduce((a, r) => a + secs(r), 0), named = g.filter(r => r.server).pop();
    return { name: named ? named.server : null, addr: addr(g[0].key), secs: Math.round(s), share: total > 0 ? Math.round(s / total * 1e4) / 1e4 : 0, rounds: g.length };
  }).sort((a, b) => b.secs - a.secs || (a.name == null) - (b.name == null)).slice(0, 5);
  const maxBy = (arr, f) => arr.reduce((m, r) => (m == null || f(r) > f(m) ? r : m), null);
  const longest = maxBy(all.filter(r => secs(r) > 0), secs), kills = maxBy(all.filter(r => r.kills > 0), r => r.kills);
  const rec = r => r ? { secs: Math.round(secs(r)), kills: r.kills, at: r.start, server: r.server || addr(r.key) } : null;
  return {
    now, current: open ? item({ ...open, end: now }) : null,
    today: Math.round(day(t0)), todayRounds: (per[stKey(t0)] || { rounds: 0 }).rounds,
    week: Math.round(span(week0, t0)), lastWeek: Math.round(span(stAddDays(week0, -7), stAddDays(week0, -1))),
    total: Math.round(total), rounds: all.length, since: all.length ? all[0].start : null,
    days: Array.from({ length: 30 }, (_, i) => { const d = stAddDays(t0, i - 29), v = per[stKey(d)] || { secs: 0, rounds: 0 }; return { day: stKey(d), secs: Math.round(v.secs), rounds: v.rounds }; }),
    hours: hours.map(Math.round),
    records: { longest: rec(longest), mostKills: rec(kills), streak, bestStreak: Math.max(streak, best), avg: all.length ? Math.round(total / all.length) : 0 },
    servers,
    recent: all.filter(r => !open || r.id !== open.id).sort((a, b) => b.start - a.start).slice(0, 50).map(item),
  };
}

MOCK_EXT['stats.get'] = async () => stMockSummary(ST_MOCK.rounds, ST_MOCK.open, Date.now());
MOCK_EXT['stats.clear'] = async () => { const removed = ST_MOCK.rounds.length; ST_MOCK.rounds = []; return { ok: true, removed }; };
MOCK_EXT['stats.remove'] = async a => { const n = ST_MOCK.rounds.length; ST_MOCK.rounds = ST_MOCK.rounds.filter(r => r.id !== a.id); return { ok: ST_MOCK.rounds.length < n }; };
