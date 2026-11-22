'use strict';
/* =====================================================================
   ENGPASS-ANALYSE (2.31): one "Leistung" card on the Spielzeit page. While FiveM runs it shows what holds the frame
   rate right now (graphics card, processor, a cap) with four small meters; afterwards the verdict of each round, with
   what helps (a link to the place in the app that does it) and what does not.
   The host measures (MainWindow.PerfWatch.cs, Core/PerfWatch.cs, Core/PerfWatchLogic.cs): "perf.get" brings it all,
   "perf.set" switches measuring on or off, the event "perf" brings each look (every 2 s) while a round is measured.
   The card lives in stats.js's page: it is put in place again whenever that page draws a new shell.
   ===================================================================== */

const PW = { d: null, err: null, busy: null, again: false, live: null, sel: null, view: 'now', all: false, mode: '', el: null, ev: 0 };
const PW_SHORT = 6;   // rounds shown before "Alle zeigen"
const PW_KINDS = [['gpu', 'Grafikkarte'], ['cpu', 'Prozessor'], ['cap', 'Gedeckelt'], ['mixed', 'Gemischt'], ['unknown', 'Ohne Werte']];

/* ---------- formats ---------- */
const pwNum = (v, d = 0) => v == null ? '–' : Number(v).toLocaleString('de-DE', { minimumFractionDigits: d, maximumFractionDigits: d });
const pwPct = v => v == null ? '–' : `${pwNum(v)} %`;
const pwShare = s => s > 0 && s < .005 ? '< 1 %' : `${Math.round(s * 100)} %`;
const pwGb = v => v == null ? '–' : `${pwNum(v, 1)} GB`;
/** "Heute, 18:04–19:30" (stats.js formats, so both cards speak alike). */
const pwWhen = r => `${stDayName(r.start, true)}, ${stClock(r.start)}–${stClock(r.end)}`;

/* ---------- loading ---------- */
async function pwLoad() {
  if (PW.busy) { PW.again = true; return PW.busy; }
  PW.busy = (async () => {
    try { PW.d = await api('perf.get'); PW.err = null; PW.live = PW.d.live || null; }
    catch (e) { PW.err = (e && e.message) || 'Unbekannter Fehler'; PW.d = null; PW.live = null; }   // its own message, never old numbers
    if (curPage === 'stats') { pwPlace(); pwRender(); }
  })().finally(() => { PW.busy = null; if (PW.again) { PW.again = false; pwLoad(); } });
  return PW.busy;
}

/* ---------- the card and its place on the Spielzeit page ---------- */
function pwCard() {
  if (PW.el) return PW.el;
  const c = document.createElement('div');
  c.className = 'card pw-card'; c.id = 'pwCard'; c.style.setProperty('--i', 2);
  c.innerHTML = `<div class="pw-h">
      <div class="pw-hl"><span class="pw-hi">${ico('pw-neck')}</span><div><b>Leistung</b><p id="pwSub"></p></div></div>
      <div class="pw-hr">
        <div class="seg pw-seg hidden" id="pwSeg" role="group" aria-label="Ansicht"><button type="button" data-pwv="now">Jetzt</button><button type="button" data-pwv="past">Letzte Runden</button></div>
        <div class="pw-sw"><span id="pwSwL">Mitmessen</span><div class="tg" id="pwTg" role="switch" tabindex="0" aria-checked="false" aria-labelledby="pwSwL"></div></div>
      </div>
    </div>
    <div class="pw-body" id="pwBody"></div>
    <p class="pw-foot" id="pwFoot">OrbitTweak misst keine FPS, sondern wer gerade am Anschlag läuft: alle 2 Sekunden und nur, solange FiveM vorne ist. Die Werte bleiben auf deinem PC.</p>`;
  c.addEventListener('click', e => {
    const v = e.target.closest('[data-pwv]'); if (v) { PW.view = v.dataset.pwv; pwRender(); return; }
    if (e.target.closest('#pwTg')) return pwToggle();
    const r = e.target.closest('[data-pwr]'); if (r) { PW.sel = r.dataset.pwr; pwPastFill(); $(`#pwCard [data-pwr="${CSS.escape(PW.sel)}"]`)?.focus(); return; }
    if (e.target.closest('[data-pwall]')) { PW.all = !PW.all; pwPastFill(); return; }
    const g = e.target.closest('[data-pwgo]'); if (g) pwGo(g.dataset.pwgo);
  });
  c.addEventListener('keydown', e => { if ((e.key === ' ' || e.key === 'Enter') && e.target.id === 'pwTg') { e.preventDefault(); pwToggle(); } });
  PW.el = c;
  return c;
}

