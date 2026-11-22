'use strict';
/* 2.31 – Tweak-Wächter: Windows updates, a new driver, another program or the user can switch back a tweak OrbitTweak set.
   The host looks about every 15 minutes while no game runs and names what OrbitTweak switched on that is no longer
   active, once two looks in a row agree. This page part draws a calm banner on the Tweaks page ("Wieder anwenden" goes
   through each tweak's own switch path, "So lassen" forgets the mark), a line under the page description with the last
   look, and the count on the Tweaks entry of the side bar. It never says why something was reset: OrbitTweak cannot see that.
   Host: MainWindow.TweakGuard.cs (twg.state / twg.check / twg.apply / twg.keep, event "twg"), Core/TweakGuard.cs,
   Core/TweakGuardLogic.cs. */

const TWG = {
  st: null,        // the host's state: { status, lastAt, nextAt, confirmAt, error, watched, items: [{ id, name, icon, cat, restart, detail, since, error }] }
  busy: '',        // 'apply' | 'keep' | 'check' while the host works on it
  done: null,      // { kind: 'apply' | 'keep', names, restart, fps } after a button, until closed or after a few seconds
  doneT: 0,
  loading: null,
};
const TWG_FPS = 'fpslimit';
const twgJoin = l => new Intl.ListFormat('de', { type: 'conjunction' }).format(l);
const twgTime = iso => { const d = iso ? new Date(iso) : null; return d && !isNaN(d) ? d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' }) : ''; };
/** "2 Tweaks wurden zurückgesetzt", with the FPS limit named on its own (it is no switch on this page). */
function twgTitle(items) {
  const fps = items.some(i => i.id === TWG_FPS), k = items.length - (fps ? 1 : 0);
  if (!k) return 'Dein FPS-Limit wurde geändert';
  const n = k === 1 ? '1 Tweak' : `${k} Tweaks`;
  return fps ? `${n} und dein FPS-Limit wurden zurückgesetzt` : `${n} ${k === 1 ? 'wurde' : 'wurden'} zurückgesetzt`;
}

/** Banner, line and badge are put in place once: after the Tweaks page's head, under its description, into its nav entry. */
function twgMount() {
  if ($('#twgBox')) return true;
  const ph = $('#p-tweaks .ph'), nav = $('.nav[data-p="tweaks"]');
  if (!ph || !ph.firstElementChild || !nav) return false;
  const box = document.createElement('div');
  box.id = 'twgBox'; box.className = 'card twg hidden'; box.setAttribute('role', 'region'); box.setAttribute('aria-label', 'Tweak-Wächter');
  ph.after(box);
  const line = document.createElement('div');
  line.id = 'twgLine'; line.className = 'twg-line';
  ph.firstElementChild.append(line);
  const b = document.createElement('span');
  b.id = 'twgNav'; b.className = 'badge hidden';
  nav.append(b);
  box.addEventListener('click', e => {
    const a = e.target.closest('[data-twg]'); if (!a || a.disabled) return;
    TWG.focus = a.dataset.twg;
    ({ apply: twgApply, keep: twgKeep, close: twgCloseDone })[a.dataset.twg]?.();
  });
  line.addEventListener('click', e => { if (e.target.closest('[data-twg="check"]')) { TWG.focus = 'check'; twgCheck(); } });
  // the buttons are drawn anew (and disabled while the host works): the focus follows them until it is put elsewhere
  const away = e => { if (!e.target.closest?.('#twgBox,#twgLine')) TWG.focus = null; };
  document.addEventListener('focusin', away);
  document.addEventListener('pointerdown', away);
  return true;
}
/** Gives the keyboard focus back to the guard's button it was on before the redraw. */
function twgRefocus() {
  if (!TWG.focus || curPage !== 'tweaks') return;
  const el = TWG.focus === 'check' ? $('#twgLine [data-twg="check"]') : $(`#twgBox [data-twg="${TWG.focus}"]`);
  if (el && !el.disabled && document.activeElement !== el) el.focus({ preventScroll: true });
}

function twgRender() {
  if (!twgMount()) return;
  const s = TWG.st, items = (s && s.items) || [];
  // side bar: how many are waiting, in the quiet badge style of "An"/"Auto"
  const nb = $('#twgNav');
  nb.textContent = items.length; nb.classList.toggle('hidden', !items.length);
  nb.title = items.length ? twgTitle(items) : '';
  twgLineRender();
  const box = $('#twgBox');
  if (TWG.done) { box.className = 'card twg twg-done'; box.innerHTML = twgDoneHtml(TWG.done); twgRefocus(); return; }
  box.classList.toggle('hidden', !items.length);
  if (!items.length) { box.innerHTML = ''; twgRefocus(); return; }
  box.className = 'card twg';
  const busy = TWG.busy === 'apply' || TWG.busy === 'keep';
  const failed = items.filter(i => i.error).length;
  const lead = items.some(i => i.id !== TWG_FPS) ? 'Von OrbitTweak eingeschaltet, jetzt nicht mehr aktiv.' : 'Von OrbitTweak gesetzt, im Treiberprofil steht jetzt ein anderer Wert.';
  box.innerHTML = `<div class="twg-ic">${ico('twg-alert')}</div>
    <div class="twg-tx">
      <b class="twg-t">${esc(twgTitle(items))}</b>
      <p class="twg-p">${lead} Das kann nach einem Windows- oder Treiber-Update passieren, durch ein anderes Programm oder weil du selbst etwas geändert hast. Woran es lag, sieht OrbitTweak nicht.</p>
      <div class="twg-list">${items.map(twgRowHtml).join('')}</div>
    </div>
    <div class="twg-act">
      <button class="btn sm gh" type="button" data-twg="keep" title="Ändert nichts. OrbitTweak fragt hier nicht mehr nach"${busy ? ' disabled' : ''}>${TWG.busy === 'keep' ? 'Einen Moment…' : 'So lassen'}</button>
      <button class="btn sm" type="button" data-twg="apply" title="Schaltet genau diese wieder ein, wie ihr Schalter in der Liste"${busy ? ' disabled' : ''}>${ico('refresh')}${TWG.busy === 'apply' ? 'Wird angewendet…' : failed ? 'Nochmal versuchen' : 'Wieder anwenden'}</button>
    </div>`;
  twgRefocus();
}
function twgRowHtml(i) {
  const sub = i.error ? `<small class="twg-err">${ico('alert')}${esc(i.error)}</small>` : i.detail ? `<small>${esc(i.detail)}</small>` : '';
  const tag = i.restart ? '<span class="eff w" title="Wirkt erst nach einem Neustart">Neustart</span>' : '';
  return `<div class="twg-row${i.error ? ' bad' : ''}"><span class="twg-ri">${ico(i.icon || 'zap')}</span><span class="twg-rt"><b>${esc(i.name)}</b>${tag}${sub}</span></div>`;
}
function twgDoneHtml(d) {
  const one = d.names.length === 1, who = twgJoin(d.names);
  const text = d.kind === 'apply'
    ? `${who} ${one ? 'ist' : 'sind'} wieder aktiv.${d.restart ? ' Was „Neustart“ trägt, wirkt erst nach einem Neustart des PCs.' : ''}${d.fps ? ' Das FPS-Limit gilt ab dem nächsten Start von FiveM.' : ''}`
    : `OrbitTweak fragt bei ${who} nicht mehr nach und ändert nichts. Schaltest du das wieder mit OrbitTweak ein, passt der Wächter dort wieder auf.`;
  return `<div class="twg-ic">${ico(d.kind === 'apply' ? 'check' : 'shield')}</div>
    <div class="twg-tx"><b class="twg-t">${d.kind === 'apply' ? 'Wieder angewendet' : 'Bleibt so'}</b><p class="twg-p">${esc(text)}</p></div>
    <div class="twg-act"><button class="ib twg-x" type="button" data-twg="close" title="Schließen" aria-label="Schließen">${ico('x')}</button></div>`;
}
/** The quiet line under the page description: what the guard does right now, with "Jetzt prüfen". */
function twgLineRender() {
  const line = $('#twgLine'); if (!line) return;
  const s = TWG.st;
  if (!s) { line.innerHTML = ''; return; }
  const n = (s.items || []).length, last = twgTime(s.lastAt);
  let text, warn = false, can = true, tip = '';
  if (TWG.busy === 'check' || s.status === 'busy') { text = 'prüft gerade…'; can = false; }
  else if (s.status === 'paused') { text = 'pausiert, solange ein Spiel läuft'; can = false; tip = 'Während FiveM oder ein anderes Spiel läuft, liest der Wächter nichts'; }
  else if (s.status === 'error') { text = 'die letzte Prüfung ging\u00a0nicht'; warn = true; tip = s.error || ''; }
  else if (s.status === 'wait') text = 'schaut gleich zum ersten Mal nach';
  else if (n) text = `${n} nicht mehr aktiv${last ? ' · geprüft um\u00a0' + last : ''}`;
  else if (s.confirmAt) text = `schaut um\u00a0${twgTime(s.confirmAt)} noch einmal nach`;
  else if (!s.watched) text = 'passt auf, sobald du Tweaks mit OrbitTweak einschaltest';
  else text = `${s.watched === 1 ? '1 Tweak' : s.watched + ' Tweaks'} wie eingestellt${last ? ' · geprüft um\u00a0' + last : ''}`;
  line.classList.toggle('twg-warn', warn);
  line.title = tip;
  line.innerHTML = `${ico(warn ? 'alert' : 'shield')}<b>Tweak-Wächter</b><span>${esc(text)}</span>`
    + `<button class="twg-now" type="button" data-twg="check"${can ? '' : ' disabled'}>${warn ? 'Nochmal prüfen' : 'Jetzt prüfen'}</button>`;
}

function twgSet(st) {
  // no toast for something new: the badge on "Tweaks" and the line in "Letzte Aktivität" say it calmly
  const before = new Set(((TWG.st && TWG.st.items) || []).map(i => i.id));
  TWG.st = st;
  if (((st && st.items) || []).some(i => !before.has(i.id)) && TWG.done) twgCloseDone(true);
  twgRender();
}
async function twgLoad() {
  if (TWG.loading) return TWG.loading;
  TWG.loading = (async () => {
    try { twgSet(await api('twg.state')); } catch { /* locked or not there yet: the next look sends an event */ }
    finally { TWG.loading = null; }
  })();
  return TWG.loading;
}
async function twgCheck() {
  if (TWG.busy) return;
  TWG.busy = 'check'; twgLineRender();
  let r; try { r = await api('twg.check'); } catch (e) { r = null; toast('Nicht geprüft', e.message, false); }
  TWG.busy = '';
  if (r) {
    twgSet(r);
    if (r.status === 'paused') toast('Gerade nicht', 'Solange ein Spiel läuft, prüft der Tweak-Wächter nichts', false);
    else if (r.status === 'error') toast('Nicht geprüft', r.error || '', false);
    else if (!(r.items || []).length && !r.confirmAt) toast('Alles aktiv', r.watched ? 'Deine Tweaks sind so, wie OrbitTweak sie eingestellt hat' : 'Noch keine Tweaks von OrbitTweak eingeschaltet');
  } else twgRender();
}
async function twgApply() {
  const items = (TWG.st && TWG.st.items) || []; if (!items.length || TWG.busy) return;
  TWG.busy = 'apply'; twgRender();
  let r; try { r = await api('twg.apply', { ids: items.map(i => i.id) }); } catch (e) { r = { error: e.message }; }
  TWG.busy = '';
  if (!r || r.error) { twgRender(); return toast('Nicht wieder angewendet', (r && r.error) || '', false); }
  TWG.st = r.state;
  if (r.failed.length) toast(r.done.length ? 'Nicht alles wieder angewendet' : 'Nicht wieder angewendet', r.failed.map(f => `${f.name}: ${f.error}`).join(' · '), false);
  if (r.done.length && !r.failed.length) twgShowDone({ kind: 'apply', names: r.done.map(d => d.name), restart: r.restart, fps: r.done.some(d => d.id === TWG_FPS) });
  else { if (r.done.length) toast('Wieder angewendet', twgJoin(r.done.map(d => d.name))); twgRender(); }
  if (r.done.length && typeof loadTweaks === 'function') loadTweaks();
  if (r.done.some(d => d.id === TWG_FPS) && typeof drvLoad === 'function' && curPage === 'gfx') drvLoad();
}
async function twgKeep() {
  const items = (TWG.st && TWG.st.items) || []; if (!items.length || TWG.busy) return;
  TWG.busy = 'keep'; twgRender();
  let r; try { r = await api('twg.keep', { ids: items.map(i => i.id) }); } catch (e) { r = { error: e.message }; }
  TWG.busy = '';
  if (!r || r.error) { twgRender(); return toast('Nicht gespeichert', (r && r.error) || '', false); }
  TWG.st = r.state;
  if (r.kept.length) twgShowDone({ kind: 'keep', names: r.kept }); else twgRender();
}
function twgShowDone(d) {
  TWG.done = d; clearTimeout(TWG.doneT);
  TWG.doneT = setTimeout(() => twgCloseDone(), 9000);
  // the close button takes the keyboard focus the pressed button had (it is gone with the banner's old content)
  if (TWG.focus) TWG.focus = 'close';
  twgRender();
}
function twgCloseDone(quiet) {
  clearTimeout(TWG.doneT); TWG.doneT = 0;
  if (!TWG.done) return;
  TWG.done = null;
  if (!quiet) twgRender();
}

// hooks: the page, host events, and a fresh look at the pending ones after the tweak list reloads (a switch flipped by
// hand, Boost or "Alles zurücksetzen" may have settled them; the host reads only those)
{ const prev = PAGE_SHOW.tweaks; PAGE_SHOW.tweaks = () => { if (prev) prev(); twgLoad(); }; }
{ const prev = PAGE_SHOW.home; PAGE_SHOW.home = () => { if (prev) prev(); if (TWG.done) twgCloseDone(); }; }
EVENT_EXT.twg = d => twgSet(d);
if (typeof loadTweaks === 'function') {
  const prevLoad = loadTweaks;
  loadTweaks = async function (...args) {   // eslint-disable-line no-func-assign
    const r = await prevLoad.apply(this, args);
    if (TWG.st && (TWG.st.items || []).length && !TWG.busy) twgLoad();
    return r;
  };
}
twgMount();
[2500, 9000, 25000].forEach(ms => setTimeout(() => { if (!TWG.st || TWG.st.status === 'wait') twgLoad(); }, ms));

/** The palette's entry (Strg+K): with something waiting it leads there, else it starts a look. */
function twgPalItem() {
  const items = (TWG.st && TWG.st.items) || [];
  const kw = 'wächter tweak zurückgesetzt weg aus verschwunden windows update treiber kontrolle prüfen';
  return items.length
    ? { id: 'ac:twg', g: 'action', icon: 'twg-alert', title: twgTitle(items), sub: 'Tweaks › Wieder anwenden oder so lassen', kw, st: String(items.length), verb: 'Ansehen', run: () => go('tweaks') }
    : { id: 'ac:twg', g: 'action', icon: 'shield', title: 'Tweak-Wächter: jetzt prüfen', sub: 'Schaut, ob noch alle Tweaks von OrbitTweak aktiv sind', kw, verb: 'Ausführen', run: () => { go('tweaks'); return twgCheck(); } };
}

/* ======================= MOCK (browser preview) =======================
   The host's shapes. ?twg= live (default: two tweaks reset), fps (a tweak and the FPS limit), fpsonly (the FPS limit),
   fail ("Wieder anwenden" fails for one), empty, none (nothing watched yet), wait (first look not done), confirm (a
   second look planned), paused (a game runs), checkerr (the last look failed). */
const TWG_MODE = new URLSearchParams(location.search).get('twg') || 'live';
const TWG_MOCK = (() => {
  const ago = m => new Date(Date.now() - m * 60000).toISOString();
  const tw = id => { const t = Mock.tweaks.find(x => x.id === id); return { id, name: t.name, icon: t.icon, cat: t.cat, restart: t.badge === 'NEUSTART', detail: null, since: ago(19), error: null }; };
  const fps = { id: TWG_FPS, name: 'FPS-Limit für FiveM', icon: 'gauge', cat: 'fivem', restart: false, detail: 'Im Treiberprofil stehen jetzt 100 statt 141 FPS', since: ago(19), error: null };
  const items = { live: ['accel', 'dvr'], fail: ['accel', 'dvr'], fps: ['accel'] }[TWG_MODE] || [];
  const st = {
    status: { wait: 'wait', paused: 'paused', checkerr: 'error' }[TWG_MODE] || 'idle',
    lastAt: TWG_MODE === 'wait' ? null : ago(4), nextAt: new Date(Date.now() + 11 * 60000).toISOString(),
    confirmAt: TWG_MODE === 'confirm' ? new Date(Date.now() + 60000).toISOString() : null,
    error: TWG_MODE === 'checkerr' ? 'Die letzte Prüfung ging nicht: Der Zugriff auf den Registrierungsschlüssel wurde verweigert.' : null,
    watched: TWG_MODE === 'none' ? 0 : 7,
    items: items.map(tw).concat(TWG_MODE === 'fps' || TWG_MODE === 'fpsonly' ? [fps] : []),
  };
  return { st, copy: () => JSON.parse(JSON.stringify(st)) };
})();
MOCK_EXT['twg.state'] = async () => {
  // like the host: the pending ones read again, a tweak switched on by hand since is gone
  const s = TWG_MOCK.st;
  s.items = s.items.filter(i => i.id === TWG_FPS || !Mock.tweaks.find(t => t.id === i.id)?.applied);
  return TWG_MOCK.copy();
};
MOCK_EXT['twg.check'] = async () => {
  const s = TWG_MOCK.st;
  if (s.status === 'paused') return TWG_MOCK.copy();
  onEvent('twg', { ...TWG_MOCK.copy(), status: 'busy' });
  await sleep(900);
  s.status = 'idle'; s.error = null; s.lastAt = new Date().toISOString(); s.confirmAt = null;
  return TWG_MOCK.copy();
};
MOCK_EXT['twg.apply'] = async a => {
  await sleep(700);
  const s = TWG_MOCK.st, done = [], failed = []; let restart = false;
  for (const it of s.items.filter(i => (a.ids || []).includes(i.id))) {
    if (TWG_MODE === 'fail' && it.id === 'dvr') {
      it.error = 'Dein Clipper nimmt über die Game Bar auf – wähl dort erst einen anderen Recorder';
      failed.push({ id: it.id, name: it.name, error: it.error });
      onEvent('log', { text: `✖ ${it.name}: ${it.error}` });
      continue;
    }
    const t = Mock.tweaks.find(x => x.id === it.id); if (t) t.applied = true;
    done.push({ id: it.id, name: it.name }); if (it.restart) restart = true;
    onEvent('log', { text: it.id === TWG_FPS ? '🎯 FPS-Limit für FiveM: 141 FPS ab dem nächsten Start' : '✔ ' + it.name });
  }
  s.items = s.items.filter(i => !done.some(d => d.id === i.id));
  return { done, failed, restart, state: TWG_MOCK.copy() };
};
MOCK_EXT['twg.keep'] = async a => {
  const s = TWG_MOCK.st, kept = s.items.filter(i => (a.ids || []).includes(i.id)).map(i => i.name);
  s.items = s.items.filter(i => !(a.ids || []).includes(i.id));
  s.watched = Math.max(0, s.watched - kept.length);   // their marks are gone: no longer watched
  if (kept.length) onEvent('log', { text: `🛡 So gelassen: ${twgJoin(kept)} · der Tweak-Wächter fragt dort nicht mehr nach` });
  return { kept, state: TWG_MOCK.copy() };
};
// the preview's host: its first look found the reset ones (the line in "Letzte Aktivität" as the host writes it)
if (!HOST) setTimeout(() => {
  const names = TWG_MOCK.st.items.map(i => i.name);
  if (names.length) onEvent('log', { text: `🛡 ${names.length === 1 ? 'Nicht mehr aktiv' : names.length + ' Tweaks nicht mehr aktiv'}: ${twgJoin(names)} · unter Tweaks wieder anwenden oder so lassen` });
  onEvent('twg', TWG_MOCK.copy());
}, 1800);
