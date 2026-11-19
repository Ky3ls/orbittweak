'use strict';
/* 2.30 – Andere Spiele: tweaks for Fortnite, Counter-Strike 2 and Rainbow Six Siege on one page (#p-og).
   Host: MainWindow.OtherGames.cs, Core/OtherGames.cs (models), Core/Games/*.cs (one file per game).
   og.games → { games: [GameInfo] }
   og.items { game } → { game: GameInfo, items: [GameItem] }
   og.set { game, id, value } → { ok, value }          (a refused change rejects with a German message)
   og.apply { game } → { ok, applied, failed: [{ id, title, error }] }
   og.revert { game } → { ok, reverted }
   GameInfo { id, name, installed, path, running, configFound, configPath, note, antiCheat }
   GameItem { id, title, sum, desc, kind: 'switch'|'choice'|'number', value, options: [{ v, t }], min, max, step, unit,
              rec, recValue, effect: 'fps'|'latency'|'comfort'|'stability', source, needsClosed, available, why, changed } */

/** Browser preview: each game file (og-fortnite.js, og-cs2.js, og-r6.js) puts its mock here: OG_MOCK[id] = { info, items }.
 *  The order of the games on the page is the host's (og.games). */
const OG_MOCK = {};
const OG_ORDER = ['fortnite', 'cs2', 'r6'];

/* =====================================================================
   The page: a switcher for the games, the chosen game's head (state, note, paths, anti-cheat and the one main action
   "Empfohlene anwenden"), then its settings as rows. Styles: og.css (Luxe tokens of uhq.css).
   Every change goes to the host one at a time (one game, one settings file) and the list is read again afterwards,
   because values can depend on each other (Fortnite's Schatten follow the Rendermodus).
   ===================================================================== */

/** The page's own words per game: the monogram on its tile (no logos), a short name for running text and where
 *  OrbitTweak looks for it (said when it is not found; the same places as Core/Games/<Game>.cs). A game without an
 *  entry gets its initials, its name and the host's note. */
const OG_GAME = {
  fortnite: { mark: 'F', short: 'Fortnite',
    look: 'OrbitTweak sucht Fortnite über die Daten des Epic Games Launchers, Fortnites eigenes Protokoll und den Ordner Programme\\Epic Games\\Fortnite. Seine Einstellungen legt Fortnite beim ersten Start unter AppData\\Local\\FortniteGame an.' },
  cs2: { mark: 'CS', short: 'CS2',
    look: 'OrbitTweak sucht CS2 in allen Bibliotheken deines Steam-Ordners und seine Einstellungen im userdata-Ordner deines Steam-Kontos. Die legt CS2 beim ersten Start an.' },
  r6: { mark: 'R6', short: 'Siege',
    look: 'OrbitTweak sucht Siege bei Ubisoft Connect, Steam und Epic und seine Einstellungen unter Dokumente\\My Games\\Rainbow Six - Siege. Die legt Siege beim ersten Start an.' },
};
/** What an item is good for: the tag on the row and its tooltip. */
const OG_EFFECT = {
  fps: ['FPS', 'Wofür es gut ist: mehr Bilder pro Sekunde'],
  latency: ['Latenz', 'Wofür es gut ist: weniger Verzögerung zwischen Eingabe und Bild'],
  comfort: ['Komfort', 'Wofür es gut ist: Übersicht und Bedienung, mehr FPS bringt es nicht'],
  stability: ['Stabilität', 'Wofür es gut ist: gleichmäßigere Bildzeiten und weniger Aussetzer'],
};
const OG_KEY = 'orbitOgGame';   // the chosen game, for this session

const OG = {
  games: null, err: '', at: 0, loading: null,   // og.games
  game: null,                                    // the chosen game's id
  data: {},                                      // per game: { info, items, raw } from og.items, or { err }
  seq: {},                                       // per game: the newest og.items read (older answers are dropped)
  pend: new Map(),                               // 'game|id' → the value on its way to the host (the row shows it and waits)
  chain: Promise.resolve(),                      // og.set, og.apply and og.revert one after the other
  bulk: '', bulkGame: '',                        // 'apply' or 'revert' while one runs
  open: new Set(),                               // 'game|id': rows showing their details
  ac: false,                                     // the full anti-cheat text is shown
  fx: '',                                        // the row whose details were just opened (only a click animates)
  poll: 0,
};
try { OG.game = sessionStorage.getItem(OG_KEY) || null; } catch { }

const ogSame = (a, b) => JSON.stringify(a) === JSON.stringify(b);
const ogMsg = e => (e && e.message) || 'Unbekannter Fehler';
const ogNames = l => new Intl.ListFormat('de', { type: 'conjunction' }).format(l);
const ogDom = s => String(s).replace(/[^\w-]/g, '_');
const ogCur = () => OG.data[OG.game] && OG.data[OG.game].items ? OG.data[OG.game] : null;
const ogInfo = () => (ogCur() && ogCur().info) || (OG.games || []).find(g => g.id === OG.game) || null;
const ogShort = g => (OG_GAME[g && g.id] || {}).short || (g && g.name) || 'das Spiel';
/** Nothing of the game was found: no install folder and no settings file (a settings file alone still counts). */
const ogMissing = g => !!g && !g.installed && !g.configFound;
/** The game rewrites its settings file on exit: while it runs, such an item waits. */
const ogLocked = (g, it) => !!(g && g.running && it.needsClosed);
const ogPending = game => [...OG.pend.keys()].some(k => k.startsWith(game + '|'));
const ogVal = (game, it) => OG.pend.has(game + '|' + it.id) ? OG.pend.get(game + '|' + it.id) : it.value;
/** What "Empfohlene anwenden" sets: the host's rule (MainWindow.OtherGames.cs og.apply). */
const ogOpenRecs = items => items.filter(i => i.rec && i.available && !ogSame(i.value, i.recValue));
const ogNum = v => String(v).replace('.', ',');

