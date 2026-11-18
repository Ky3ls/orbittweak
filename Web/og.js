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

/* ---------- page: placeholder until the page itself is built ---------- */
const OG = { games: null, game: null, items: null };
async function ogLoad() {
  try { OG.games = (await api('og.games')).games || []; } catch { OG.games = []; }
  const el = $('#p-og'); if (!el) return;
  el.innerHTML = `<div class="ph"><div><div class="kicker">Andere Spiele</div><h1>Tweaks für <span class="grad">andere Spiele</span></h1></div></div>`
    + OG.games.map(g => `<div class="card"><b>${esc(g.name)}</b> ${g.installed ? 'installiert' : 'nicht gefunden'}</div>`).join('');
}
PAGE_SHOW.og = () => ogLoad();

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
