'use strict';
/* 2.31 – Einstellungs-Backup (Einstellungen › Sicherheit): FiveM's key bindings, graphics and CitizenFX.ini as a zip
   under %AppData%\OrbitTweak\backups, written back on request, automatically once a day when FiveM starts.
   Host: MainWindow.CfgBackup.cs, Core/CfgBackup.cs, Core/CfgBackupLogic.cs. The card is drawn into #st-safety. */

const BK = {
  st: null, err: '', at: 0, loading: null,
  busy: '',          // 'create' | 'restore:<name>' | 'delete:<name>' | 'auto'
  msg: null,         // the banner under the main action: { ok, title, text, undo? }
  all: false,        // the whole list instead of the newest few
};
const BK_SHOW = 5;
const BK_KIND_ICO = { manual: 'bk-archive', auto: 'clock', safety: 'shield' };
const BK_FMT_T = new Intl.DateTimeFormat('de-DE', { hour: '2-digit', minute: '2-digit' });
const BK_FMT_D = new Intl.DateTimeFormat('de-DE', { day: 'numeric', month: 'long' });
const BK_FMT_Y = new Intl.DateTimeFormat('de-DE', { day: 'numeric', month: 'long', year: 'numeric' });
const bkList = l => new Intl.ListFormat('de', { type: 'conjunction' }).format(l);
const bkN = (n, one, many) => `${n} ${n === 1 ? one : many}`;

/** "Heute, 14:02", "Gestern, 21:17", "19. November, 18:40", "3. März 2025, 09:12". */
function bkWhen(iso, lower) {
  const d = iso ? new Date(iso) : null;
  if (!d || isNaN(d) || d.getFullYear() < 2000) return 'Unbekannt';
  const day = x => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
  const diff = Math.round((day(new Date()) - day(d)) / 864e5);
  const t = BK_FMT_T.format(d);
  const s = diff === 0 ? `Heute, ${t}` : diff === 1 ? `Gestern, ${t}`
    : `${(d.getFullYear() === new Date().getFullYear() ? BK_FMT_D : BK_FMT_Y).format(d)}, ${t}`;
  return lower && diff <= 1 ? s[0].toLowerCase() + s.slice(1) : s;
}

/** "von heute, 14:02", "vom 19. November, 18:40" (inside a sentence). */
const bkOf = iso => { const w = bkWhen(iso, true); return /^(heute|gestern)/.test(w) ? 'von ' + w : /^\d/.test(w) ? 'vom ' + w : w; };

/** The card's frame, made once (below "Wiederherstellen"). */
function bkFrame() {
  if ($('#bkCard') || !$('#st-safety')) return;
  $('#st-safety').insertAdjacentHTML('beforeend', `
  <div class="card mt bk-card" id="bkCard">
    <div class="sh"><b>FiveM-Einstellungen sichern</b><span class="sub">Tastenbelegung, Grafik und FiveM-Optionen</span></div>
    <div class="bk-src" id="bkSrc"></div>
    <div class="bk-go"><span class="bk-last" id="bkLast">Wird gelesen…</span><button class="btn p" id="bkMake" type="button">${ico('bk-archive')}<span>Jetzt sichern</span></button></div>
    <div id="bkMsg"></div>
    <div class="srow bk-auto"><div><b>Automatisch sichern</b><p>Höchstens einmal am Tag beim Start von FiveM und nur, wenn sich etwas geändert hat. Die letzten 10 automatischen bleiben, deine eigenen löscht OrbitTweak nie.</p></div>
      <div class="tg" id="bkAutoTg" role="switch" tabindex="0" aria-checked="false" aria-label="Automatisch sichern"></div></div>
    <div class="bk-lh"><b>Sicherungen</b><i class="bk-n" id="bkN"></i><span class="sp"></span><button class="btn sm gh" id="bkDir" type="button">${ico('folder')}Ordner öffnen</button></div>
    <div id="bkRows"></div>
    <p class="bk-foot" id="bkFoot">Nur auf deinem PC. Nicht dabei sind Caches, die Daten der Server und deine Rockstar-Anmeldung.</p>
  </div>`);
}