function ogRemember(id) { OG.game = id; try { sessionStorage.setItem(OG_KEY, id); } catch { } }
function ogMark(g, cls = '') {
  const m = (OG_GAME[g.id] || {}).mark || String(g.name || '?').split(/\s+/).map(w => (w.match(/[\p{L}\p{N}]/u) || [''])[0]).join('').slice(0, 2).toUpperCase();
  return `<span class="og-mk${cls}" data-ogmk="${esc(g.id)}" aria-hidden="true">${esc(m)}</span>`;
}
/** A value as the page says it: An/Aus, the option's label, the number with its unit. */
function ogShown(it, v) {
  if (it.kind === 'switch') return v ? 'An' : 'Aus';
  if (it.kind === 'choice') { const o = (it.options || []).find(x => x.v === String(v)); return o ? o.t : String(v ?? '–'); }
  if (it.kind === 'number') return v == null || v === '' ? '–' : ogNum(v) + (it.unit ? ' ' + it.unit : '');
  return String(v ?? '–');
}
/** state, chip text, the shorter text on the tile */
function ogState(g) {
  if (g.running) return ['run', 'Läuft gerade', 'Läuft gerade'];
  if (ogMissing(g)) return ['miss', 'Nicht gefunden', 'Nicht gefunden'];
  if (!g.configFound) return ['cfg', 'Einstellungen noch nicht angelegt', 'Einstellungen fehlen noch'];
  if (!g.installed) return ['part', 'Nur Einstellungen gefunden', 'Nur Einstellungen gefunden'];
  return ['ok', 'Installiert', 'Installiert'];
}
/** The anti-cheat in a few words: its first sentence, else the name before the colon (the full text stays one click away). */
function ogAcShort(s) {
  s = String(s || '').trim();
  const first = (s.match(/^.*?[.!?](?=\s|$)/) || [s])[0];
  if (first.length <= 80) return first;
  const name = s.split(':')[0].trim();
  if (name.length < first.length && name.length <= 60) return name;
  return first.slice(0, 78).replace(/\s+\S*$/, '') + ' …';
}
/** A path that keeps its end: the folders shorten in the middle, the last part stays readable. */
function ogPath(p) {
  const s = String(p), i = s.lastIndexOf('\\');
  return `<span class="og-path" title="${esc(s)}"><span>${esc(i > 0 ? s.slice(0, i) : s)}</span>${i > 0 ? `<span>${esc(s.slice(i))}</span>` : ''}</span>`;
}
/** One strip above the list when something holds the rows: the running game or settings it has not made yet. */
function ogBanner(g, items) {
  if (!g || ogMissing(g) || !items) return null;
  const av = items.filter(i => i.available);
  if (g.running && av.some(i => i.needsClosed)) {
    const free = av.filter(i => !i.needsClosed);
    return { kind: 'run', icon: 'padlock',
      text: /läuft/i.test(g.note) ? g.note : `${g.name} läuft gerade. Schließ es ganz, bevor du hier etwas änderst, sonst überschreibt es die Werte beim Beenden.`,
      more: free.length === 1 ? `„${free[0].title}“ geht auch jetzt.` : free.length > 1 ? `${free.length} Einstellungen gehen auch jetzt.` : '' };
  }
  if (!g.configFound) {
    // the host's own words for it (the rows that wait for the file say the same, so they do not repeat it)
    const w = items.find(i => !i.available && i.needsClosed && i.why);
    return { kind: 'cfg', icon: 'info', text: (w && w.why) || g.note || `Starte ${g.name} einmal, dann legt es seine Einstellungen an.`, more: '' };
  }
  return null;
}

/* ---------- drawing ---------- */
/** Writes html into el only when it changed, so focus, an open list and running animations stay. */
function ogPut(el, html) { if (el && el._h !== html) { el._h = html; el.innerHTML = html; } }
/** The rows: when the same rows are there, only the ones that changed are replaced. */
function ogPutRows(host, rows) {
  const kids = [...host.children];
  if (host._h != null || kids.length !== rows.length || kids.some((c, i) => c.dataset.ogi !== rows[i][0])) {
    host._h = null; host.innerHTML = rows.map(r => r[1]).join('');
    [...host.children].forEach((c, i) => { c._h = rows[i][1]; });
    return;
  }
  rows.forEach(([, h], i) => {
    if (kids[i]._h === h) return;
    const t = document.createElement('template'); t.innerHTML = h;
    const n = t.content.firstElementChild; n._h = h; kids[i].replaceWith(n);
  });
}

function ogFrame() {
  $('#p-og').innerHTML = `
  <div class="ph">
    <div><div class="kicker">Andere Spiele</div><h1>Tweaks für <span class="grad">andere Spiele</span></h1>
    <p class="pd">OrbitTweak ändert nur, was das Spiel selbst in seinem Menü speichert, und Windows-Einstellungen, nie Spieldateien. Alles lässt sich zurücksetzen.</p></div>
    <div class="ph-r"><button class="btn og-rl" id="ogReload" type="button" data-ogre data-ogk="reload">${ico('refresh')}Neu prüfen</button></div>
  </div>
  <div class="og-games" id="ogGames" role="tablist" aria-label="Spiel"></div>
  <div id="ogPanel" role="tabpanel">
    <div class="card og-head" id="ogHead" hidden><div class="og-main" id="ogMain"></div><div class="og-go" id="ogGo"></div></div>
    <div id="ogBan"></div>
    <div class="card og-list">
      <div class="og-lh"><b>Einstellungen</b><p id="ogLhP"></p></div>
      <div id="ogRows"></div>
    </div>
  </div>`;
}