/** stats.js draws its page as one shell per state: the card goes in under the three big numbers (or under its notes). */
function pwPlace() {
  const page = $('#p-stats'); if (!page) return;
  const mode = page.dataset.mode;
  if (!mode || mode === 'wait') { PW.el?.remove(); return; }
  const card = pwCard();
  if (mode === 'full') {
    const sum = $('#stSum');
    if (sum && card.previousElementSibling !== sum) sum.after(card);
  } else if (card.parentElement !== page || page.lastElementChild !== card) page.append(card);
  card.classList.toggle('pw-solo', mode !== 'full');
}
// Spielzeit's list of rounds is drawn again after every load there, also after a round was removed or the history
// cleared: the verdicts of those rounds go as well (the host drops them), so the card loads again
const PW_LIST = new MutationObserver(() => { clearTimeout(PW_LIST.t); PW_LIST.t = setTimeout(() => { if (curPage === 'stats' && (PW.d || PW.err)) pwLoad(); }, 400); });
new MutationObserver(() => {
  const list = $('#stList');
  if (list !== PW_LIST.el) { PW_LIST.disconnect(); PW_LIST.el = list; if (list) PW_LIST.observe(list, { childList: true }); }
  if (PW.d || PW.err) { pwPlace(); if (PW.mode === '' || !$('#pwBody')?.childElementCount) pwRender(); }
}).observe($('#p-stats'), { childList: true });

/* ---------- drawing ---------- */
function pwModeOf() {
  const d = PW.d;
  if (!d) return PW.err ? 'fail' : 'wait';
  if (!d.on) return 'off';
  // no values for a while (Windows' counters broken): said instead of "Messung läuft an" for ever
  const stuck = !!PW.live && PW.live.state === 'warmup';
  if (d.error && (stuck ? !(PW.view === 'past' && d.rounds.length) : !PW.live && !d.rounds.length)) return 'err';
  if (PW.live) return PW.view === 'past' && d.rounds.length ? 'past' : 'live';
  return d.rounds.length ? 'past' : 'empty';
}

function pwRender() {
  const card = PW.el; if (!card || !card.isConnected) return;
  const d = PW.d, mode = pwModeOf();
  const tg = $('#pwTg'), on = !!(d && d.on);
  tg.classList.toggle('on', on); tg.setAttribute('aria-checked', on);
  card.querySelector('.pw-sw').classList.toggle('hidden', !d || !!(d && d.unreadable));
  const seg = $('#pwSeg'), both = !!(PW.live && d && d.on && d.rounds.length);
  seg.classList.toggle('hidden', !both);
  $$('#pwSeg button').forEach(b => { const o = b.dataset.pwv === (mode === 'past' ? 'past' : 'now'); b.classList.toggle('on', o); b.setAttribute('aria-pressed', o); });
  $('#pwFoot').classList.toggle('hidden', mode === 'fail' || mode === 'off' || mode === 'wait');
  card.dataset.mode = mode;
  // the live view and the rounds are filled in place; the short states are simply drawn again
  if (PW.mode !== mode || !$('#pwBody').childElementCount || !['live', 'past'].includes(mode)) { PW.mode = mode; $('#pwBody').innerHTML = pwShell(mode); }
  if (mode === 'live') pwLiveFill();
  else if (mode === 'past') pwPastFill();
  pwSub(mode);
}

function pwSub(mode) {
  const d = PW.d, l = PW.live;
  const t = mode === 'live' ? (l.state === 'paused' ? 'Misst weiter, sobald FiveM wieder vorne ist' : 'Misst gerade mit, alle 2 Sekunden')
    : mode === 'past' ? (PW.live ? 'Die Runde läuft, hier die letzten Urteile' : 'Was deine FPS in den letzten Runden begrenzt hat')
    : mode === 'empty' ? 'Misst ab deiner nächsten Runde mit'
    : mode === 'off' ? 'Messung aus' : mode === 'err' ? 'Gerade keine Werte von Windows' : mode === 'fail' ? 'Gerade nicht abrufbar' : '';
  $('#pwSub').textContent = d && d.unreadable ? 'Messwerte gerade gesperrt, diese Sitzung wird nicht gespeichert' : t;
}