function bkSrc(s) {
  // the size when it is there; a reason (FiveM folder not found, too big) takes the place of what is in it
  const tag = s.exists ? `<span class="bk-ssz">${esc(bytes(s.size))}</span>` : '<em class="bk-stag">Nicht vorhanden</em>';
  return `<div class="bk-s${s.exists && !s.note ? '' : ' off'}" title="${esc(s.path)}">
    <span class="bk-sic">${ico(s.key === 'fivemcfg' ? 'keyboard' : s.key === 'ini' ? 'file' : 'monitor')}</span>
    <span class="bk-stx"><span class="bk-sn"><b>${esc(s.name)}</b>${tag}</span><span class="bk-sw${s.note ? ' note' : ''}">${esc(s.note || s.what)}</span></span>
  </div>`;
}

function bkRow(it) {
  const busyR = BK.busy === 'restore:' + it.name, busyD = BK.busy === 'delete:' + it.name, any = !!BK.busy;
  const files = it.files || [];
  const kind = it.ok ? `<em class="bk-k">${esc(it.kindLabel)}</em>` : '<em class="bk-k bad">Beschädigt</em>';
  const sub = it.ok ? [kind, bkN(files.length, 'Datei', 'Dateien'), bytes(it.size)].join('<i>·</i>') : `${kind}<i>·</i>${esc(it.error || '')}`;
  return `<div class="bk-row${it.ok ? '' : ' bad'}" data-bkname="${esc(it.name)}">
    <span class="bk-ic">${ico(it.ok ? (BK_KIND_ICO[it.kind] || 'bk-archive') : 'alert')}</span>
    <span class="bk-tx"><b>${esc(bkWhen(it.created))}</b><span class="bk-sub">${sub}</span>${it.ok ? `<span class="bk-fl">${esc(files.map(f => f.name).join(' · '))}</span>` : ''}</span>
    ${it.ok ? `<button class="btn sm" type="button" data-bkres="${esc(it.name)}"${any ? ' disabled' : ''}>${busyR ? '<i class="bk-ring"></i>Stelle wieder her…' : ico('undo') + 'Wiederherstellen'}</button>` : ''}
    <button class="bk-del" type="button" data-bkdel="${esc(it.name)}" title="Sicherung löschen" aria-label="Sicherung vom ${esc(bkWhen(it.created))} löschen"${any ? ' disabled' : ''}>${busyD ? '<i class="bk-ring"></i>' : ico('trash')}</button>
  </div>`;
}

function bkEmpty(icon, title, text, cls = '', btn = '') {
  return `<div class="bk-empty ${cls}">${ico(icon)}<b>${esc(title)}</b><span>${esc(text)}</span>${btn}</div>`;
}