function ogTabs() {
  const l = OG.games || [], sel = l.some(g => g.id === OG.game) ? OG.game : l[0] && l[0].id;
  // before the first answer: three quiet placeholders, so nothing jumps when the games arrive
  if (!OG.games) return ogPut($('#ogGames'), OG.err ? '' : OG_ORDER.map(() => '<div class="og-g og-sk" aria-hidden="true"><span class="og-mk"></span><span class="og-gt"><i></i><i></i></span></div>').join(''));
  ogPut($('#ogGames'), l.map(g => {
    const on = g.id === OG.game, [k, , t] = ogState(g);
    return `<button class="og-g${on ? ' on' : ''}" type="button" role="tab" id="ogTab-${ogDom(g.id)}" aria-selected="${on}" aria-controls="ogPanel" tabindex="${g.id === sel ? 0 : -1}" data-ogg="${esc(g.id)}" data-ogk="tab:${esc(g.id)}">`
      + `${ogMark(g)}<span class="og-gt"><b>${esc(g.name)}</b><span class="og-gs ${k}"><i></i>${esc(t)}</span></span></button>`;
  }).join(''));
  const p = $('#ogPanel'); if (p && OG.game) p.setAttribute('aria-labelledby', 'ogTab-' + ogDom(OG.game));
}

/** The one main action of the shown game and the quiet way back: [html, all set]. */
function ogGo(g, items) {
  const name = ogShort(g);
  if (!items) {
    const err = OG.data[g.id] && OG.data[g.id].err;
    return [`<div class="og-gx"><span class="og-gk">Empfohlen für ${esc(name)}</span><p class="og-gp">${err ? 'Die Einstellungen sind gerade nicht gelesen.' : 'Wird gelesen…'}</p></div>`, false];
  }
  const recs = items.filter(i => i.rec && i.available), open = ogOpenRecs(items), changed = items.filter(i => i.changed);
  const busy = !!OG.bulk || ogPending(g.id), miss = ogMissing(g);
  const runApply = g.running && open.some(i => i.needsClosed), runRevert = g.running && changed.some(i => i.needsClosed);
  const canApply = !miss && open.length > 0 && !runApply && !busy, canRevert = changed.length > 0 && !runRevert && !busy;
  const names = open.length <= 2 ? `<b>${esc(ogNames(open.map(i => i.title)))}</b>`
    : `<b>${esc(open.slice(0, 2).map(i => i.title).join(', '))}</b> und ${open.length - 2} weitere`;
  let line;
  if (miss) line = `Sobald OrbitTweak ${esc(name)} findet, stehen hier die Empfehlungen.`;
  else if (!recs.length && !g.configFound) line = `Sobald ${esc(name)} seine Einstellungen angelegt hat, stehen hier die Empfehlungen.`;
  else if (!recs.length) line = 'Auf diesem PC gibt es hier gerade nichts zu empfehlen.';
  else if (!open.length) line = `<b>Alles gesetzt</b> Die ${recs.length === 1 ? 'empfohlene Einstellung steht' : 'empfohlenen Einstellungen stehen'} schon richtig.`;
  else if (runApply) line = `${names} ${open.length === 1 ? 'kann' : 'können'} erst nach dem Schließen von ${esc(name)} gesetzt werden.`;
  else line = `${names} ${open.length === 1 ? 'steht' : 'stehen'} noch nicht auf dem empfohlenen Wert.`;
  const applyTip = miss ? `${g.name} wurde nicht gefunden` : !open.length ? 'Nichts offen' : runApply ? `Schließ ${g.name} zuerst` : `Setzt ${open.length === 1 ? 'die offene Empfehlung' : `die ${open.length} offenen Empfehlungen`}`;
  const revTip = !changed.length ? 'OrbitTweak hat hier nichts geändert' : runRevert ? `Schließ ${g.name} zuerst` : `Bringt ${changed.length === 1 ? 'den Wert' : `die ${changed.length} Werte`} von vor OrbitTweak zurück`;
  const applying = OG.bulk === 'apply' && OG.bulkGame === g.id, reverting = OG.bulk === 'revert' && OG.bulkGame === g.id;
  const done = !miss && recs.length > 0 && !open.length;
  return [`<div class="og-gx"><span class="og-gk">${done ? `<i class="og-okc">${ico('check')}</i>` : ''}Empfohlen für ${esc(name)}</span><p class="og-gp">${line}</p></div>
    <button class="btn p og-ap" id="ogApply" type="button" data-ogk="apply" title="${esc(applyTip)}" aria-label="${applying ? 'Wird gesetzt' : `Empfohlene anwenden${open.length ? `, ${open.length} offen` : ''}`}"${canApply ? '' : ' aria-disabled="true"'}>${applying ? '<i class="og-spin"></i>' : ico('zap')}${applying ? 'Wird gesetzt…' : 'Empfohlene anwenden'}${open.length && !applying ? `<i class="og-cnt" aria-hidden="true">${open.length}</i>` : ''}</button>
    <button class="btn sm og-rv" id="ogRevert" type="button" data-ogk="revert" title="${esc(revTip)}" aria-label="${reverting ? 'Wird zurückgesetzt' : `Zurücksetzen${changed.length ? `, ${changed.length} geändert` : ''}`}"${canRevert ? '' : ' aria-disabled="true"'}>${reverting ? '<i class="og-spin"></i>' : ico('undo')}${reverting ? 'Wird zurückgesetzt…' : 'Zurücksetzen'}${changed.length && !reverting ? `<i class="og-cnt q" aria-hidden="true">${changed.length}</i>` : ''}</button>`, done];
}