function pwShell(mode) {
  if (mode === 'wait') return '';
  if (mode === 'fail' || mode === 'err') {
    const msg = mode === 'fail' ? PW.err : PW.d.error;
    return `<div class="pw-msg"><span class="pw-ei">${ico('alert')}</span><div><b>${mode === 'fail' ? 'Leistung gerade nicht abrufbar' : 'Windows liefert keine Leistungswerte'}</b><p>${esc(msg)}</p></div></div>`;
  }
  if (mode === 'off') {
    const n = PW.d.rounds.length;
    return `<div class="pw-msg pw-plain"><div><b>Messung aus</b><p>OrbitTweak schaut nicht nach, was deine FPS begrenzt, und liest keine Leistungswerte.${n ? ` ${n === 1 ? 'Deine gemessene Runde bleibt' : `Deine ${n} gemessenen Runden bleiben`} gespeichert.` : ''}</p></div></div>`;
  }
  if (mode === 'empty') return pwEmptyHtml();
  if (mode === 'live') return `<div class="pw-live">
      <div class="pw-now">
        <div class="pw-st"><i class="pw-dot" id="pwDot"></i><span id="pwSince"></span></div>
        <h3 class="pw-t" id="pwNowT"></h3><p class="pw-p" id="pwNowP"></p>
        <div class="pw-mixw" id="pwMixW"><div class="pw-mixh"><span>Diese Runde bisher</span><b id="pwMixT"></b></div><div class="pw-mix" id="pwMix"></div><div class="pw-leg" id="pwLeg"></div></div>
      </div>
      <div class="pw-ms" id="pwMs">${['gpu', 'cpu', 'vram', 'ram'].map(k => `<div class="pw-m" data-m="${k}"></div>`).join('')}</div>
    </div>`;
  return `<div class="pw-past"><div class="pw-v" id="pwV"></div><div class="pw-list"><div class="pw-lh"><b>Letzte Runden</b><span id="pwLn"></span></div><div id="pwRows"></div></div></div>`;
}

function pwEmptyHtml() {
  const ghost = [['gpucard', 'Grafikkarte', 72], ['cpu', 'Prozessor', 46], ['pw-vram', 'Grafikspeicher', 58], ['ram', 'Arbeitsspeicher', 38]]
    .map(([i, n, w]) => `<div class="pw-gm"><span>${ico(i)}${n}</span><i style="--v:${w / 100}"></i></div>`).join('');
  return `<div class="pw-empty">
      <div class="pw-el">
        <h3 class="pw-t">Noch keine Runde gemessen</h3>
        <p class="pw-p">Sobald FiveM läuft, schaut OrbitTweak alle 2 Sekunden nach, ob Grafikkarte, Prozessor oder Speicher deine FPS begrenzen. Nach der Runde steht hier, was wirklich hilft und was nicht.</p>
      </div>
      <div class="pw-ghost" aria-hidden="true">${ghost}</div>
    </div>`;
}

/** The share of the time per kind as one fine bar, with a legend of the kinds that took at least 1 %. */
function pwMixHtml(s) {
  const parts = PW_KINDS.filter(([k]) => s[k] >= .01);
  const bar = parts.map(([k]) => `<i class="pw-k-${k}" style="flex:${s[k]}"></i>`).join('');
  const leg = parts.map(([k, n]) => `<span><i class="pw-k-${k}"></i>${n} <b>${pwShare(s[k])}</b></span>`).join('');
  return [bar || '<i class="pw-k-none" style="flex:1"></i>', leg];
}