function bkRender() {
  bkFrame();
  const st = BK.st, items = (st && st.items) || [], src = (st && st.sources) || [];
  const have = src.filter(s => s.exists && !s.note);

  // what would be saved now
  const sEl = $('#bkSrc');
  if (sEl) sEl.innerHTML = st ? src.map(bkSrc).join('') : (BK.err ? '' : '<div class="bk-s wait"></div><div class="bk-s wait"></div>');

  // the main action and when the last backup was made
  const make = $('#bkMake'), last = $('#bkLast');
  const newest = items.find(i => i.ok);
  if (last) {
    last.classList.toggle('warn', !!st && !have.length);
    last.innerHTML = !st ? (BK.err ? 'Nicht gelesen' : 'Wird gelesen…')
      : !have.length ? `${ico('alert')}Keine FiveM-Einstellungen gefunden. Starte FiveM einmal, dann legt FiveM sie an.`
      : newest ? `Zuletzt gesichert <b>${esc(bkWhen(newest.created, true))}</b>${newest.kind === 'auto' ? ' (automatisch)' : ''}`
      : 'Noch keine Sicherung';
  }
  if (make) {
    make.disabled = !st || !have.length || !!BK.busy;
    make.innerHTML = BK.busy === 'create' ? '<i class="bk-ring"></i><span>Sichere…</span>' : `${ico('bk-archive')}<span>Jetzt sichern</span>`;
  }

  // the result of the last action
  const m = $('#bkMsg');
  if (m) m.innerHTML = BK.msg ? `<div class="bk-msg ${BK.msg.ok ? 'ok' : 'bad'}" role="status">
      <span class="bk-mi">${ico(BK.msg.ok ? 'check' : 'alert')}</span>
      <div class="bk-mt"><b>${esc(BK.msg.title)}</b>${BK.msg.text ? `<span>${esc(BK.msg.text)}</span>` : ''}</div>
      ${BK.msg.undo ? `<button class="btn sm" type="button" data-bkundo="${esc(BK.msg.undo)}"${BK.busy ? ' disabled' : ''}>${ico('undo')}Rückgängig</button>` : ''}
      <button class="bk-x" type="button" data-bkclose aria-label="Hinweis schließen">${ico('x')}</button></div>` : '';

  // automatic
  const tg = $('#bkAutoTg');
  if (tg) {
    const on = !!(st && st.auto);
    tg.classList.toggle('on', on); tg.classList.toggle('busy', BK.busy === 'auto' || !st);
    tg.setAttribute('aria-checked', String(on));
    tg.title = on ? 'Sichert beim Start von FiveM' : 'Nur, wenn du sicherst';
    tg.closest('.bk-auto')?.classList.toggle('hidden', !st);   // not read yet: no switch that looks "off"
  }

  // the list
  const n = $('#bkN'); if (n) n.textContent = st ? String(items.length) : '';
  const dir = $('#bkFoot'); if (dir && st) dir.innerHTML = `Nur auf deinem PC unter <code>${esc(st.dir)}</code>. Nicht dabei sind Caches, die Daten der Server und deine Rockstar-Anmeldung.`;
  const rows = $('#bkRows'); if (!rows) return;
  if (!st) {
    rows.innerHTML = BK.err
      ? bkEmpty('alert', 'Sicherungen nicht gelesen', BK.err, 'bad', `<button class="btn sm" type="button" data-bkretry>${ico('refresh')}Noch mal versuchen</button>`)
      : bkEmpty('bk-archive', 'Lese deine Sicherungen', 'Einen Moment', 'wait');
    return;
  }
  if (!items.length) {
    rows.innerHTML = bkEmpty('bk-archive', 'Noch keine Sicherung', have.length
      ? 'Sichere jetzt, bevor du an Tasten oder Grafik drehst. Mit „Automatisch sichern“ passiert es beim nächsten Start von FiveM von selbst.'
      : 'Sobald FiveM einmal gelaufen ist, kannst du hier sichern.');
    return;
  }
  const shown = BK.all ? items : items.slice(0, BK_SHOW);
  rows.innerHTML = shown.map(bkRow).join('')
    + (items.length > BK_SHOW ? `<button class="bk-more" type="button" data-bkmore>${BK.all ? 'Weniger zeigen' : `Alle ${items.length} zeigen`}${ico('chev')}</button>` : '');
  rows.querySelector('.bk-more')?.classList.toggle('open', BK.all);
}

async function bkLoad(quiet) {
  if (BK.loading) return BK.loading;
  if (!BK.st) { BK.err = ''; bkRender(); }
  BK.loading = (async () => {
    try { BK.st = await api('bk.state'); BK.err = ''; BK.at = Date.now(); }
    catch (e) {
      BK.err = (e && e.message) || 'Unbekannter Fehler';
      if (BK.st && !quiet) toast('Sicherungen nicht neu gelesen', BK.err, false);
    }
    bkRender();
  })().finally(() => { BK.loading = null; });
  return BK.loading;
}

async function bkCreate() {
  if (BK.busy) return;
  BK.busy = 'create'; BK.msg = null; bkRender();
  try {
    const r = await api('bk.create');
    const f = (r.item && r.item.files) || [];
    const miss = (r.skipped || []).map(s => `${s.name}: ${s.why}`);
    BK.msg = { ok: true, title: 'Gesichert', text: `${bkN(f.length, 'Datei', 'Dateien')} · ${bytes(r.item.size)}` + (miss.length ? `. Nicht dabei: ${miss.join(', ')}` : '') };
  } catch (e) {
    BK.msg = { ok: false, title: 'Nicht gesichert', text: (e && e.message) || 'Unbekannter Fehler' };
  }
  BK.busy = ''; await bkLoad(true);
}