function ogHead() {
  const el = $('#ogHead'), g = ogInfo();
  if (!el) return;
  el.hidden = !g;
  if (!g) return;
  const cur = ogCur(), items = cur && cur.info.id === g.id ? cur.items : null, ban = ogBanner(g, items);
  const [k, chip] = ogState(g);
  const note = g.note && !(ban && ban.text === g.note) ? `<p class="og-note">${esc(g.note)}</p>` : '';
  const none = t => `<span class="og-none">${t}</span>`;
  const facts = [];
  if (g.path || !ogMissing(g)) facts.push(['Spielordner', g.path ? ogPath(g.path) : none('Nicht gefunden')]);
  if (g.configPath || !ogMissing(g)) facts.push(['Einstellungen', g.configPath ? ogPath(g.configPath) + (g.configFound ? '' : none('noch nicht angelegt')) : none('Noch nicht angelegt')]);
  if (g.antiCheat) facts.push(['Anti-Cheat', `<span class="og-acs" title="${esc(g.antiCheat)}">${esc(ogAcShort(g.antiCheat))}</span>`
    + `<button class="og-more og-acb" id="ogAcb" type="button" data-ogk="ac" aria-expanded="${OG.ac}" aria-controls="ogAcx">${ico('chev')}<span>${OG.ac ? 'Weniger' : 'Was OrbitTweak ändert'}</span></button>`]);
  ogPut($('#ogMain'), `<div class="og-id">${ogMark(g, ' lg')}<div class="og-idt"><div class="og-nm"><h2>${esc(g.name)}</h2><span class="og-st ${k}"><i></i>${esc(chip)}</span></div>${note}</div></div>
      ${facts.length ? `<dl class="og-facts">${facts.map(([t, d]) => `<div><dt>${t}</dt><dd>${d}</dd></div>`).join('')}</dl>` : ''}
      ${g.antiCheat ? `<p class="og-acx${OG.fx === 'ac' ? ' og-fx' : ''}" id="ogAcx"${OG.ac ? '' : ' hidden'}>${ico('shield')}<span>${esc(g.antiCheat)}</span></p>` : ''}`);
  const [go, done] = ogGo(g, items), box = $('#ogGo');
  box.classList.toggle('done', done);
  ogPut(box, go);
}

function ogEmpty(icon, title, text, cls = '', extra = '') {
  return `<div class="og-empty ${cls}">${ico(icon)}<b>${esc(title)}</b><span>${esc(text)}</span>${extra}</div>`;
}
const OG_RETRY = `<button class="btn sm" type="button" data-ogre data-ogk="retry">${ico('refresh')}Noch mal versuchen</button>`;
const OG_RECHECK = `<button class="btn sm" type="button" data-ogre data-ogk="recheck">${ico('refresh')}Neu prüfen</button>`;

/** The control of a row: switch, segmented choice (up to four short options), list, or a number with its unit. */
function ogCtl(g, it, v, off, busy) {
  const lab = esc(it.title), id = esc(it.id);
  const tip = off ? ` title="${esc(it.available ? `Schließ ${g.name} zuerst` : it.why || 'Gerade nicht verfügbar')}"` : '';
  const dis = off || busy || OG.bulk ? ' aria-disabled="true"' : '', tab = off ? ' tabindex="-1"' : '';
  if (it.kind === 'switch')
    return `<div class="tg${v ? ' on' : ''}" role="switch" tabindex="${off ? -1 : 0}" aria-checked="${!!v}" aria-label="${lab}" data-ogsw="${id}" data-ogk="sw:${id}"${dis}${tip}></div>`;
  if (it.kind === 'choice') {
    const o = it.options || [], cur = String(v ?? '');
    const seg = o.length >= 2 && o.length <= 4 && o.every(x => x.t.length <= 18) && o.reduce((n, x) => n + x.t.length, 0) <= 36;
    if (seg) return `<div class="seg og-seg" role="group" aria-label="${lab}"${tip}>${o.map(x => `<button type="button" class="${cur === x.v ? 'on' : ''}" aria-pressed="${cur === x.v}" data-ogch="${id}" data-ogv="${esc(x.v)}" data-ogk="ch:${id}:${esc(x.v)}"${dis}${tab}>${esc(x.t)}</button>`).join('')}</div>`;
    const known = o.some(x => x.v === cur);
    return `<select class="in og-sel" aria-label="${lab}" data-ogsel="${id}" data-ogk="sel:${id}"${off ? ' disabled' : ''}${busy || OG.bulk ? ' aria-disabled="true"' : ''}${tip}>`
      + (known ? '' : `<option value="" selected disabled>${cur ? `Eigener Wert (${esc(cur)})` : '–'}</option>`)
      + o.map(x => `<option value="${esc(x.v)}"${cur === x.v ? ' selected' : ''}>${esc(x.t)}</option>`).join('') + '</select>';
  }
  if (it.kind === 'number') {
    const range = it.min != null && it.max != null ? `, ${ogNum(it.min)} bis ${ogNum(it.max)}${it.unit ? ' ' + it.unit : ''}` : '';
    return `<label class="og-num${off ? ' off' : ''}"${tip}><input type="text" inputmode="decimal" role="spinbutton" spellcheck="false" autocomplete="off" placeholder="–" value="${esc(v ?? '')}"`
      + ` data-ognum="${id}" data-ogk="num:${id}" aria-label="${lab}${esc(range)}"${it.min != null ? ` aria-valuemin="${it.min}"` : ''}${it.max != null ? ` aria-valuemax="${it.max}"` : ''}${v != null && v !== '' ? ` aria-valuenow="${esc(v)}"` : ''}`
      + `${off ? ' disabled' : ''}${busy || OG.bulk ? ' readonly' : ''}>${it.unit ? `<span>${esc(it.unit)}</span>` : ''}</label>`;
  }
  return '';
}