/* the round being measured: headline, the round so far, four meters */
function pwLiveFill() {
  const l = PW.live; if (!l || !$('#pwMs')) return;
  const n = l.now, warm = l.state === 'warmup', paused = l.state === 'paused';
  $('#pwDot').className = 'pw-dot' + (l.state === 'live' ? ' on' : '');
  $('#pwSince').textContent = warm ? 'Gleich geht’s los' : !l.since ? 'Gerade' : paused ? `Runde seit ${stClock(l.since)}` : `Gerade · seit ${stClock(l.since)}`;
  $('#pwNowT').textContent = l.title; $('#pwNowP').textContent = l.text;
  const mixOk = l.shares && l.secs >= 10;
  $('#pwMixW').classList.toggle('hidden', !mixOk);
  if (mixOk) {
    const [bar, leg] = pwMixHtml(l.shares);
    $('#pwMix').innerHTML = bar; $('#pwLeg').innerHTML = leg; $('#pwMixT').textContent = `${stDur(l.secs)} gemessen`;
  }
  const hot = !paused && l.kind;
  const M = {
    gpu: ['gpucard', ['Grafikkarte', 'Grafikkarte'], n && n.gpu != null ? pwPct(n.gpu) : '–', n && n.gpu != null ? n.gpu : 0,
      !n ? 'Kommt gleich' : n.gpu == null ? 'Keine Werte von Windows' : n.own ? '3D-Last von FiveM' : '3D-Last der ganzen Karte', hot === 'gpu', false],
    cpu: ['cpu', ['Prozessor', 'Prozessor'], n ? pwPct(n.core) : '–', n ? n.core : 0, n ? [`Stärkster Kern · gesamt ${pwPct(n.cpu)}`, `Kern · gesamt ${pwPct(n.cpu)}`] : 'Kommt gleich', hot === 'cpu', false],
    vram: ['pw-vram', ['Grafikspeicher', 'VRAM'], n && n.vram != null ? pwGb(n.vramUsedGb) : '–', n && n.vram != null ? n.vram : 0,
      !n ? 'Kommt gleich' : n.vram == null ? 'Größe unbekannt' : `von ${pwGb(n.vramGb)}${n.vramFull ? ' · voll' : ` · ${pwPct(n.vram)}`}`, false, !!(n && n.vramFull)],
    ram: ['ram', ['Arbeitsspeicher', 'RAM'], n ? pwGb(n.ramUsedGb) : '–', n ? n.ram : 0, n ? `von ${pwGb(n.ramGb)}${n.ramTight ? ' · knapp' : ` · ${pwPct(n.ram)}`}` : 'Kommt gleich', false, !!(n && n.ramTight)],
  };
  for (const [k, [icon, name, val, pct, sub, isHot, warn]] of Object.entries(M)) {
    const el = $(`#pwMs [data-m="${k}"]`);
    // long and short words: the short ones where the meters get narrow (perf.css)
    if (!el.firstChild) el.innerHTML = `<div class="pw-mh"><span class="pw-mk" title="${name[0]}">${ico(icon)}<span class="pw-lw">${name[0]}</span><span class="pw-sw2">${name[1]}</span></span><b class="pw-mv"></b></div><span class="pw-mb"><i></i></span><span class="pw-mn"></span>`;
    el.classList.toggle('hot', isHot); el.classList.toggle('warn', warn); el.classList.toggle('dim', paused || warm);
    el.querySelector('.pw-mv').textContent = val;
    el.querySelector('.pw-mb i').style.transform = `scaleX(${Math.max(0, Math.min(1, pct / 100)).toFixed(3)})`;
    const [lw, sw] = Array.isArray(sub) ? sub : [sub, sub];
    el.querySelector('.pw-mn').innerHTML = lw === sw ? esc(lw) : `<span class="pw-lw">${esc(lw)}</span><span class="pw-sw2">${esc(sw)}</span>`;
  }
}

/* the rounds: the chosen one's verdict on the left, the list on the right */
function pwPastFill() {
  const list = PW.d.rounds; if (!list.length || !$('#pwV')) return;
  let r = list.find(x => String(x.id) === String(PW.sel));
  if (!r) { r = list[0]; PW.sel = String(r.id); }
  const v = r.verdict, warn = v.key === 'vram' || v.key === 'ram';
  const [bar, leg] = pwMixHtml(r.shares);
  const help = v.help.map(h => `<div class="pw-tip"><span>${esc(h.text)}</span>${h.go ? `<button class="btn sm gh" type="button" data-pwgo="${esc(h.go)}">${esc(h.label)}${ico('chev')}</button>` : ''}</div>`).join('');
  $('#pwV').innerHTML = `<div class="pw-vh"><span class="chip${warn ? ' w' : ''}">${esc(v.short)}</span><span class="pw-when">${esc(pwWhen(r))}${r.server ? ` · ${esc(r.server)}` : ''}</span></div>
    <h3 class="pw-t">${esc(v.title)}</h3><p class="pw-p">${esc(v.text)}</p>
    <div class="pw-mixw"><div class="pw-mix">${bar}</div><div class="pw-leg">${leg}</div></div>
    <div class="pw-sec"><div class="pw-sh">${ico('check')}Was hilft</div>${help}</div>
    ${v.noHelp ? `<div class="pw-sec pw-nope"><div class="pw-sh">${ico('ban')}Was nicht hilft</div><p>${esc(v.noHelp)}</p></div>` : ''}
    ${v.notes.length ? `<ul class="pw-notes">${v.notes.map(n => `<li>${esc(n)}</li>`).join('')}</ul>` : ''}`;
  const show = PW.all ? list : list.slice(0, PW_SHORT);
  $('#pwLn').textContent = list.length >= 50 ? 'Die letzten 50' : stCount(list.length, 'Runde', 'Runden');
  $('#pwRows').innerHTML = show.map(x => {
    const on = String(x.id) === PW.sel, w = x.verdict.key === 'vram' || x.verdict.key === 'ram';
    const [b] = pwMixHtml(x.shares);
    return `<button class="pw-r${on ? ' on' : ''}" type="button" data-pwr="${esc(String(x.id))}" aria-pressed="${on}" title="${esc(x.verdict.title)}">
      <span class="pw-rt">${esc(stDayName(x.start))} · ${stClock(x.start)}</span><span class="pw-rs${x.server ? '' : ' unk'}">${esc(x.server || 'Server nicht erkannt')}</span>
      <span class="pw-rk${w ? ' w' : ''}">${esc(x.verdict.short)}</span><span class="pw-mix pw-rb">${b}</span></button>`;
  }).join('') + (list.length > PW_SHORT ? `<div class="pw-more"><button class="btn sm gh" type="button" data-pwall>${PW.all ? 'Weniger zeigen' : `Alle ${list.length} zeigen`}</button></div>` : '');
}