/** The question before a restore: which files (all ticked), FiveM closed, what happens to the rest. */
function bkAsk(it) {
  return new Promise(res => {
    const files = it.files || [];
    const pick = new Set(files.map(f => f.key));
    const running = !!(BK.st && BK.st.running);
    modal(`<div class="m-cf"><div class="m-ic">${ico('undo')}</div><div><h1>Sicherung wiederherstellen?</h1>
        <p>Sicherung ${esc(bkOf(it.created))}. Vorher sichert OrbitTweak deinen jetzigen Stand, damit du zurückkannst.</p></div></div>
      <div class="bk-pick" id="bkPick">${files.map(f => `<button class="bk-pr on" type="button" data-bkpk="${esc(f.key)}" role="checkbox" aria-checked="true">
        <span class="bk-cb"><svg viewBox="0 0 24 24" fill="none" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg></span>
        <span class="bk-pt"><b>${esc(f.name)}</b><small>${esc(f.what)} · ${esc(bytes(f.size))}</small></span></button>`).join('')}</div>
      <p class="bk-mnote${running ? ' warn' : ''}" id="bkMNote">${running ? ico('alert') + 'FiveM läuft gerade. Schließ es zuerst, sonst schreibt FiveM beim Beenden seine Einstellungen darüber.' : 'FiveM muss geschlossen sein. Was du abwählst, bleibt wie es ist.'}</p>
      <div class="act"><button class="btn lg" id="bkMNo" type="button">Abbrechen</button><button class="btn lg p" id="bkMYes" type="button">${ico('undo')}Wiederherstellen</button></div>`);
    const yes = $('#bkMYes');
    $('#bkPick').onclick = e => {
      const b = e.target.closest('[data-bkpk]'); if (!b) return;
      const k = b.dataset.bkpk; pick.has(k) ? pick.delete(k) : pick.add(k);
      b.classList.toggle('on', pick.has(k)); b.setAttribute('aria-checked', String(pick.has(k)));
      yes.disabled = !pick.size;
    };
    $('#bkMNo').onclick = () => { closeModal(); res(null); };
    yes.onclick = () => { closeModal(); res([...pick]); };
  });
}

async function bkRestore(name, undo) {
  const it = BK.st && BK.st.items.find(i => i.name === name);
  if (!it || BK.busy) return;
  let keys = null;
  if (!undo) { keys = await bkAsk(it); if (!keys || BK.busy) return; }
  BK.busy = 'restore:' + name; BK.msg = null; bkRender();
  try {
    const r = await api('bk.restore', { name, keys: keys || [] });
    const done = r.restored || [], same = r.unchanged || [], bad = [...(r.failed || []), ...(r.skipped || [])];
    const notes = [];
    if (same.length) notes.push(`${bkList(same)} ${same.length === 1 ? 'war' : 'waren'} schon so`);
    if (bad.length) notes.push(`nicht geschrieben: ${bad.map(s => `${s.name} (${s.why})`).join(', ')}`);
    if (done.length) {
      BK.msg = { ok: !(r.failed || []).length, title: undo ? 'Rückgängig gemacht' : done.length === 1 ? `${done[0]} wiederhergestellt` : `${done.length} Dateien wiederhergestellt`,
        text: [done.length > 1 ? bkList(done) : '',
          !r.safety ? '' : r.safetyNew ? 'Dein vorheriger Stand liegt als „Vor dem Wiederherstellen“ in der Liste' : 'Dein vorheriger Stand war schon gesichert',
          ...notes].filter(Boolean).join('. ') + '. FiveM liest die Einstellungen beim nächsten Start.',
        undo: undo ? null : r.safety };
    } else if (same.length && !bad.length) {
      BK.msg = { ok: true, title: 'Schon auf diesem Stand', text: 'Deine Einstellungen gleichen der Sicherung, es wurde nichts geändert.' };
    } else {
      BK.msg = { ok: false, title: 'Nichts wiederhergestellt', text: notes.join('. ') || 'Keine Datei ließ sich schreiben.' };
    }
  } catch (e) {
    BK.msg = { ok: false, title: undo ? 'Nicht rückgängig gemacht' : 'Nicht wiederhergestellt', text: (e && e.message) || 'Unbekannter Fehler' };
  }
  BK.busy = ''; await bkLoad(true);
}

async function bkDelete(name) {
  const it = BK.st && BK.st.items.find(i => i.name === name);
  if (!it || BK.busy) return;
  const ok = await confirmBox('Sicherung löschen?', `Die Sicherung ${bkOf(it.created)} wird endgültig gelöscht. Deine Einstellungen in FiveM bleiben, wie sie sind.`, { ok: 'Löschen', danger: true });
  if (!ok || BK.busy) return;
  BK.busy = 'delete:' + name; bkRender();
  try {
    await api('bk.delete', { name });
    if (BK.msg && BK.msg.undo === name) BK.msg = null;   // the undo pointed at it
    toast('Sicherung gelöscht', bkWhen(it.created));
  } catch (e) { toast('Nicht gelöscht', (e && e.message) || '', false); }
  BK.busy = ''; await bkLoad(true);
}