function ogRow(g, it, ban) {
  const key = g.id + '|' + it.id, v = ogVal(g.id, it), busy = OG.pend.has(key);
  const na = !it.available, locked = !na && ogLocked(g, it), open = OG.open.has(key), did = `ogd-${ogDom(g.id)}-${ogDom(it.id)}`;
  const eff = OG_EFFECT[it.effect];
  let rec = '';
  if (it.rec && !na && it.recValue != null) {
    const want = ogShown(it, it.recValue);
    rec = ogSame(v, it.recValue)
      ? `<span class="og-ok" title="Steht auf dem empfohlenen Wert (${esc(want)})">${ico('check')}<span class="og-sr">Empfohlener Wert gesetzt</span></span>`
      : `<span class="og-rec" title="„Empfohlene anwenden“ stellt ${esc(want)} ein">Empfohlen: ${esc(want)}</span>`;
  }
  const chg = it.changed ? '<span class="og-chg" title="Von OrbitTweak geändert: „Zurücksetzen“ bringt den Wert von vorher zurück">Geändert</span>' : '';
  const why = na && it.why && !(ban && ban.text === it.why) ? `<small class="og-why">${esc(it.why)}</small>` : '';
  const more = it.desc ? ` <button class="og-more" type="button" data-ogmore="${esc(it.id)}" data-ogk="more:${esc(it.id)}" aria-expanded="${open}" aria-controls="${did}">${ico('chev')}<span>${open ? 'Weniger' : 'Details'}</span></button>` : '';
  const src = it.source ? `<p class="og-src">${ico('og-book')}<span><b>Quelle</b> ${esc(it.source)}</span></p>` : '';
  return [key, `<div class="og-row${na ? ' na' : ''}${locked ? ' lk' : ''}${busy ? ' busy' : ''}${open ? ' open' : ''}${open && OG.fx === key ? ' og-fx' : ''}" data-ogi="${esc(key)}">`
    + `<div class="og-tx"><div class="og-n"><b>${esc(it.title)}</b>${eff ? `<span class="og-eff" title="${esc(eff[1])}">${esc(eff[0])}</span>` : ''}${rec}${chg}</div>`
    + `<p class="og-s">${esc(it.sum)}${more}</p>${why}</div>`
    + `<div class="og-ctl"${busy ? ' aria-busy="true"' : ''}>${busy ? '<i class="og-spin" aria-hidden="true"></i>' : ''}${ogCtl(g, it, v, na || locked, busy)}</div>`
    + (it.desc ? `<div class="og-det" id="${did}"${open ? '' : ' hidden'}><p>${esc(it.desc)}</p>${src}</div>` : '')
    + '</div>'];
}

function ogBody() {
  const rows = $('#ogRows'), lp = $('#ogLhP'), bn = $('#ogBan'); if (!rows) return;
  const g = ogInfo(), cur = ogCur(), items = cur && g && cur.info.id === g.id ? cur.items : null;
  const ban = ogBanner(g, items);
  ogPut(bn, ban ? `<div class="og-ban ${ban.kind}">${ico(ban.icon)}<p>${esc(ban.text)}${ban.more ? ` <span>${esc(ban.more)}</span>` : ''}</p><button class="btn sm" type="button" data-ogre data-ogk="banre">${ico('refresh')}Neu prüfen</button></div>` : '');
  let lh = '';
  if (!OG.games) ogPut(rows, OG.err ? ogEmpty('alert', 'Spiele nicht gelesen', OG.err, 'bad', OG_RETRY) : ogEmpty('search', 'Suche deine Spiele', 'Fortnite, Counter-Strike 2 und Rainbow Six Siege', 'wait'));
  else if (!OG.games.length) ogPut(rows, ogEmpty('gamepad', 'Keine Spiele', 'Für andere Spiele gibt es gerade nichts einzustellen.'));
  else if (!items) {
    const err = OG.data[OG.game] && OG.data[OG.game].err;
    ogPut(rows, err ? ogEmpty('alert', 'Einstellungen nicht gelesen', err, 'bad', OG_RETRY) : ogEmpty('sliders', `Lese ${g ? g.name : 'das Spiel'}`, 'Einstellungsdatei und Windows-Grafikeinstellung', 'wait'));
  } else if (ogMissing(g)) {
    const t = items.map(i => i.title);
    const then = t.length ? `<p class="og-then">Danach kannst du hier ${esc(t.length <= 3 ? ogNames(t) : t.slice(0, 3).join(', ') + ` und ${t.length - 3} weitere`)} einstellen.</p>` : '';
    ogPut(rows, ogEmpty('search', `${g.name} nicht gefunden`, (OG_GAME[g.id] || {}).look || g.note || '', '', then + OG_RECHECK));
  } else if (!items.length) ogPut(rows, ogEmpty('sliders', 'Keine Einstellungen', `Für ${g.name} gibt es auf diesem PC gerade nichts einzustellen.`));
  else {
    ogPutRows(rows, items.map(it => ogRow(g, it, ban)));
    const ch = items.filter(i => i.changed).length, na = items.filter(i => !i.available).length;
    lh = [`${items.length} ${items.length === 1 ? 'Einstellung' : 'Einstellungen'}`, ch ? `${ch} von OrbitTweak geändert` : '', na ? `${na} hier nicht verfügbar` : ''].filter(Boolean).join(' · ');
  }
  if (lp && lp.textContent !== lh) lp.textContent = lh;
}