/* ---------- actions ---------- */
async function pwToggle() {
  if (!PW.d || PW.d.unreadable) return;
  const on = !PW.d.on;
  try {
    PW.d = await api('perf.set', { on }); PW.live = PW.d.live || null;
    toast(on ? 'Engpass-Analyse an' : 'Engpass-Analyse aus', on ? 'OrbitTweak misst mit, solange FiveM läuft' : 'Es werden keine Leistungswerte mehr gelesen');
  } catch (e) { toast('Nicht umgeschaltet', e.message, false); }
  pwRender();
}

/** "Was hilft" leads to the place that does it, which lights up briefly. */
function pwShow(sel) {
  setTimeout(() => {
    const el = typeof sel === 'function' ? sel() : $(sel); if (!el) return;
    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    el.classList.remove('flash'); void el.offsetWidth; el.classList.add('flash');
  }, 160);
}
const PW_GO = {
  presets: () => { go('gfx'); pwShow('#presets'); },
  tuning: () => { go('gfx'); pwShow('#p-gfx .gtune'); },
  fps: () => { go('gfx'); pwShow('#fpsCard'); },
  tex: () => { go('gfx'); pwShow('#texCard'); },
  bgbrake: () => { goTweaks('fivem'); pwShow(() => $('#tgrid [data-id="bgbrake"]')?.closest('.tw')); },
  cleaner: () => { go('cleaner'); pwShow('#p-cleaner .lx-ram'); },
  autostart: () => go('autostart'),
  diag: () => go('diag'),
};
function pwGo(k) { (PW_GO[k] || (() => { }))(); }

/** From the Strg+K palette: the Spielzeit page, scrolled to the card. */
function pwOpen() {
  go('stats');
  const at = Date.now(), find = () => { const c = $('#pwCard'); if (c && c.isConnected) return pwShow(() => c); if (Date.now() - at < 3000) setTimeout(find, 120); };
  setTimeout(find, 60);
}

/* ---------- hooks ---------- */
{ const prev = PAGE_SHOW.stats; PAGE_SHOW.stats = () => { if (prev) prev(); pwLoad(); }; }

/** Each look while a round is measured; done = a round was just kept (or the game closed): everything fresh. */
EVENT_EXT.perf = d => {
  PW.live = d.live || null;
  if (PW.d && 'error' in d) PW.d.error = d.error;
  if (d.done || (PW.d && !PW.d.on)) { if (curPage === 'stats') pwLoad(); return; }
  if (curPage !== 'stats' || document.hidden || !PW.d) return;
  // FiveM in front: the page rests, the values come every 10 s instead of 2 s (for a second screen)
  if (REST.game && (PW.ev++ % 5)) return;
  if (pwModeOf() !== PW.mode) pwRender(); else if (PW.mode === 'live') { pwLiveFill(); pwSub('live'); }
};