async function bkAuto() {
  if (!BK.st || BK.busy) return;
  const want = !BK.st.auto;
  BK.busy = 'auto'; BK.st.auto = want; bkRender();
  try { const r = await api('bk.auto', { on: want }); BK.st.auto = !!(r && r.auto); }
  catch (e) { BK.st.auto = !want; toast('Nicht umgeschaltet', (e && e.message) || '', false); }
  BK.busy = ''; bkRender();
}

/** The tab was opened: what is shown is read fresh when it is older than a quarter minute. */
function bkShow() { if (!BK.st || Date.now() - BK.at > 15000) bkLoad(true); else bkRender(); }

bkFrame();
bkRender();
$('#st-safety')?.addEventListener('click', e => {
  if (e.target.closest('#bkMake')) return bkCreate();
  const r = e.target.closest('[data-bkres]'); if (r) return bkRestore(r.dataset.bkres);
  const d = e.target.closest('[data-bkdel]'); if (d) return bkDelete(d.dataset.bkdel);
  const u = e.target.closest('[data-bkundo]'); if (u) return bkRestore(u.dataset.bkundo, true);
  if (e.target.closest('[data-bkclose]')) { BK.msg = null; return bkRender(); }
  if (e.target.closest('#bkAutoTg')) return bkAuto();
  if (e.target.closest('[data-bkmore]')) { BK.all = !BK.all; return bkRender(); }
  if (e.target.closest('[data-bkretry]')) return bkLoad(false);
  if (e.target.closest('#bkDir')) return api('bk.folder').catch(err => toast('Ordner nicht geöffnet', (err && err.message) || '', false));
});
$('#st-safety')?.addEventListener('keydown', e => {
  if ((e.key === ' ' || e.key === 'Enter') && e.target.id === 'bkAutoTg') { e.preventDefault(); if (!e.repeat) bkAuto(); }
});
// the safety tab becomes visible (a click on the tab, the palette, go('settings') with it open)
if ($('#st-safety')) new MutationObserver(() => { if (!$('#st-safety').classList.contains('hidden')) bkShow(); })
  .observe($('#st-safety'), { attributes: true, attributeFilter: ['class'] });
{
  const prev = PAGE_SHOW.settings;
  PAGE_SHOW.settings = () => { prev?.(); if (!$('#st-safety')?.classList.contains('hidden')) bkShow(); };
}
// an automatic backup was made while OrbitTweak is open
EVENT_EXT.bkauto = () => { BK.at = 0; if (curPage === 'settings' && !$('#st-safety')?.classList.contains('hidden')) bkLoad(true); };