/** Everything; the keyboard stays on the control it was on, and a number being typed is kept. */
function ogRender() {
  if (!$('#ogRows')) return;
  const a = document.activeElement, mine = a && a.dataset && a.dataset.ogk && $('#p-og').contains(a) ? a : null;
  const typed = mine && mine.dataset.ognum && mine.dataset.dirty ? { v: mine.value, s: mine.selectionStart, e: mine.selectionEnd } : null;
  ogTabs(); ogHead(); ogBody();
  OG.fx = '';
  const rl = $('#ogReload');
  if (rl) { const l = !!OG.loading; rl.classList.toggle('spin', l); l ? rl.setAttribute('aria-disabled', 'true') : rl.removeAttribute('aria-disabled'); }
  if (mine && !mine.isConnected) {
    const n = $('#p-og').querySelector(`[data-ogk="${CSS.escape(mine.dataset.ogk)}"]`);
    if (n && !n.disabled) {
      n.focus({ preventScroll: true });
      if (typed && n.tagName === 'INPUT' && !n.readOnly) { n.value = typed.v; n.dataset.dirty = '1'; try { n.setSelectionRange(typed.s, typed.e); } catch { } }
    }
  }
  ogPollSync();
}

/* ---------- reading ---------- */
async function ogItems(quiet, game = OG.game) {
  if (!game) return;
  const seq = OG.seq[game] = (OG.seq[game] || 0) + 1;
  if (game === OG.game) ogRender();
  try {
    const r = await api('og.items', { game });
    if (seq !== OG.seq[game]) return;
    const raw = JSON.stringify(r), had = OG.data[game];
    if (had && had.raw === raw) return;   // nothing new (the poll while a game runs): leave the page as it is
    OG.data[game] = { info: r.game, items: (r && r.items) || [], raw };
    if (OG.games) { const k = OG.games.findIndex(x => x.id === game); if (k >= 0) OG.games[k] = r.game; }
    // rows that are gone lose their open details
    const ids = new Set(OG.data[game].items.map(i => game + '|' + i.id));
    [...OG.open].forEach(k => k.startsWith(game + '|') && !ids.has(k) && OG.open.delete(k));
  } catch (e) {
    if (seq !== OG.seq[game]) return;
    if (OG.data[game] && OG.data[game].items) { if (!quiet) toast('Nicht neu gelesen', ogMsg(e), false); }
    else OG.data[game] = { err: ogMsg(e) };
  }
  if (game === OG.game) ogRender();
}

/** The games and then the chosen game's settings. Each visit reads anew: the game may have run in between. */
async function ogLoad(quiet) {
  if (OG.loading) return OG.loading;
  OG.loading = (async () => {
    ogRender();
    try {
      const r = await api('og.games');
      OG.games = (r && r.games) || []; OG.err = ''; OG.at = Date.now();
    } catch (e) {
      OG.err = ogMsg(e);
      if (OG.games && !quiet) toast('Spiele nicht neu gelesen', OG.err, false);
    }
    if (OG.games && OG.games.length && !OG.games.some(g => g.id === OG.game))
      ogRemember((OG.games.find(g => g.installed || g.configFound) || OG.games[0]).id);   // the first one that is there
    if (OG.data[OG.game] && OG.data[OG.game].err) delete OG.data[OG.game];   // "Noch mal versuchen" shows the wait again
    if (OG.games && OG.games.length) await ogItems(quiet);
  })().finally(() => { OG.loading = null; ogRender(); });
  return OG.loading;
}

/** While the shown game runs, the page looks every few seconds whether it was closed (the rows unlock by themselves). */
function ogPollSync() {
  const g = ogInfo(), want = curPage === 'og' && !!g && !!g.running && !document.hidden;
  if (want && !OG.poll) OG.poll = setInterval(() => {
    const now = ogInfo();
    if (curPage !== 'og' || document.hidden || !now || !now.running) { clearInterval(OG.poll); OG.poll = 0; return; }
    if (!OG.bulk && !ogPending(now.id)) ogItems(true);
  }, 5000);
  else if (!want && OG.poll) { clearInterval(OG.poll); OG.poll = 0; }
}

function ogPick(id, focus) {
  if (id !== OG.game) {
    ogRemember(id); OG.ac = false;
    if (OG.data[id] && OG.data[id].err) delete OG.data[id];
    ogRender();
    ogItems(true);
  }
  if (focus) $(`#ogTab-${ogDom(id)}`)?.focus({ preventScroll: true });
}

/* ---------- changing ---------- */
/** One item: the row shows the new value and waits; the host's answer (or its German refusal) decides. */
function ogSet(id, value) {
  const g = ogInfo(), cur = ogCur();
  if (!g || !cur || OG.bulk) return;
  const game = g.id, it = cur.items.find(i => i.id === id), key = game + '|' + id;
  if (!it || OG.pend.has(key)) return;
  if (!it.available) return toast('Gerade nicht möglich', `${it.title}: ${it.why || 'Gerade nicht verfügbar'}`, false);
  if (ogLocked(g, it)) return toast('Gerade nicht möglich', `${it.title}: Schließ ${g.name} zuerst, sonst überschreibt das Spiel die Einstellung beim Beenden.`, false);
  if (ogSame(it.value, value)) return;
  OG.pend.set(key, value); ogRender();
  OG.chain = OG.chain.then(async () => {
    try {
      const r = await api('og.set', { game, id, value });
      const now = r && 'value' in r ? r.value : value;
      const live = OG.data[game] && OG.data[game].items && OG.data[game].items.find(i => i.id === id);   // (the list may be read anew meanwhile)
      if (live) live.value = now;
      toast(`${it.title}: ${ogShown(it, now)}`, `Wirkt ab dem nächsten Start von ${ogShort(g)}`);
    } catch (e) {
      toast('Nicht geändert', `${it.title}: ${ogMsg(e)}`, false);
    }
    OG.pend.delete(key);
    if (game === OG.game) ogRender();
    if (!ogPending(game)) await ogItems(true, game);   // values can depend on each other
  });
  return OG.chain;
}