/* ======================= MOCK (browser preview): the host's shapes and texts ======================= */
// The verdicts were made by Core/PerfWatchLogic.cs for these rounds (texts as the host gives them); a 32 GB PC with a
// 12 GB card. pwPreview('live' | 'warmup' | 'paused' | 'done' | 'empty' | 'error' | 'fail' | 'off' | 'nogpu') switches
// the preview to another state.
const PW_MOCK = HOST ? null : (() => {
  const V = {
    gpu: { key: 'gpu', short: 'Grafikkarte', title: 'Deine Grafikkarte war das Limit', text: 'In 62 % der Zeit lief sie am Anschlag, im Schnitt zu 93 %. Das ist bei hohen Einstellungen ganz normal: Sie gibt alles, und mehr FPS gibt es nur, wenn sie weniger zu rechnen hat.',
      help: [{ text: 'Ein Preset tiefer, etwa „Ausgewogen“ statt „Kino“, oder einzeln runter: MSAA, Gras, Schatten und Post-FX kosten in GTA am meisten.', go: 'presets', label: 'Zu den Presets' },
        { text: 'Mit NVIDIA-Karte: Ein FPS-Limit knapp unter deinen üblichen FPS nimmt ihr die Spitzen. Das bringt gleichmäßigere Bilder und weniger Hitze, aber keine FPS.', go: 'fps', label: 'Zum FPS-Limit' }],
      noHelp: 'RAM freigeben, der Cleaner und Windows-Tweaks ändern daran nichts: Die Karte rechnet schon so schnell sie kann.', notes: [] },
    vram: { key: 'vram', short: 'VRAM voll', title: 'Dein Grafikspeicher war voll', text: 'In 31 % der Zeit war er zu 95 % oder mehr belegt (Spitze 11,9 GB von 12,0 GB). Dann muss Windows Texturen in den Arbeitsspeicher auslagern, das gibt oft Nachladeruckler und matschige Texturen.',
      help: [{ text: 'Texturbudget eine Stufe runter: FiveM hält dann weniger Texturen im Grafikspeicher.', go: 'tex', label: 'Zum Texturbudget' }, { text: 'Texturen im Feintuning von „Sehr hoch“ auf „Hoch“.', go: 'tuning', label: 'Zum Feintuning' },
        { text: 'Schließ nebenbei Programme, die Grafikspeicher belegen: Browser mit Videos, NVIDIA Broadcast, Aufnahme-Tools.', go: null, label: null }],
      noHelp: '„RAM freigeben“ im Cleaner leert den Grafikspeicher nicht.', notes: ['Daneben: Grafikkarte am Limit 60 %, Prozessor 17 %, gedeckelt 8 %, gemischt 15 %.'] },
    cpu: { key: 'cpu', short: 'Prozessor', title: 'Dein Prozessor hat gebremst', text: 'In 61 % der Zeit hat die Grafikkarte auf ihn gewartet (Grafikkarte im Schnitt 72 %, stärkster Kern 93 %). In FiveM ist das oft ein einzelner Kern, den Skripte, viele Spieler und Autos in der Nähe auslasten.',
      help: [{ text: '„Hintergrund-Apps ausbremsen“ stellt Browser und Launcher hinter das Spiel. Das hilft genau dann, wenn der Prozessor voll ist.', go: 'bgbrake', label: 'Zum Tweak' },
        { text: 'Bevölkerungsdichte und Distanz-Skalierung im Feintuning runter entlasten den Prozessor etwas. Auf vielen Servern bestimmt der Server die Bevölkerung aber selbst.', go: 'tuning', label: 'Zum Feintuning' },
        { text: 'Schließ, was nebenbei rechnet: Videos im Browser, Streams, Aufnahmen.', go: null, label: null }],
      noHelp: 'Niedrigere Auflösung, weniger Kantenglättung oder kleinere Texturen bringen hier kaum FPS, sie entlasten nur die Grafikkarte, die ohnehin wartet. Viele Skripte und Spieler auf vollen Servern kosten Prozessorzeit, daran kann kein Tool etwas ändern.', notes: [] },
    cap: { key: 'cap', short: 'Gedeckelt', title: 'Deine FPS waren gedeckelt', text: 'In 79 % der Zeit hatten Grafikkarte (im Schnitt 61 %) und Prozessor Luft. Meist hält dann ein FPS-Limit oder V-Sync die Bildrate fest, und das ist gut so: gleichmäßige Bilder, weniger Hitze und Lüfterlärm.',
      help: [{ text: 'Willst du mehr FPS, setz das Limit höher oder schalte V-Sync in den GTA-Einstellungen aus. Ohne V-Sync kann das Bild reißen.', go: 'fps', label: 'Zum FPS-Limit' }],
      noHelp: 'Tweaks, der Cleaner und ein kleineres Preset bringen hier keine FPS, solange das Limit greift.', notes: ['Hast du weder ein Limit noch V-Sync an, bremst eher ein einzelner Prozessorkern, den Windows zwischen den Kernen hin- und herschiebt. Auch Menüs und Ladebildschirme sehen so aus.'] },
    mixed: { key: 'mixed', short: 'Gemischt', title: 'Kein klarer Engpass', text: 'Mal war es die Grafikkarte, mal der Prozessor, keiner von beiden lange genug für ein klares Urteil (Grafikkarte am Limit 27 %, Prozessor 24 %, gedeckelt 16 %, gemischt 33 %).',
      help: [{ text: 'Hier gibt es keinen einzelnen Hebel. Ruckelt es trotzdem, findet die Diagnose oft die Ursache, etwa einen alten Treiber oder einen vollen Cache.', go: 'diag', label: 'Zur Diagnose' }],
      noHelp: 'Einzelne Tweaks bringen hier selten spürbar etwas.', notes: [] },
    nogpu: { key: 'nogpu', short: 'Ohne GPU-Werte', title: 'Ohne Werte der Grafikkarte kein Urteil', text: 'Windows hat für deine Grafikkarte keine Auslastung geliefert, dafür braucht es einen aktuellen Treiber. Gemessen wurde trotzdem: stärkster Prozessorkern im Schnitt 78 %, Arbeitsspeicher 47 %.',
      help: [{ text: 'Grafiktreiber aktualisieren, dann misst OrbitTweak ab der nächsten Runde auch die Grafikkarte.', go: 'diag', label: 'Zur Diagnose' }], noHelp: null, notes: [] },
  };
  // [verdict, shares gpu/cpu/cap/mixed/unknown, vram full, avg gpu/core/cpu/vram/ram, peak vram GB]
  const R = [
    ['gpu', [.621, .179, .079, .121, 0], .021, [93.4, 71, 38, 78, 47], 11.4],
    ['vram', [.6, .173, .08, .147, 0], .313, [91, 69, 36, 93, 52], 11.9],
    ['cpu', [.117, .606, .089, .189, 0], 0, [72, 93, 52, 66, 49], 9.2],
    ['cap', [.05, .067, .789, .094, 0], 0, [61, 58, 27, 62, 44], 8.7],
    ['mixed', [.274, .237, .158, .332, 0], .032, [84, 82, 41, 80, 50], 11.6],
    ['gpu', [.70, .093, .08, .127, 0], 0, [94.6, 68, 35, 74, 46], 10.6],
  ];
  const past = (ST_MOCK ? ST_MOCK.rounds : []).slice().sort((a, b) => b.start - a.start).slice(0, R.length);
  const rounds = past.map((p, i) => {
    const [k, s, vf, avg, pv] = R[i];
    return { id: p.id, start: p.start, end: p.end, secs: Math.round((p.end - p.start) / 1000 * .94), server: p.server || null,
      shares: { gpu: s[0], cpu: s[1], cap: s[2], mixed: s[3], unknown: s[4] }, vramFull: vf, ramTight: 0,
      avg: { gpu: avg[0], core: avg[1], cpu: avg[2], vram: avg[3], ram: avg[4] }, peak: { gpu: 100, core: 100, cpu: Math.min(100, avg[2] + 31), vram: Math.min(100, pv / 12 * 100), ram: 58 },
      vramGb: 12, peakVramGb: pv, ramGb: 31.9, peakRamGb: 18.6, own: true, verdict: V[k] };
  });
  return { state: 'live', on: true, rounds, V, recent: [], n: { gpu: 0, cpu: 0, cap: 0, mixed: 0, unknown: 0 }, secs: 0, last: null, rnd: seeded(3107), fail: false, gone: null };
})();

