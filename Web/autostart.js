'use strict';
/* 2.27 – Autostart: what starts with Windows. Switching off works exactly like the Task Manager (the flag under
   Explorer\StartupApproved, for Store apps their State value since 2.28), nothing is deleted.
   Host: MainWindow.Autostart.cs, Core/Autostart.cs, Core/AutostartStore.cs, Core/AutostartLogic.cs. */

const AS = {
  items: null, at: 0, err: '', loading: null,
  filter: 'all',
  busy: new Set(),   // switches waiting for the host
  open: new Set(),   // rows showing their command line
  keep: new Set(),   // switched while a filter is on: they stay in the list until the filter changes
  undo: [],          // what "Empfohlene ausschalten" switched off last, for "Rückgängig"
};
const AS_FILTERS = [['all', 'Alle'], ['on', 'An'], ['off', 'Aus']];
const AS_FMT_DAY = new Intl.DateTimeFormat('de-DE', { day: 'numeric', month: 'long' });
const AS_FMT_YEAR = new Intl.DateTimeFormat('de-DE', { day: 'numeric', month: 'long', year: 'numeric' });
const asList = l => new Intl.ListFormat('de', { type: 'conjunction' }).format(l);
const asCanOff = () => (AS.items || []).filter(i => i.rec === 'off' && i.on && !i.lock);
/** "Neu laden" waits while the list is read and while switches are on their way to Windows (a list read then could miss them). */
const asReloadState = () => { const b = $('#asReload'); if (b) b.disabled = !!AS.loading || AS.busy.size > 0; };

/** The page's frame, drawn once at load: go() then already finds its cards for the entrance. */
function asFrame() {
  $('#p-autostart').innerHTML = `
  <div class="ph">
    <div><div class="kicker">Autostart</div><h1>Was mit Windows <span class="grad">startet</span></h1>
    <p class="pd">Ausschalten löscht nichts: Das Programm bleibt installiert und startet nur nicht mehr von selbst, genau wie im Task-Manager. Wirkt ab dem nächsten Windows-Start.</p></div>
    <div class="ph-r"><button class="btn" id="asReload" type="button">${ico('refresh')}Neu laden</button></div>
  </div>
  <div class="card as-sum">
    <div class="as-m"><span class="as-k">Programme</span><b id="asN">–</b><span class="as-s" id="asNS">im Autostart</span></div>
    <div class="as-m"><span class="as-k">Davon an</span><b id="asOn">–</b><span class="as-s" id="asOnS">starten mit Windows</span></div>
    <div class="as-m"><span class="as-k">Können aus</span><b id="asRec">–</b><span class="as-s" id="asRecS">kosten nur Startzeit</span></div>
    <div class="as-go" id="asGo"></div>
  </div>
  <div class="card as-list">
    <div class="as-lh"><div><b>Beim Windows-Start</b><p id="asLhP">Wird gelesen…</p></div><div class="seg" id="asSeg"></div></div>
    <div id="asRows"></div>
  </div>`;
}