function ogFailText(failed) {
  const t = failed.map(f => f.title), e = failed[0].error || 'Fehler';
  if (failed.every(f => f.error === e)) return `${ogNames(t)}: ${e}`;
  return `Nicht gesetzt: ${ogNames(t)}. ${failed[0].title}: ${e}`;
}

async function ogApply() {
  const g = ogInfo(), cur = ogCur();
  if (!g || !cur || OG.bulk || ogPending(g.id) || ogMissing(g)) return;
  const game = g.id, open = ogOpenRecs(cur.items);
  if (!open.length) return;
  if (g.running && open.some(i => i.needsClosed)) return toast('Gerade nicht möglich', `Schließ ${g.name} zuerst, dann setzt OrbitTweak die Empfehlungen.`, false);
  OG.bulk = 'apply'; OG.bulkGame = game; ogRender();
  await (OG.chain = OG.chain.then(async () => {
    try {
      const r = await api('og.apply', { game });
      const n = (r && r.applied) || 0, failed = (r && r.failed) || [];
      if (!failed.length) toast(n ? (n === 1 ? '1 Empfehlung gesetzt' : `${n} Empfehlungen gesetzt`) : 'Schon alles gesetzt',
        n ? `${g.name} · wirkt ab dem nächsten Start` : 'Die empfohlenen Einstellungen standen schon richtig');
      else toast(n ? `${n} von ${n + failed.length} Empfehlungen gesetzt` : 'Keine Empfehlung gesetzt', ogFailText(failed), false);
    } catch (e) { toast('Nicht angewendet', ogMsg(e), false); }
  }));
  OG.bulk = ''; ogRender();
  await ogItems(true, game);
}

async function ogRevert() {
  const g = ogInfo(), cur = ogCur();
  if (!g || !cur || OG.bulk || ogPending(g.id)) return;
  const game = g.id, changed = cur.items.filter(i => i.changed);
  if (!changed.length) return;
  if (g.running && changed.some(i => i.needsClosed)) return toast('Gerade nicht möglich', `Schließ ${g.name} zuerst, sonst überschreibt das Spiel die Werte beim Beenden.`, false);
  const one = changed.length === 1;
  const ok = await confirmBox(`${g.name} zurücksetzen?`,
    `${one ? `„${changed[0].title}“ bekommt wieder den Wert` : `${changed.length} Einstellungen bekommen wieder die Werte`} von vor OrbitTweak. Das wirkt ab dem nächsten Start von ${ogShort(g)}.`,
    { ok: 'Zurücksetzen' });
  if (!ok || OG.bulk || ogPending(game)) return;
  OG.bulk = 'revert'; OG.bulkGame = game; ogRender();
  await (OG.chain = OG.chain.then(async () => {
    try {
      const r = await api('og.revert', { game });
      const n = (r && r.reverted) || 0;
      toast(n ? (n === 1 ? '1 Einstellung zurückgesetzt' : `${n} Einstellungen zurückgesetzt`) : 'Nichts zurückgesetzt',
        n ? `${g.name} hat wieder die Werte von vorher · ab dem nächsten Start` : 'OrbitTweak hatte hier nichts mehr geändert');
    } catch (e) { toast('Nicht zurückgesetzt', ogMsg(e), false); }
  }));
  OG.bulk = ''; ogRender();
  await ogItems(true, game);
}

/** A typed number goes to the host on Enter or when the field is left, not on every key. */
function ogNumCommit(inp) {
  const cur = ogCur(), it = cur && cur.items.find(i => i.id === inp.dataset.ognum);
  delete inp.dataset.dirty;
  if (!it || inp.readOnly || inp.disabled) return;
  const now = ogVal(OG.game, it), back = () => { inp.value = now ?? ''; };
  const raw = inp.value.trim().replace(',', '.');
  if (raw === '') return back();
  const v = Number(raw);
  if (!Number.isFinite(v)) { back(); return toast('Nicht geändert', `${it.title}: Gib eine Zahl ein.`, false); }
  if (v === Number(now)) return back();
  if ((it.min != null && v < it.min) || (it.max != null && v > it.max)) {
    back(); return toast('Nicht geändert', `${it.title}: Erlaubt sind ${ogNum(it.min)} bis ${ogNum(it.max)}${it.unit ? ' ' + it.unit : ''}.`, false);
  }
  if (it.step && Math.abs((v - (it.min || 0)) / it.step - Math.round((v - (it.min || 0)) / it.step)) > 1e-9) {
    back(); return toast('Nicht geändert', `${it.title}: ${it.step === 1 ? 'Nur ganze Zahlen.' : `Nur in Schritten von ${ogNum(it.step)}.`}`, false);
  }
  ogSet(it.id, v);
}