/* the same thresholds as Core/PerfWatchLogic.cs Classify, for the made-up looks */
function pwMockKind(s) {
  if (s.gpu == null) return 'unknown';
  if (s.gpu >= 95) return 'gpu';
  if (s.gpu < 85 && (s.core >= 90 || s.cpu >= 90)) return 'cpu';
  if (s.gpu < 85 && s.core < 80 && s.cpu < 80) return 'cap';
  return 'mixed';
}
const PW_NOW = {
  gpu: ['Grafikkarte am Limit', 'Sie läuft am Anschlag und bestimmt gerade deine FPS.'], cpu: ['Prozessor bremst', 'Die Grafikkarte wartet auf den Prozessor, meist auf einen einzelnen Kern.'],
  cap: ['Gedeckelt', 'Beide haben Luft: Ein FPS-Limit oder V-Sync hält die Bildrate fest.'], mixed: ['Kein klarer Engpass', 'Grafikkarte und Prozessor arbeiten, keiner liegt am Anschlag.'],
  unknown: ['Ohne Werte der Grafikkarte', 'Windows liefert für deine Grafikkarte gerade keine Auslastung.'],
  warmup: ['Messung läuft an', 'Die ersten Werte kommen in ein paar Sekunden.'], paused: ['Pausiert', 'FiveM ist gerade nicht vorne. Gemessen wird nur, solange du spielst.'],
};
/** One made-up look of a GPU-bound evening on a 12 GB card, the round started 72 minutes ago (as Spielzeit's mock). */
function pwMockLook() {
  const M = PW_MOCK, r = M.rnd, nogpu = M.state === 'nogpu';
  if (!M.secs) {   // the round so far: mostly at the card's limit
    M.secs = 72 * 60 * .93; Object.assign(M.n, { gpu: 1260, cpu: 330, cap: 150, mixed: 260, unknown: 0 }); M.recent = ['gpu', 'gpu', 'gpu', 'gpu'];
    if (nogpu) { Object.assign(M.n, { gpu: 0, cpu: 0, cap: 0, mixed: 0, unknown: 2000 }); M.recent = []; }
  }
  const spike = r() < .2;
  const s = { gpu: nogpu ? null : Math.round(spike ? 78 + r() * 8 : 95 + r() * 4), own: !nogpu, core: Math.round(spike ? 90 + r() * 6 : 58 + r() * 16), cpu: Math.round(31 + r() * 12),
    vramGb: 12, vramUsedGb: Math.round((7.6 + r() * .5) * 10) / 10, ramGb: 31.9, ramUsedGb: Math.round((14.4 + r() * .4) * 10) / 10 };
  s.vram = Math.round(s.vramUsedGb / s.vramGb * 100); s.ram = Math.round(s.ramUsedGb / s.ramGb * 100);
  const k = pwMockKind(s);
  M.n[k]++; M.secs += 2; M.recent.push(k); if (M.recent.length > 5) M.recent.shift(); M.last = s;
  return s;
}
function pwMockLive() {
  const M = PW_MOCK;
  if (!['live', 'warmup', 'paused', 'nogpu'].includes(M.state) || !M.on || !ST_MOCK || !ST_MOCK.open) return null;
  const state = M.state === 'nogpu' ? 'live' : M.state;
  if (state === 'live') pwMockLook();
  const s = state === 'warmup' ? null : M.last || pwMockLook();
  let kind = null;
  if (state === 'live') { const c = {}; let best = -1; for (let i = M.recent.length - 1; i >= 0; i--) { const x = M.recent[i]; c[x] = (c[x] || 0) + 1; } for (let i = M.recent.length - 1; i >= 0; i--) if (c[M.recent[i]] > best) { best = c[M.recent[i]]; kind = M.recent[i]; } }
  const tot = Object.values(M.n).reduce((a, b) => a + b, 0), sh = k => Math.round(M.n[k] / tot * 1000) / 1000;
  const [title, text] = PW_NOW[state === 'live' ? kind : state];
  return { round: ST_MOCK.open.id, state, kind, title, text,
    now: s ? { gpu: s.gpu, own: s.own, core: s.core, cpu: s.cpu, vram: s.vram, vramGb: s.vramGb, vramUsedGb: s.vramUsedGb, ram: s.ram, ramGb: s.ramGb, ramUsedGb: s.ramUsedGb, vramFull: s.vram >= 95, ramTight: s.ram >= 90 } : null,
    since: state === 'warmup' ? null : ST_MOCK.open.start, secs: state === 'warmup' ? 0 : Math.round(M.secs), shares: state === 'warmup' ? null : { gpu: sh('gpu'), cpu: sh('cpu'), cap: sh('cap'), mixed: sh('mixed'), unknown: sh('unknown') } };
}
function pwMockGet() {
  const M = PW_MOCK;
  if (M.state === 'fail') throw new Error('Gesperrt: Bitte zuerst deinen Key einlösen');
  // like the host: rounds removed from the Spielzeit history are gone here as well
  const known = new Set([...(ST_MOCK ? ST_MOCK.rounds : []).map(r => r.id), ST_MOCK && ST_MOCK.open ? ST_MOCK.open.id : null]);
  const kept = M.rounds.filter(r => known.has(r.id));
  const rounds = M.state === 'empty' || M.state === 'error' ? [] : !kept.length ? kept : M.state === 'nogpu' ? [{ ...kept[0], shares: { gpu: 0, cpu: 0, cap: 0, mixed: 0, unknown: 1 }, avg: { gpu: null, core: 78, cpu: 41, vram: 70, ram: 47 }, own: false, verdict: M.V.nogpu }, ...kept.slice(1)] : kept;
  return { on: M.on, error: M.state === 'error' ? 'Windows liefert gerade keine Leistungswerte. Meist sind die Leistungsindikatoren beschädigt: „lodctr /R“ in einer Eingabeaufforderung als Administrator repariert sie.' : null,
    unreadable: false, live: M.on ? pwMockLive() : null, rounds };
}
/** Preview another state of the card (for working on the page). */
function pwPreview(state) { if ((state === 'nogpu') !== (PW_MOCK.state === 'nogpu')) PW_MOCK.secs = 0; PW_MOCK.state = state; PW_MOCK.on = state !== 'off'; PW.view = 'now'; PW.sel = null; return pwLoad(); }
window.pwPreview = pwPreview;

if (!HOST) {
  MOCK_EXT['perf.get'] = async () => pwMockGet();
  MOCK_EXT['perf.set'] = async a => { PW_MOCK.on = !!a.on; if (PW_MOCK.on && PW_MOCK.state === 'off') PW_MOCK.state = 'live'; return pwMockGet(); };
  // the host sends a look every 2 s while a round is measured
  setInterval(() => { if (curPage === 'stats' && PW_MOCK.on && ['live', 'paused', 'nogpu'].includes(PW_MOCK.state)) onEvent('perf', { live: pwMockLive(), done: false }); }, 2000);
}