/* ---------- browser preview: the same shapes as MainWindow.CfgBackup.cs returns ---------- */
const BK_MOCK = {
  auto: true, running: false, fail: '',   // fail: 'state' | 'create' | 'restore' (preview of the error states)
  sources: [
    { key: 'fivemcfg', name: 'fivem.cfg', what: 'Tastenbelegung und Spiel-Einstellungen', path: '%APPDATA%\\CitizenFX\\fivem.cfg', exists: true, size: 47312, modified: null, note: null },
    { key: 'gfx', name: 'gta5_settings.xml', what: 'Grafik', path: '%APPDATA%\\CitizenFX\\gta5_settings.xml', exists: true, size: 8094, modified: null, note: null },
    { key: 'ini', name: 'CitizenFX.ini', what: 'GTA-V-Ordner und Update-Kanal', path: '<FiveM.app>\\CitizenFX.ini', exists: true, size: 312, modified: null, note: null },
    { key: 'gtaxml', name: 'settings.xml', what: 'Grafik älterer FiveM-Installationen', path: '%LOCALAPPDATA%\\Rockstar Games\\GTA V\\settings.xml', exists: false, size: 0, modified: null, note: null },
  ],
  items: [],
};
{
  const M = BK_MOCK;
  const pad = n => String(n).padStart(2, '0');
  const nameOf = d => `fivem-${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}_${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}.zip`;
  const LABEL = { manual: 'Von dir', auto: 'Automatisch', safety: 'Vor dem Wiederherstellen' };
  const filesNow = () => M.sources.filter(s => s.exists && !s.note).map(s => ({ key: s.key, name: s.name, what: s.what, size: s.size }));
  const item = (d, kind, files = filesNow(), extra = {}) => ({ name: nameOf(d), created: d.toISOString(), kind, kindLabel: LABEL[kind], size: Math.round(files.reduce((a, f) => a + f.size, 0) * .23) + 640, files, ok: true, error: null, ...extra });
  const ago = (days, h, m) => { const d = new Date(); d.setDate(d.getDate() - days); d.setHours(h, m, 12, 0); return d; };
  const two = filesNow().filter(f => f.key !== 'ini');
  const earlier = new Date(Date.now() - 2.4 * 3600e3); earlier.setSeconds(12, 0);
  M.items = [
    item(earlier, 'manual'),
    item(ago(1, 21, 17), 'auto'),
    item(ago(3, 18, 40), 'auto', two),
    item(ago(6, 20, 5), 'safety'),
    item(ago(9, 19, 31), 'auto', two),
    item(ago(20, 16, 12), 'manual', two),
    { name: nameOf(ago(41, 12, 0)), created: ago(41, 12, 0).toISOString(), kind: null, kindLabel: 'Unbekannt', size: 9120, files: [], ok: false, error: 'Die Sicherung ist beschädigt: fivem.cfg passt nicht zur Prüfsumme.' },
  ];
  M.seed = M.items.map(i => ({ ...i }));
  const sort = () => M.items.sort((a, b) => b.created.localeCompare(a.created));
  MOCK_EXT['bk.state'] = async () => {
    await sleep(220);
    if (M.fail === 'state') throw new Error('Der Ordner %APPDATA%\\OrbitTweak\\backups ist gesperrt.');
    return { auto: M.auto, lastAuto: M.items.find(i => i.kind === 'auto')?.created || null, keepAuto: 10, dir: '%APPDATA%\\OrbitTweak\\backups', running: M.running,
      sources: M.sources.map(s => ({ ...s })), items: M.items.map(i => ({ ...i, files: i.files.map(f => ({ ...f })) })) };
  };
  MOCK_EXT['bk.create'] = async () => {
    await sleep(700);
    if (M.fail === 'create' || !filesNow().length) throw new Error('Keine FiveM-Einstellungen gefunden. Starte FiveM einmal, dann legt FiveM sie an.');
    const it = item(new Date(), 'manual'); M.items.unshift(it); sort();
    return { item: it, skipped: [] };
  };
  MOCK_EXT['bk.restore'] = async a => {
    await sleep(900);
    const it = M.items.find(i => i.name === a.name);
    if (!it) throw new Error('Diese Sicherung gibt es nicht mehr.');
    if (M.running || M.fail === 'restore') throw new Error('Bitte FiveM zuerst schließen. Sonst schreibt FiveM beim Beenden seine Einstellungen über die wiederhergestellten.');
    if (!it.ok) throw new Error(it.error + ' Es wurde nichts geändert.');
    const keys = a.keys && a.keys.length ? a.keys : it.files.map(f => f.key);
    if (keys.some(k => !it.files.some(f => f.key === k))) throw new Error('Diese Datei ist nicht in der Sicherung. Es wurde nichts geändert.');
    // the newest backup is close to what is there now: its CitizenFX.ini comes back as "unchanged"
    const same = it === M.items[0] ? it.files.filter(f => f.key === 'ini' && keys.includes(f.key)).map(f => f.name) : [];
    const pick = it.files.filter(f => keys.includes(f.key));
    const safety = item(new Date(), 'safety'); M.items.unshift(safety); sort();
    return { restored: pick.filter(f => !same.includes(f.name)).map(f => f.name), unchanged: same, skipped: [], failed: [], safety: safety.name, safetyNew: true };
  };
  MOCK_EXT['bk.delete'] = async a => { await sleep(160); M.items = M.items.filter(i => i.name !== a.name); return { ok: true }; };
  MOCK_EXT['bk.auto'] = async a => { await sleep(120); M.auto = !!a.on; return { auto: M.auto }; };
  MOCK_EXT['bk.folder'] = async () => ({ ok: true });
}