ogFrame();
$('#p-og').addEventListener('click', e => {
  const t = e.target.closest('[data-ogg]'); if (t) return ogPick(t.dataset.ogg);
  if (e.target.closest('[data-ogre]')) { if (!OG.loading) ogLoad(false); return; }
  if (e.target.closest('[aria-disabled="true"]')) return;
  const sw = e.target.closest('[data-ogsw]');
  if (sw) { const it = ogCur() && ogCur().items.find(i => i.id === sw.dataset.ogsw); if (it) ogSet(it.id, !ogVal(OG.game, it)); return; }
  const ch = e.target.closest('[data-ogch]'); if (ch) { if (!ch.classList.contains('on')) ogSet(ch.dataset.ogch, ch.dataset.ogv); return; }
  const mo = e.target.closest('[data-ogmore]');
  if (mo) {
    const k = OG.game + '|' + mo.dataset.ogmore;
    if (OG.open.has(k)) OG.open.delete(k); else { OG.open.add(k); OG.fx = k; }
    return ogRender();
  }
  if (e.target.closest('#ogAcb')) { OG.ac = !OG.ac; OG.fx = OG.ac ? 'ac' : ''; return ogRender(); }
  if (e.target.closest('#ogApply')) return ogApply();
  if (e.target.closest('#ogRevert')) return ogRevert();
});
$('#p-og').addEventListener('change', e => {
  const s = e.target.closest('select[data-ogsel]');
  if (s) {
    const it = ogCur() && ogCur().items.find(i => i.id === s.dataset.ogsel);
    if (!it) return;
    if (s.getAttribute('aria-disabled') === 'true') { s.value = String(ogVal(OG.game, it)); return; }   // busy: it stays
    return ogSet(it.id, s.value);
  }
  const n = e.target.closest('input[data-ognum]'); if (n) ogNumCommit(n);
});
$('#p-og').addEventListener('input', e => { const n = e.target.closest('input[data-ognum]'); if (n) n.dataset.dirty = '1'; });
$('#p-og').addEventListener('keydown', e => {
  const tab = e.target.closest('[role="tab"][data-ogg]');
  if (tab && ['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(e.key)) {
    const l = OG.games || [], i = l.findIndex(g => g.id === tab.dataset.ogg); if (i < 0) return;
    e.preventDefault();
    const j = e.key === 'Home' ? 0 : e.key === 'End' ? l.length - 1 : (i + (e.key === 'ArrowRight' ? 1 : -1) + l.length) % l.length;
    return ogPick(l[j].id, true);
  }
  const n = e.target.closest('input[data-ognum]');
  if (n) {
    if (e.key === 'Enter') { e.preventDefault(); ogNumCommit(n); return; }
    if (e.key === 'Escape') { const it = ogCur() && ogCur().items.find(i => i.id === n.dataset.ognum); if (it) { n.value = ogVal(OG.game, it) ?? ''; delete n.dataset.dirty; } return; }
    if ((e.key === 'ArrowUp' || e.key === 'ArrowDown') && !n.readOnly) {
      const it = ogCur() && ogCur().items.find(i => i.id === n.dataset.ognum); if (!it) return;
      e.preventDefault();
      const step = (it.step || 1) * (e.shiftKey ? 10 : 1), base = Number(n.value.replace(',', '.'));
      let v = (Number.isFinite(base) ? base : Number(it.value) || 0) + (e.key === 'ArrowUp' ? step : -step);
      if (it.min != null) v = Math.max(it.min, v); if (it.max != null) v = Math.min(it.max, v);
      n.value = String(Math.round(v * 1e6) / 1e6); n.dataset.dirty = '1';
    }
    return;
  }
  if (e.key !== ' ' && e.key !== 'Enter') return;
  const sw = e.target.closest('.tg[data-ogsw]');
  if (sw) { e.preventDefault(); if (!e.repeat) sw.click(); }   // a held key switches once
});
// back from the game (Alt+Tab): read again
window.addEventListener('focus', () => { if (curPage === 'og' && OG.games && !OG.loading && Date.now() - OG.at > 3000) ogLoad(true); });
PAGE_SHOW.og = () => { ogRender(); ogLoad(true); };

/* ---------- browser preview: the same shapes as MainWindow.OtherGames.cs ---------- */
{
  const mock = id => { const m = OG_MOCK[id]; if (!m) throw new Error('Unbekanntes Spiel.'); return m; };
  const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
  MOCK_EXT['og.games'] = async () => { await sleep(120); return { games: OG_ORDER.filter(id => OG_MOCK[id]).map(id => ({ ...OG_MOCK[id].info })) }; };
  MOCK_EXT['og.items'] = async a => { await sleep(160); const m = mock(a.game); return { game: { ...m.info }, items: m.items.map(i => ({ ...i })) }; };
  MOCK_EXT['og.set'] = async a => {
    const m = mock(a.game), i = m.items.find(x => x.id === a.id);
    if (!i) throw new Error('Unbekannte Einstellung.');
    if (!i.available) throw new Error(i.why || 'Gerade nicht verfügbar.');
    if (i.needsClosed && m.info.running) throw new Error(`Schließ ${m.info.name} zuerst, sonst überschreibt das Spiel die Einstellung beim Beenden.`);
    if (!('orig' in i)) i.orig = i.value;
    i.value = a.value; i.changed = !same(i.value, i.orig);
    return { ok: true, value: i.value };
  };
  MOCK_EXT['og.apply'] = async a => {
    const m = mock(a.game); let applied = 0; const failed = [];
    for (const i of m.items.filter(x => x.rec && x.available && !same(x.value, x.recValue))) {
      try { await MOCK_EXT['og.set']({ game: a.game, id: i.id, value: i.recValue }); applied++; } catch (e) { failed.push({ id: i.id, title: i.title, error: e.message }); }
    }
    return { ok: !failed.length, applied, failed };
  };
  MOCK_EXT['og.revert'] = async a => {
    const m = mock(a.game); let reverted = 0;
    for (const i of m.items) if ('orig' in i && i.changed) { i.value = i.orig; i.changed = false; reverted++; }
    return { ok: true, reverted };
  };
}