/** The program's own icon, or its first letter on a quiet tile. */
function asIcon(it) {
  if (it.icon && /^data:image\//.test(it.icon)) return `<img src="${esc(it.icon)}" alt="" draggable="false">`;
  const ch = (String(it.name || '').match(/[\p{L}\p{N}]/u) || ['?'])[0].toUpperCase();
  return `<span class="as-mono">${esc(ch)}</span>`;
}
/** "Kann aus" while it still starts; "Besser an" always (warm when it is off). */
function asChip(it) {
  if (it.lock) return `<span class="as-rec pol" title="${esc(it.why)}">Richtlinie</span>`;
  if (it.rec === 'off' && it.on) return `<span class="as-rec off" title="${esc(it.why)}">Kann aus</span>`;
  if (it.rec === 'on') return `<span class="as-rec ${it.on ? 'keep' : 'warn'}" title="${esc(it.why)}">Besser an</span>`;
  return '';
}
function asSince(iso) {
  const d = iso ? new Date(iso) : null;
  if (!d || isNaN(d)) return '';
  return (d.getFullYear() === new Date().getFullYear() ? AS_FMT_DAY : AS_FMT_YEAR).format(d);
}
function asDetail(it) {
  const since = !it.on ? asSince(it.offSince) : '';
  const recIco = it.lock ? 'padlock' : it.rec === 'on' ? 'shield' : it.rec === 'off' ? 'power' : 'sparkle';
  // a Store app's entry is "family/task id": the task is what Windows switches
  const task = it.store ? String(it.entry).slice(String(it.entry).indexOf('/') + 1) : '';
  const facts = [
    it.why ? `<span>${ico(it.exists ? recIco : 'alert')}${esc(it.why)}</span>` : '',
    since ? `<span>${ico('clock')}Aus seit ${esc(since)}</span>` : '',
    it.store ? `<span>${ico('bag')}Store-App, Startaufgabe „${esc(task)}“</span>`
      : `<span>${ico(it.folder ? 'file' : 'layers')}${it.folder ? 'Datei' : 'Registrierungswert'} „${esc(it.entry)}“</span>`,
  ].filter(Boolean).join('');
  // a dead link in the startup folder is still there to show; a Store app's folder (WindowsApps) cannot be opened
  const canShow = !it.store && (it.folder || (it.exists && it.path));
  return `<div class="as-det">
    <code class="as-cmd">${esc(it.command)}</code>
    <div class="as-df"><div class="as-facts">${facts}</div>${canShow ? `<button class="btn sm gh" type="button" data-asrev="${esc(it.id)}">${ico('folder')}Im Explorer zeigen</button>` : ''}</div>
  </div>`;
}
function asRow(it) {
  const open = AS.open.has(it.id), busy = AS.busy.has(it.id);
  const sub = [it.exists ? '' : `<span class="as-miss">${ico('alert')}Datei fehlt</span>`, esc(it.publisher || ''), esc(it.source)].filter(Boolean).join(' · ');
  return `<div class="as-row ${it.on ? 'on' : 'off'}${it.exists ? '' : ' miss'}${open ? ' open' : ''}" data-asid="${esc(it.id)}">
    <div class="as-hd" role="button" tabindex="0" aria-expanded="${open}" title="${open ? '' : esc(it.command)}">
      <span class="as-ic">${asIcon(it)}</span>
      <span class="as-tx"><span class="as-t"><b>${esc(it.name)}</b>${asChip(it)}</span><span class="as-sub">${sub}</span></span>
      <svg class="i as-ch"><use href="#chev"/></svg>
    </div>
    <div class="tg ${it.on ? 'on' : ''}${busy ? ' busy' : ''}${it.lock ? ' pol' : ''}" data-astg="${esc(it.id)}" role="switch" tabindex="0" aria-checked="${it.on}"${it.lock ? ' aria-disabled="true"' : ''} aria-label="${esc(it.name)} mit Windows starten" title="${it.lock ? 'Von einer Richtlinie festgelegt' : it.on ? 'Startet mit Windows' : 'Startet nicht mit Windows'}"></div>
    ${open ? asDetail(it) : ''}
  </div>`;
}
function asEmpty(icon, title, text, cls = '', btn = '') {
  return `<div class="as-empty ${cls}">${ico(icon)}<b>${esc(title)}</b><span>${esc(text)}</span>${btn}</div>`;
}

/** Numbers, the main action and the list. `focusId` keeps the keyboard on a switch that was just used. */
function asRender(focusId) {
  const items = AS.items, all = items || [];
  const hadFocus = document.activeElement?.dataset?.astg;   // the list is drawn anew: the keyboard stays on its switch
  const on = all.filter(i => i.on).length, can = asCanOff();
  const set = (id, v) => { const el = $('#' + id); if (el) el.textContent = v; };
  set('asN', items ? all.length : '–'); set('asOn', items ? on : '–'); set('asRec', items ? can.length : '–');
  set('asOnS', items ? (on === 1 ? 'startet mit Windows' : 'starten mit Windows') : 'starten mit Windows');
  set('asRecS', items ? (can.length ? 'kosten nur Startzeit' : 'alles erledigt') : 'kosten nur Startzeit');
  $('#p-autostart .as-sum')?.classList.toggle('idle', !items);
  asReloadState();

  // the one main action: switch off what only costs boot time
  const undo = AS.undo.filter(id => all.some(i => i.id === id && !i.on));
  const undoBtn = undo.length ? `<button class="btn sm gh" id="asUndo" type="button">${ico('undo')}Rückgängig</button>` : '';
  let go = '';
  if (items && can.length) {
    const pile = can.slice(0, 5).map(i => `<span class="as-pi" title="${esc(i.name)}">${asIcon(i)}</span>`).join('') + (can.length > 5 ? `<span class="as-pm">+${can.length - 5}</span>` : '');
    const names = can.length <= 2 ? `<b>${esc(asList(can.map(i => i.name)))}</b>` : `<b>${esc(can.slice(0, 2).map(i => i.name).join(', '))}</b> und ${can.length - 2} weitere`;
    go = `<div class="as-gh"><div class="as-pile">${pile}</div></div>
      <p>${names} brauchst du beim Start nicht.</p>
      <button class="btn p" id="asOff" type="button">${ico('power')}Empfohlene ausschalten</button>${undoBtn}`;
  } else if (items) {
    go = `<div class="as-gh"><span class="as-ok">${ico('check')}</span></div>
      <p><b>Aufgeräumt</b> Hier startet nichts mehr, was wir als „Kann aus“ kennen.</p>${undoBtn}`;
  }
  const g = $('#asGo'); if (g) { g.innerHTML = go; g.classList.toggle('done', !!items && !can.length); }

  // filter with counts
  const n = { all: all.length, on, off: all.length - on };
  const seg = $('#asSeg');
  if (seg) seg.innerHTML = AS_FILTERS.map(([k, t]) => `<button type="button" class="${AS.filter === k ? 'on' : ''}" data-asf="${k}" aria-pressed="${AS.filter === k}">${t}<i class="as-cnt">${items ? n[k] : '–'}</i></button>`).join('');
  const lh = $('#asLhP');
  const nStore = all.filter(i => i.store).length;
  if (lh) lh.textContent = !items ? (AS.err ? 'Nicht gelesen' : 'Wird gelesen…')
    : !all.length ? 'Keine Einträge gefunden'
    : `${all.filter(i => !i.folder && !i.store).length} aus der Registrierung · ${all.filter(i => i.folder).length} aus dem Autostart-Ordner`
      + (nStore ? ` · ${nStore} ${nStore === 1 ? 'Store-App' : 'Store-Apps'}` : '');

  const rows = $('#asRows'); if (!rows) return;
  if (!items) {
    rows.innerHTML = AS.err
      ? asEmpty('alert', 'Autostart nicht gelesen', AS.err, 'bad', `<button class="btn sm" type="button" data-asretry>${ico('refresh')}Noch mal versuchen</button>`)
      : asEmpty('power', 'Lese den Autostart', 'Registrierung, Autostart-Ordner und Store-Apps', 'wait');
    return;
  }
  const shown = all.filter(i => AS.filter === 'all' || AS.keep.has(i.id) || (AS.filter === 'on') === i.on);
  rows.innerHTML = shown.length ? shown.map(asRow).join('')
    : !all.length ? asEmpty('check', 'Nichts im Autostart', 'In der Registrierung, im Autostart-Ordner und bei den Store-Apps startet kein Programm.', 'ok')
    : AS.filter === 'off' ? asEmpty('power', 'Nichts ausgeschaltet', 'Alles in der Liste startet mit Windows.')
    : asEmpty('power', 'Nichts startet mit Windows', 'Alle Einträge sind ausgeschaltet.');
  if (focusId && hadFocus === focusId) rows.querySelector(`.tg[data-astg="${CSS.escape(focusId)}"]`)?.focus({ preventScroll: true });
}

async function asLoad(quiet) {
  if (AS.loading) return AS.loading;
  if (AS.busy.size) return;   // read again once the switches are through (the next visit or "Neu laden")
  if (!AS.items) { AS.err = ''; asRender(); }
  AS.loading = (async () => {
    try {
      const r = await api('autostart.list');
      AS.items = ((r && r.items) || []).slice().sort((a, b) => a.name.localeCompare(b.name, 'de', { sensitivity: 'base' }));
      AS.err = ''; AS.at = Date.now();
      // rows that are gone lose their open state and undo
      const ids = new Set(AS.items.map(i => i.id));
      [...AS.open].forEach(id => ids.has(id) || AS.open.delete(id));
      AS.undo = AS.undo.filter(id => ids.has(id));
    } catch (e) {
      AS.err = (e && e.message) || 'Unbekannter Fehler';
      if (AS.items && !quiet) toast('Autostart nicht neu gelesen', AS.err, false);
    }
    AS.keep.clear();
    asRender();
  })().finally(() => { AS.loading = null; asReloadState(); });
  asReloadState();
  return AS.loading;
}

async function asToggle(id) {
  const it = AS.items && AS.items.find(x => x.id === id);
  if (!it || AS.busy.has(id)) return;
  if (it.lock) return toast('Nicht umschaltbar', `${it.name}: ${it.why || 'Von einer Richtlinie festgelegt'}`, false);
  const want = !it.on;
  // audio, drivers, input devices and protection: one question before they go
  if (!want && it.rec === 'on' && it.exists) {
    const sure = await confirmBox(`${it.name} ausschalten?`, `${it.why}. Du kannst es trotzdem ausschalten und hier jederzeit wieder einschalten.`, { ok: 'Trotzdem ausschalten' });
    if (!sure || !it.on || AS.busy.has(id) || !AS.items.includes(it)) return;   // (the list may have been read anew meanwhile)
  }
  AS.busy.add(id); AS.keep.add(id); it.on = want; asRender(id);
  try {
    const r = await api('autostart.set', { id, on: want, name: it.name });
    it.on = !!(r && r.on);
    if (it.on !== want) toast('Nicht umgeschaltet', `${it.name}: Windows hat die Änderung nicht übernommen`, false);
    else {
      it.offSince = want || it.store ? null : new Date().toISOString();   // Windows keeps no time for a Store app
      toast(want ? 'Startet wieder mit Windows' : 'Startet nicht mehr mit Windows', `${it.name} · ab dem nächsten Windows-Start`);
    }
  } catch (e) {
    it.on = !want;
    toast('Nicht umgeschaltet', `${it.name}: ${(e && e.message) || 'Fehler'}`, false);
  }
  AS.busy.delete(id); asRender(id);
}

/** The main action: everything marked "Kann aus" that still starts, after one question that names them. */
async function asOffAll() {
  let list = asCanOff(); if (!list.length) return;
  const one = list.length === 1;
  // FiveM does not open Steam itself, and servers that check the Steam account turn you away without it
  const steam = list.some(i => /\bsteam\b/i.test(i.name)) ? ' Verlangt dein Server Steam, öffne Steam vor FiveM selbst.' : '';
  const ok = await confirmBox(one ? `${list[0].name} ausschalten?` : `${list.length} Programme ausschalten?`,
    `${asList(list.map(i => i.name))} ${one ? 'startet' : 'starten'} dann nicht mehr mit Windows. Gelöscht wird nichts, du kannst ${one ? 'es' : 'sie'} hier jederzeit wieder einschalten.${steam}`,
    { ok: 'Ausschalten' });
  if (!ok) return;
  list = list.filter(i => AS.items.includes(i) && i.on && !AS.busy.has(i.id));   // the list may have been read anew meanwhile
  if (!list.length) return;
  const done = [], failed = [];
  list.forEach(i => AS.busy.add(i.id)); asRender();
  for (const it of list) {
    try {
      const r = await api('autostart.set', { id: it.id, on: false, name: it.name });
      it.on = !!(r && r.on);
      if (it.on) failed.push([it, 'Windows hat die Änderung nicht übernommen']);
      else { it.offSince = it.store ? null : new Date().toISOString(); done.push(it); }
    } catch (e) { failed.push([it, (e && e.message) || 'Fehler']); }
    AS.busy.delete(it.id); AS.keep.add(it.id); asRender();
  }
  AS.undo = done.map(i => i.id); asRender();
  if (!failed.length) toast(done.length === 1 ? `${done[0].name} ausgeschaltet` : `${done.length} Programme ausgeschaltet`, 'Wirkt ab dem nächsten Windows-Start');
  else toast(`${done.length} von ${list.length} ausgeschaltet`, `${failed[0][0].name}: ${failed[0][1]}`, false);
}

async function asUndo() {
  const ids = AS.undo; AS.undo = [];
  const list = ids.map(id => AS.items.find(i => i.id === id)).filter(i => i && !i.on && !AS.busy.has(i.id));
  if (!list.length) return asRender();
  list.forEach(i => AS.busy.add(i.id)); asRender();
  let n = 0;
  for (const it of list) {
    try { const r = await api('autostart.set', { id: it.id, on: true, name: it.name }); it.on = !!(r && r.on); if (it.on) { it.offSince = null; n++; } } catch { }
    AS.busy.delete(it.id); AS.keep.add(it.id); asRender();
  }
  if (n === list.length) toast('Rückgängig gemacht', `${n === 1 ? list[0].name + ' startet' : n + ' Programme starten'} wieder mit Windows`);
  else toast('Nicht alles zurückgenommen', `${n} von ${list.length} starten wieder mit Windows`, false);
}

async function asReveal(id) {
  try { const r = await api('autostart.reveal', { id }); if (r && r.ok === false) toast('Nicht im Explorer gezeigt', r.error || '', false); }
  catch (e) { toast('Nicht im Explorer gezeigt', (e && e.message) || '', false); }
}

function asExpand(id) {
  AS.open.has(id) ? AS.open.delete(id) : AS.open.add(id);
  const it = AS.items && AS.items.find(x => x.id === id), row = $(`#asRows .as-row[data-asid="${CSS.escape(id)}"]`);
  if (!it || !row) return asRender();
  const had = row.contains(document.activeElement);
  row.outerHTML = asRow(it);
  if (had) $(`#asRows .as-row[data-asid="${CSS.escape(id)}"] .as-hd`)?.focus({ preventScroll: true });
}

asFrame();
$('#p-autostart').addEventListener('click', e => {
  const t = e.target.closest('[data-astg]'); if (t) return asToggle(t.dataset.astg);
  const f = e.target.closest('[data-asf]'); if (f) { if (AS.filter !== f.dataset.asf) { AS.filter = f.dataset.asf; AS.keep.clear(); asRender(); } return; }
  const rv = e.target.closest('[data-asrev]'); if (rv) return asReveal(rv.dataset.asrev);
  if (e.target.closest('#asOff')) return asOffAll();
  if (e.target.closest('#asUndo')) return asUndo();
  if (e.target.closest('#asReload, [data-asretry]')) return asLoad(false);
  const h = e.target.closest('.as-hd'); if (h) asExpand(h.closest('.as-row').dataset.asid);
});
$('#p-autostart').addEventListener('keydown', e => {
  if (e.key !== ' ' && e.key !== 'Enter') return;
  if (e.repeat) { if (e.target.closest('.tg[data-astg], .as-hd')) e.preventDefault(); return; }   // a held key switches once
  const t = e.target.closest('.tg[data-astg]'); if (t) { e.preventDefault(); return asToggle(t.dataset.astg); }
  const h = e.target.closest('.as-hd'); if (h && e.target === h) { e.preventDefault(); asExpand(h.closest('.as-row').dataset.asid); }
});
// opened again: what is shown is read fresh when it is older than half a minute (the Task Manager may have switched something)
PAGE_SHOW.autostart = () => { if (!AS.items || Date.now() - AS.at > 30000) asLoad(true); else asRender(); };

/* ---------- browser preview: the same shapes as MainWindow.Autostart.cs returns ---------- */
{
  const svg = (c1, c2, g) => 'data:image/svg+xml,' + encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48"><defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${c1}"/><stop offset="1" stop-color="${c2}"/></linearGradient></defs><rect x="4" y="4" width="40" height="40" rx="11" fill="url(#g)"/><g fill="none" stroke="#fff" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round">${g}</g></svg>`);
  const G = {
    pad: '<rect x="12" y="18" width="24" height="13" rx="6.5"/><path d="M18 22.5v4M16 24.5h4"/><circle cx="29.5" cy="23.5" r=".9" fill="#fff"/><circle cx="31.5" cy="26" r=".9" fill="#fff"/>',
    chat: '<path d="M14 16h20v12H23l-5 4v-4h-4z"/>',
    wave: '<path d="M13 24h4l3-7 4 14 4-10 2 3h5"/>',
    cloud: '<path d="M18 31h13a5.5 5.5 0 0 0 .6-11A8 8 0 0 0 17 21.6 4.8 4.8 0 0 0 18 31z"/>',
    bolt: '<path d="M26 13l-9 13h7l-2 9 9-13h-7z"/>',
    mouse: '<rect x="18" y="13" width="12" height="22" rx="6"/><path d="M24 17v4"/>',
    shield: '<path d="M24 13l9 3.6V23c0 5.6-3.8 9.6-9 11.5-5.2-1.9-9-5.9-9-11.5v-6.4z"/>',
    play: '<path d="M20 16.5v15l12-7.5z" fill="#fff"/>',
    rings: '<circle cx="20.5" cy="24" r="6.5"/><circle cx="27.5" cy="24" r="6.5"/>',
    snap: '<rect x="14" y="17" width="20" height="15" rx="3"/><circle cx="24" cy="24.5" r="4"/><path d="M20 17l1.5-3h5l1.5 3"/>',
  };
  const SRC = {
    hkcu: ['Nur für dich · Registrierung', false, false], hklm: ['Alle Nutzer · Registrierung', true, false],
    hklm32: ['Alle Nutzer · Registrierung (32 Bit)', true, false], user: ['Nur für dich · Autostart-Ordner', false, true],
    store: ['Nur für dich · Store-App', false, false],
  };
  const WHY = {
    launcher: 'Launcher, öffnet sich beim Spielstart von selbst', steam: 'Launcher, öffne ihn vor FiveM selbst, wenn dein Server Steam verlangt', app: 'App, die du bei Bedarf selbst öffnest',
    updater: 'Updater, das Programm prüft beim Öffnen selbst', missing: 'Datei fehlt, startet ohnehin nicht',
    security: 'Gehört zu deinem Virenschutz, bitte anlassen', audio: 'Ton und Audio-Einstellungen hängen daran',
    driver: 'Treiber-Software, Einstellungen und Profile hängen daran', input: 'Maus, Tastatur und Headset brauchen es für Profile und Tasten',
    discord: 'Für Sprachchat oft gewollt, deine Entscheidung', sync: 'Synchronisiert deine Dateien nur, solange es läuft',
  };
  const ago = d => new Date(Date.now() - d * 864e5).toISOString();
  const L = 'C:\\Users\\Max\\AppData\\Local', R = 'C:\\Users\\Max\\AppData\\Roaming', PF = 'C:\\Program Files', PF86 = 'C:\\Program Files (x86)';
  const WA = 'C:\\Program Files\\WindowsApps';
  const e = (src, entry, name, publisher, command, path, o = {}) => ({
    id: src + '|' + entry, name, entry, publisher, command, path, exists: true, source: SRC[src][0], allUsers: SRC[src][1], folder: SRC[src][2],
    store: src === 'store', lock: false, on: true, offSince: null, icon: null, rec: null, why: '', ...o,
  });
  const MOCK = [
    e('hkcu', 'Steam', 'Steam', 'Valve Corporation', `"${PF86}\\Steam\\steam.exe" -silent`, `${PF86}\\Steam\\steam.exe`, { icon: svg('#2A475E', '#171A21', G.pad), rec: 'off', why: WHY.steam }),
    e('hkcu', 'Discord', 'Discord', 'Discord Inc.', `${L}\\Discord\\Update.exe --processStart Discord.exe`, `${L}\\Discord\\Update.exe`, { icon: svg('#7B86FF', '#5865F2', G.chat), why: WHY.discord }),
    e('hkcu', 'Spotify', 'Spotify', 'Spotify AB', `${R}\\Spotify\\Spotify.exe --autostart --minimized`, `${R}\\Spotify\\Spotify.exe`, { icon: svg('#3BE477', '#1AA34A', G.wave), rec: 'off', why: WHY.app }),
    e('hkcu', 'EpicGamesLauncher', 'Epic Games Launcher', 'Epic Games, Inc.', `"${PF86}\\Epic Games\\Launcher\\Portal\\Binaries\\Win64\\EpicGamesLauncher.exe" -silent`, `${PF86}\\Epic Games\\Launcher\\Portal\\Binaries\\Win64\\EpicGamesLauncher.exe`, { icon: svg('#3A3A3E', '#18181B', G.pad), rec: 'off', why: WHY.launcher }),
    e('hkcu', 'OneDrive', 'Microsoft OneDrive', 'Microsoft Corporation', `"${PF}\\Microsoft OneDrive\\OneDrive.exe" /background`, `${PF}\\Microsoft OneDrive\\OneDrive.exe`, { icon: svg('#3AA0FF', '#0364B8', G.cloud), why: WHY.sync }),
    e('hklm', 'RtkAudUService', 'Realtek HD Audio Universal Service', 'Realtek Semiconductor', `"C:\\Windows\\System32\\RtkAudUService64.exe" -background`, 'C:\\Windows\\System32\\RtkAudUService64.exe', { rec: 'on', why: WHY.audio }),
    e('hklm32', 'NvBackend', 'NVIDIA GeForce Experience Backend', 'NVIDIA Corporation', `"${PF86}\\NVIDIA Corporation\\Update Core\\NvBackend.exe"`, `${PF86}\\NVIDIA Corporation\\Update Core\\NvBackend.exe`, { icon: svg('#8BD43A', '#5E9A1C', G.bolt), rec: 'on', why: WHY.driver }),
    e('hkcu', 'Logitech G HUB', 'Logitech G HUB', 'Logitech, Inc.', `"${PF}\\LGHUB\\system_tray\\lghub_system_tray.exe" --minimized`, `${PF}\\LGHUB\\system_tray\\lghub_system_tray.exe`, { icon: svg('#38C6F4', '#0B87B8', G.mouse), rec: 'on', why: WHY.input }),
    e('hklm', 'SecurityHealth', 'Windows-Sicherheitsbenachrichtigungssymbol', 'Microsoft Corporation', 'C:\\Windows\\system32\\SecurityHealthSystray.exe', 'C:\\Windows\\system32\\SecurityHealthSystray.exe', { icon: svg('#4AA8FF', '#1A63C9', G.shield), rec: 'on', why: WHY.security }),
    e('hkcu', 'com.squirrel.Teams.Teams', 'Microsoft Teams (classic)', 'Microsoft Corporation', `${L}\\Microsoft\\Teams\\Update.exe --processStart "Teams.exe" --process-start-args "--system-initiated"`, `${L}\\Microsoft\\Teams\\Update.exe`, { icon: svg('#7B83EB', '#4B53BC', G.chat), on: false, offSince: ago(3), rec: 'off', why: WHY.app }),
    e('hklm', 'EADM', 'EA app', 'Electronic Arts', `"${PF}\\Electronic Arts\\EA Desktop\\EA Desktop\\EALauncher.exe" -silent`, `${PF}\\Electronic Arts\\EA Desktop\\EA Desktop\\EALauncher.exe`, { icon: svg('#FF5A5F', '#D6202A', G.play), rec: 'off', why: WHY.launcher }),
    e('hkcu', 'Opera GX Browser Assistant', 'Opera GX Browser Assistant', '', `${L}\\Programs\\Opera GX\\assistant\\browser_assistant.exe`, `${L}\\Programs\\Opera GX\\assistant\\browser_assistant.exe`, { exists: false, rec: 'off', why: WHY.missing }),
    e('hklm', 'Adobe Creative Cloud', 'Adobe Creative Cloud', 'Adobe Inc.', `"${PF}\\Adobe\\Adobe Creative Cloud\\ACC\\Creative Cloud.exe" --showwindow=false --onOSstartup=true`, `${PF}\\Adobe\\Adobe Creative Cloud\\ACC\\Creative Cloud.exe`, { icon: svg('#FF6B5E', '#DA1F26', G.rings), on: false, offSince: ago(41), rec: 'off', why: WHY.updater }),
    e('user', 'ShareX.lnk', 'ShareX', 'ShareX Team', `"${PF}\\ShareX\\ShareX.exe" -silent`, `${PF}\\ShareX\\ShareX.exe`, { icon: svg('#5BC0F8', '#2B7BD6', G.snap) }),
    e('store', 'MSTeams_8wekyb3d8bbwe/TeamsTfwStartupTask', 'Microsoft Teams', 'Microsoft Corporation', `${WA}\\MSTeams_25163.3611.3774.6315_x64__8wekyb3d8bbwe\\ms-teams.exe`, `${WA}\\MSTeams_25163.3611.3774.6315_x64__8wekyb3d8bbwe\\ms-teams.exe`, { icon: svg('#7B83EB', '#4B53BC', G.chat), rec: 'off', why: WHY.app }),
    e('store', '5319275A.WhatsAppDesktop_cv1g1gvanyjgm/WhatsAppStartupTask', 'WhatsApp', 'WhatsApp Inc.', `${WA}\\5319275A.WhatsAppDesktop_2.2546.3.0_x64__cv1g1gvanyjgm\\WhatsApp.Root.exe`, `${WA}\\5319275A.WhatsAppDesktop_2.2546.3.0_x64__cv1g1gvanyjgm\\WhatsApp.Root.exe`, { icon: svg('#4BE07B', '#1DA851', G.chat), rec: 'off', why: WHY.app }),
    e('store', 'Microsoft.WindowsTerminal_8wekyb3d8bbwe/StartTerminalOnLoginTask', 'Terminal', 'Microsoft Corporation', `${WA}\\Microsoft.WindowsTerminal_1.23.12811.0_x64__8wekyb3d8bbwe\\WindowsTerminal.exe`, `${WA}\\Microsoft.WindowsTerminal_1.23.12811.0_x64__8wekyb3d8bbwe\\WindowsTerminal.exe`, { on: false }),
  ];
  MOCK_EXT['autostart.list'] = async () => { await sleep(260); return { items: MOCK.map(i => ({ ...i })) }; };
  MOCK_EXT['autostart.set'] = async a => {
    const i = MOCK.find(x => x.id === a.id);
    if (!i) throw new Error('Den Eintrag gibt es nicht mehr. Lade die Liste neu.');
    if (i.lock) throw new Error('Eine Richtlinie legt fest, ob diese App mit Windows startet. Das lässt sich hier nicht ändern.');
    if (i.on !== !!a.on) { i.on = !!a.on; i.offSince = i.on || i.store ? null : new Date().toISOString(); }
    return { on: i.on };
  };
  MOCK_EXT['autostart.reveal'] = async a => {
    const i = MOCK.find(x => x.id === a.id);
    return i && !i.store && (i.exists || i.folder) ? { ok: true } : { ok: false, error: 'Die Datei ist nicht mehr da.' };
  };
}
