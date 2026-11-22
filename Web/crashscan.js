'use strict';
/* 2.31 – Absturz-Analyse on the Diagnose page: FiveM's last ten crashes from its logs and its crash folder, the likely
   cause in plain words, the fixes that really exist on this PC and a short text for support (the host takes the Windows
   user name out of it). Nothing here guesses: an unclear cause says so and shows FiveM's raw words.
   Host: MainWindow.CrashScan.cs, Core/CrashScan.cs, Core/CrashScanLogic.cs (the researched formats are described there). */

const CRS = {
  data: null, err: '', loading: null, at: 0,
  open: new Set(),    // ids of crashes shown with their details
  seen: false,        // the newest crash opens by itself once
  busy: new Set(),    // fix buttons waiting for the host ("id|fix")
  done: new Set(),    // fixes that went through, shown as done until the list is read again
  copied: '',         // the crash whose text was just copied
  fk: null,           // the focused control of the card ([data key, value, crash id]) while it renders again
};
const CRS_SURE = { klar: 'Klarer Hinweis', wahrscheinlich: 'Wahrscheinlich', unklar: 'Unklar' };
const CRS_DAY = new Intl.DateTimeFormat('de-DE', { day: 'numeric', month: 'short' });
const CRS_YEAR = new Intl.DateTimeFormat('de-DE', { day: 'numeric', month: 'short', year: 'numeric' });
const CRS_CLOCK = new Intl.DateTimeFormat('de-DE', { hour: '2-digit', minute: '2-digit' });

/** "Heute, 20:30", "Gestern, 18:01", "19. Nov., 09:00" in the PC's time zone. */
function crsWhen(iso) {
  const d = iso ? new Date(iso) : null;
  if (!d || isNaN(d)) return 'Zeit unbekannt';
  const now = new Date(), day = x => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
  const diff = Math.round((day(now) - day(d)) / 864e5), t = CRS_CLOCK.format(d);
  if (diff === 0) return 'Heute, ' + t;
  if (diff === 1) return 'Gestern, ' + t;
  return (d.getFullYear() === now.getFullYear() ? CRS_DAY : CRS_YEAR).format(d) + ', ' + t;
}

/** Reads the crashes again (a running read is shared). */
function crsLoad() {
  if (CRS.loading) return CRS.loading;
  CRS.err = '';
  crsRender();
  CRS.loading = (async () => {
    try {
      const r = await api('crash.list');
      if (r && r.error) { CRS.err = r.error; CRS.data = null; }
      else { CRS.data = r; CRS.at = Date.now(); CRS.done.clear(); }
    } catch (e) { CRS.err = (e && e.message) || 'Unbekannter Fehler'; CRS.data = null; }
    finally { CRS.loading = null; }
    if (CRS.data && !CRS.seen) { CRS.seen = true; const first = CRS.data.items && CRS.data.items[0]; if (first) CRS.open.add(first.id); }
    crsRender();
  })();
  return CRS.loading;
}

/** Diagnose shown: read when nothing is there yet or the list is older than half a minute. */
function crsShow() { if (!CRS.data || Date.now() - CRS.at > 30000) crsLoad(); else crsRender(); }

/** Strg+K: Diagnose with the card in view. */
function crsFocus() {
  go('diag');
  setTimeout(() => {
    const c = $('#crsCard'); if (!c) return;
    c.scrollIntoView({ behavior: 'smooth', block: 'start' });
    c.classList.remove('crs-flash'); void c.offsetWidth; c.classList.add('crs-flash');
  }, 80);
}

function crsEmpty(kind, icon, title, text, extra = '') {
  return `<div class="lx-empty crs-empty ${kind}">${ico(icon)}<b>${esc(title)}</b><span>${esc(text)}</span>${extra}</div>`;
}

const CRS_FK = ['crsrow', 'crsfix', 'crscopy', 'crsrev', 'crsretry', 'crsgo'];

/** The card re-renders with innerHTML: the control that had the keyboard focus gets it back (also after it was
 *  disabled for a moment while a fix ran), so Tab and Enter keep working in the list. */
function crsKeepFocus(body) {
  const a = document.activeElement;
  if (a && body.contains(a)) { const k = CRS_FK.find(x => a.dataset[x] !== undefined); CRS.fk = k ? [k, a.dataset[k], a.dataset.crsfor || ''] : null; }
  else if (a && a !== document.body) CRS.fk = null;   // the user went on elsewhere
  return () => {
    if (!CRS.fk) return;
    const [k, v, f] = CRS.fk, attr = k.replace(/^crs/, 'data-crs');
    let el = body.querySelector(`[${attr}="${CSS.escape(v)}"]${f ? `[data-crsfor="${CSS.escape(f)}"]` : ''}`);
    if (el && el.disabled && k === 'crsfix' && CRS.busy.has(f + '|' + v)) return;   // back on it once the fix is through
    // a fix that is done stays disabled: its crash's row takes the focus
    if ((!el || el.disabled) && f) el = body.querySelector(`[data-crsrow="${CSS.escape(f)}"]`);
    if (!el || el.disabled) el = body.querySelector('[data-crsrow],[data-crsretry],[data-crsgo]');
    if (!el) return;   // skeleton while reading: the next render tries again
    el.focus({ preventScroll: true });
    CRS.fk = null;
  };
}

function crsRender() {
  const body = $('#crsBody'), sub = $('#crsSub'), rel = $('#crsReload');
  if (!body) return;
  const refocus = crsKeepFocus(body);
  crsPaint(body, sub);
  refocus();
  if (rel) rel.disabled = !!CRS.loading;
}

function crsPaint(body, sub) {
  const d = CRS.data;
  if (CRS.err) {
    sub.textContent = 'Nicht gelesen';
    body.innerHTML = crsEmpty('crs-bad', 'alert', 'Abstürze nicht gelesen', CRS.err, `<button class="btn sm" type="button" data-crsretry>${ico('refresh')}Erneut versuchen</button>`);
    return;
  }
  if (!d) {
    sub.textContent = 'Wird gelesen…';
    body.innerHTML = `<div class="crs-skel" aria-hidden="true">${'<i></i>'.repeat(3)}</div>`;
    return;
  }
  if (!d.found) {
    sub.textContent = 'FiveM nicht gefunden';
    body.innerHTML = crsEmpty('', 'crs-crack', 'FiveM nicht gefunden', 'Wähl im Launcher deinen FiveM-Ordner, dann liest OrbitTweak dessen Abstürze mit.', `<button class="btn sm" type="button" data-crsgo="launcher">${ico('rocket')}Zum Launcher</button>`);
    return;
  }
  const items = d.items || [];
  if (!items.length) {
    sub.textContent = 'Aus FiveMs Logs (FiveM behält sie sieben Tage) und dem Crash-Ordner';
    body.innerHTML = crsEmpty('ok', 'check', 'Keine Abstürze', 'In FiveMs Logs und im Crash-Ordner steht kein Absturz. Wenn doch mal einer passiert, steht hier, woran es lag.');
    return;
  }
  sub.textContent = items.length === 1 ? 'Der letzte Absturz aus FiveMs Logs und Crash-Ordner' : `Die letzten ${items.length} aus FiveMs Logs und Crash-Ordner`;
  const p = d.pattern;
  body.innerHTML = (p ? `<div class="crs-pat">${ico('crs-repeat')}<p><b>${p.count} von ${p.of} Abstürzen</b> der letzten zwei Wochen: ${esc(p.title)}. Dort lohnt es sich zuerst anzusetzen.</p></div>` : '')
    + `<div class="crs-list">${items.map(crsRow).join('')}</div>`;
}

function crsRow(it) {
  const open = CRS.open.has(it.id), c = it.cause || {};
  const meta = [crsWhen(it.time), it.build, it.code || it.module, it.server].filter(Boolean).map(esc).join('<i>·</i>');
  return `<div class="crs-row${open ? ' open' : ''}" data-crsid="${esc(it.id)}">
    <button class="crs-rh" type="button" data-crsrow="${esc(it.id)}" aria-expanded="${open}">
      <span class="crs-ic">${ico(c.icon || 'alert')}</span>
      <span class="crs-rt"><b>${esc(c.title || 'Absturz')}</b><span>${meta}</span></span>
      <span class="crs-sure ${esc(c.sure || '')}">${esc(CRS_SURE[c.sure] || '')}</span>
      ${ico('chev', 'i crs-chev')}
    </button>
    ${open ? crsDetail(it) : ''}
  </div>`;
}

function crsDetail(it) {
  const c = it.cause || {};
  const said = [];
  if (it.signature) said.push(`<div class="crs-f"><span>Signatur</span><code class="crs-sig">${esc(it.signature)}</code></div>`);
  if (it.text) said.push(`<div class="crs-f"><span>Fehler</span><p>${esc(it.text)}</p></div>`);
  if (!it.signature && !it.text) said.push(`<div class="crs-f"><span>Gemeldet</span><p class="crs-none">Im Log steht nichts zu diesem Absturz, nur der Dump ist da.</p></div>`);
  const small = [['Build', it.build || 'unbekannt'], ['Server', it.server], ['Crash-Hash', it.legacyHash], ['Report-ID', it.reportId]].filter(x => x[1]);
  const stack = (it.stack || []).length ? `<details class="crs-stk"><summary>${ico('chev')}Stacktrace, ${it.stack.length} ${it.stack.length === 1 ? 'Zeile' : 'Zeilen'}</summary><code>${it.stack.map(esc).join('\n')}</code></details>` : '';
  const fixes = (it.fixes || []).map(f => {
    const key = it.id + '|' + f.id, done = CRS.done.has(key), busy = CRS.busy.has(key);
    return `<button class="btn sm crs-fix${done ? ' crs-done' : ''}" type="button" data-crsfix="${esc(f.id)}" data-crsfor="${esc(it.id)}"${done || busy ? ' disabled' : ''}>${ico(done ? 'check' : crsFixIcon(f))}${esc(done ? crsDoneLabel(f) : f.label)}</button>`;
  }).join('');
  const copied = CRS.copied === it.id;
  const file = it.dump ? ['dump', 'Dump zeigen'] : it.log ? ['log', 'Log zeigen'] : null;
  return `<div class="crs-det">
    <div class="crs-cols">
      <div class="crs-ex">
        <p class="crs-why">${esc(c.why || '')}</p>
        ${(c.steps || []).length ? `<ul class="crs-steps">${c.steps.map(s => `<li>${esc(s)}</li>`).join('')}</ul>` : ''}
      </div>
      <div class="crs-said">
        <div class="crs-sh">Was FiveM gemeldet hat</div>
        ${said.join('')}
        <div class="crs-small">${small.map(([k, v]) => `<span><em>${esc(k)}</em><b>${esc(v)}</b></span>`).join('')}</div>
        ${stack}
      </div>
    </div>
    <div class="crs-acts">
      ${fixes}
      <span class="crs-gap"></span>
      ${file ? `<button class="btn sm gh" type="button" data-crsrev="${esc(it.id)}" data-crskind="${file[0]}">${ico('folder')}${file[1]}</button>` : ''}
      <button class="btn sm gh crs-copy${copied ? ' crs-done' : ''}" type="button" data-crscopy="${esc(it.id)}">${ico(copied ? 'check' : 'copy')}${copied ? 'Kopiert' : 'Für Support kopieren'}</button>
    </div>
  </div>`;
}

const crsFixIcon = f => f.kind === 'host' ? (f.arg === 'plugins' ? 'folder' : 'download') : f.kind === 'page' ? 'chev' : f.id === 'netcheck' ? 'nc-pulse' : f.id === 'pagefile' ? 'ram' : f.id === 'cache' ? 'trash' : f.id === 'hags' ? 'gpucard' : 'zap';
const crsDoneLabel = f => f.id === 'cache' ? 'Cache geleert' : f.id === 'hags' ? 'Aus nach Neustart' : 'Erledigt';
const crsItem = id => ((CRS.data && CRS.data.items) || []).find(x => x.id === id);

/** Opens a page, with a tab or a card in view ("mods#rs", "gfx#texCard"). */
function crsGo(arg) {
  const [page, part] = String(arg).split('#');
  go(page);
  if (!part) return;
  if (page === 'mods' && typeof modTab === 'function') return modTab(part);
  setTimeout(() => $('#' + part)?.scrollIntoView({ behavior: 'smooth', block: 'center' }), 120);
}

async function crsFix(id, fixId) {
  const it = crsItem(id), f = it && (it.fixes || []).find(x => x.id === fixId);
  if (!f) return;
  const key = id + '|' + f.id;
  if (f.kind === 'page') return crsGo(f.arg);
  if (f.kind === 'action' && f.arg === 'netcheck') return typeof ncOpen === 'function' ? ncOpen() : undefined;   // a dialog: nothing to mark as done
  CRS.busy.add(key); crsRender();
  let ok = false;
  try {
    if (f.kind === 'host') {
      const r = await api('crash.fix', { fix: f.arg });
      if (!r.ok) toast('Nicht geöffnet', r.error || '', false);
      else toast(f.arg === 'plugins' ? 'Plugins-Ordner geöffnet' : 'Treiberseite geöffnet', f.arg === 'plugins' ? 'Nimm die Datei aus dem Absturz heraus und starte FiveM neu' : 'Dort findest du den aktuellen Treiber für deine Grafikkarte');
    } else if (f.kind === 'health') {
      const r = await api('health.fix', { fix: f.arg });
      ok = !!r.ok;
      toast(ok ? 'Auslagerungsdatei abgesichert' : 'Nicht geändert', ok ? 'Wirkt nach einem Neustart' : 'Windows hat die Änderung nicht übernommen', ok);
      if (ok && typeof loadTweaks === 'function') loadTweaks();
    } else if (f.kind === 'tweakoff') {
      const r = await api('tweak.set', { id: f.arg, on: false });
      ok = !r.error;   // "applied" can stay true until the restart when Windows' own default takes over again
      toast(ok ? 'GPU-Planung ausgeschaltet' : 'Nicht ausgeschaltet', ok ? 'Wirkt nach einem Neustart' : (r.error || ''), ok);
      if (typeof loadTweaks === 'function') loadTweaks();
    } else if (f.kind === 'action' && f.arg === 'fmcache') {
      const r = await api('fivem.cache');
      ok = !r.error;
      if (ok && typeof addFreed === 'function') addFreed(r.freed || 0);
      toast(ok ? 'FiveM-Cache geleert' : 'Cache nicht geleert', ok ? 'Der nächste Start lädt die Server-Daten neu' : r.error, ok);
    }
  } catch (e) { toast('Nicht ausgeführt', (e && e.message) || '', false); }
  CRS.busy.delete(key);
  if (ok) CRS.done.add(key);
  crsRender();
}

async function crsCopy(id) {
  const it = crsItem(id); if (!it) return;
  let ok = false;
  try { ok = !!(await api('clip', { text: it.support })).ok; } catch { }
  // the browser preview has no host clipboard
  if (!ok && !HOST) try { await navigator.clipboard.writeText(it.support); ok = true; } catch { }
  if (!ok) return toast('Nicht kopiert', 'Die Zwischenablage war gerade belegt – versuch es noch einmal', false);
  toast('Für Support kopiert', 'Ohne deinen Windows-Namen – einfach in Discord einfügen');
  CRS.copied = id; crsRender();
  clearTimeout(CRS.copyT);
  CRS.copyT = setTimeout(() => { if (CRS.copied === id) { CRS.copied = ''; crsRender(); } }, 2600);
}

/** Strg+K: the newest crash's text for support, read first if needed. */
async function crsCopyLast() {
  if (!CRS.data || Date.now() - CRS.at > 30000) await crsLoad();
  const it = CRS.data && CRS.data.items && CRS.data.items[0];
  if (CRS.err) return toast('Abstürze nicht gelesen', CRS.err, false);
  if (!it) return toast('Kein Absturz da', 'In FiveMs Logs und im Crash-Ordner steht keiner', false);
  return crsCopy(it.id);
}

async function crsReveal(id, kind) {
  const it = crsItem(id); if (!it) return;
  const r = await api('crash.reveal', kind === 'dump' ? { dump: it.dump } : { log: it.log }).catch(e => ({ ok: false, error: e.message }));
  if (!r.ok) toast('Nicht gezeigt', r.error || '', false);
}

document.addEventListener('click', e => {
  const t = e.target.closest('[data-crsrow],[data-crsfix],[data-crscopy],[data-crsrev],[data-crsretry],[data-crsgo],#crsReload');
  if (!t || !t.closest('#crsCard')) return;
  if (t.id === 'crsReload' || t.dataset.crsretry !== undefined) return crsLoad();
  if (t.dataset.crsgo) return go(t.dataset.crsgo);
  if (t.dataset.crsrow) { const id = t.dataset.crsrow; CRS.open.has(id) ? CRS.open.delete(id) : CRS.open.add(id); return crsRender(); }
  if (t.dataset.crsfix) return crsFix(t.dataset.crsfor, t.dataset.crsfix);
  if (t.dataset.crscopy) return crsCopy(t.dataset.crscopy);
  if (t.dataset.crsrev) return crsReveal(t.dataset.crsrev, t.dataset.crskind);
});

{ const prev = PAGE_SHOW.diag; PAGE_SHOW.diag = () => { prev?.(); crsShow(); }; }

/* ---------- browser preview: the host's exact shapes, with crashes in the formats FiveM writes ----------
   ?crash=empty | error | nofivem shows the other states; CRS_MOCK.mode does the same at run time. */
const CRS_MOCK = { mode: new URLSearchParams(location.search).get('crash') || 'list' };
function crsMockList() {
  const now = Date.now(), H = 36e5, at = (h, m = 0) => new Date(now - h * H - m * 6e4).toISOString();
  const fmt = new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  const support = (it) => ['**FiveM-Absturz** · ' + fmt.format(new Date(it.time)) + ' Uhr', 'Build: ' + (it.build || 'unbekannt'),
    it.signature ? 'Signatur: `' + it.signature + '`' : '', it.text ? 'Fehler: ' + it.text : '', !it.signature && !it.text ? 'Fehler: keine Details im Log' : '',
    it.legacyHash ? 'Crash-Hash: ' + it.legacyHash : '', it.reportId ? 'Report-ID: ' + it.reportId : '',
    'Vermutete Ursache: ' + it.cause.title + (it.cause.sure === 'unklar' ? ' (unsicher)' : '') + ' – laut OrbitTweak'].filter(Boolean).join('\n');
  const oc = 'Übertaktung (auch eine ab Werk) testweise auf Standard stellen – etwa in MSI Afterburner „Reset“.';
  const nv = { id: 'driver:nvidia', label: 'NVIDIA-Treiber laden', kind: 'host', arg: 'driver:nvidia' };
  const items = [
    { id: 'm1', time: at(2, 14), build: 'b3258', server: 'cfx.re/join/q4m8vx', title: 'nvgpucomp64.dll+2A4B10', signature: 'nvgpucomp64.dll+2A4B10', module: 'nvgpucomp64.dll', code: null, text: null, legacyHash: null,
      reportId: 'si-923407810a0e40deba6e61b554b7a499', stack: ['nvgpucomp64.dll+2A4B10', 'nvgpucomp64.dll+1F0C02', 'nvwgf2umx.dll+9F1E20', 'd3d11.dll!CContext::TID3D11DeviceContext_Draw_<1> (0x5e)', 'GTA5_b3258.exe!sub_1415D7E8C (0x1c4)'],
      dump: '0f8b3c1e-5d2a-4c6e-9d1b-1c2d3e4f5a6b.dmp', log: 'CitizenFX_log_2026-11-22T091502.log',
      cause: { key: 'gpu-driver', title: 'NVIDIA-Grafiktreiber', why: 'Der Absturz passierte in nvgpucomp64.dll, dem NVIDIA-Grafiktreiber. Das liegt oft an der Treiberversion, manchmal an Übertaktung oder Overlays, die sich in die Grafik einklinken.', steps: ['Treiber aktualisieren – kam der Fehler nach einem Update, eine Version zurück.', oc, 'PC neu starten, dann FiveM.'], sure: 'klar', icon: 'gpucard' },
      fixes: [nv] },
    { id: 'm2', time: at(26, 40), build: 'b3258', server: null, title: 'RAGE error: ERR_MEM_EMBEDDEDALLOC_ALLOC', signature: null, module: null, code: 'ERR_MEM_EMBEDDEDALLOC_ALLOC',
      text: 'RAGE error: ERR_MEM_EMBEDDEDALLOC_ALLOC – A game error (at 00000001400a2b44) caused the game to stop working. Out of memory in the game heap.', legacyHash: null, reportId: 'si-6f2424cb9eb64950aa4976c0a48f5a56', stack: [],
      dump: '5b0c7a41-8e3f-4a9b-b2d4-0c6e1f7a9d33.dmp', log: 'CitizenFX_log_2026-11-21T174120.log',
      cause: { key: 'memory', title: 'Arbeitsspeicher voll', why: 'GTA V meldet einen Speicherfehler (ERR_MEM_EMBEDDEDALLOC_ALLOC). Meist lief der Arbeitsspeicher voll – mit vielen offenen Programmen, einer zu kleinen Auslagerungsdatei oder sehr vielen Server-Inhalten.', steps: ['Deine Auslagerungsdatei ist in Ordnung.', 'Browser und andere große Programme vor dem Spielen schließen.', 'Ist dein Texturbudget erhöht, senk es testweise.'], sure: 'klar', icon: 'ram' },
      fixes: [{ id: 'tex', label: 'Texturbudget ansehen', kind: 'page', arg: 'gfx#texCard' }] },
    { id: 'm3', time: at(74, 5), build: 'b3258', server: null, title: 'rtsshooks64.dll+1F2A', signature: 'rtsshooks64.dll+1F2A', module: 'rtsshooks64.dll', code: null, text: null, legacyHash: null, reportId: null,
      stack: ['rtsshooks64.dll+1F2A', 'dxgi.dll!CDXGISwapChain::Present (0x3b)', 'GTA5_b3258.exe!sub_14002A3B4 (0x30)'], dump: '9a1e44c0-2f6b-4d8e-8c3a-5e7f9b1d2c40.dmp', log: 'CitizenFX_log_2026-11-19T081133.log',
      cause: { key: 'overlay', title: 'RivaTuner (MSI Afterburner)', why: 'Der Absturz passierte in rtsshooks64.dll, einem Teil von RivaTuner (MSI Afterburner) – nicht von FiveM. Solche Programme klinken sich ins Spiel ein und können es zum Absturz bringen.', steps: ['Das Overlay in RivaTuner für FiveM abschalten oder RivaTuner vor dem Spielen beenden.', 'Danach FiveM neu starten.'], sure: 'klar', icon: 'layers' },
      fixes: [{ id: 'autostart', label: 'Autostart öffnen', kind: 'page', arg: 'autostart' }] },
    { id: 'm4', time: at(96, 48), build: 'b3258', server: null, title: 'RAGE error: ERR_STR_FAILURE', signature: null, module: null, code: 'ERR_STR_FAILURE',
      text: 'RAGE error: ERR_STR_FAILURE – A game error (at 0000000141087d3c) caused the game to stop working.', legacyHash: null, reportId: null, stack: [],
      dump: '2d6f8a10-7c3e-4b5a-9e1d-8f0a2b4c6d7e.dmp', log: 'CitizenFX_log_2026-11-18T094410.log',
      cause: { key: 'streaming', title: 'Spiel-Daten nicht ladbar', why: 'GTA V konnte Daten nicht laden, die es beim Spielen nachlädt. Oft ist eine Datei im FiveM-Cache beschädigt, seltener ein Server-Inhalt.', steps: ['FiveM-Cache leeren – der nächste Start lädt die Server-Daten neu.', 'Passiert es nur auf einem Server, melde es dort.'], sure: 'wahrscheinlich', icon: 'download' },
      fixes: [{ id: 'cache', label: 'FiveM-Cache leeren', kind: 'action', arg: 'fmcache' }] },
    { id: 'm5', time: at(118, 30), build: 'b3258', server: null, title: 'GTA5_b3258.exe!sub_141322F74 (0x1a)', signature: 'GTA5_b3258.exe!sub_141322F74 (0x1a)', module: 'GTA5_b3258.exe', code: null, text: null,
      legacyHash: 'violet-network-nitrogen', reportId: 'si-cb413ac3233f4d3fa2e557d39d487581', stack: ['GTA5_b3258.exe!sub_141322F74 (0x1a)', 'GTA5_b3258.exe!sub_14132A0B8 (0x6d)', 'GTA5_b3258.exe!sub_1407BED00 (0x1d1)'],
      dump: null, log: 'CitizenFX_log_2026-11-17T193015.log',
      cause: { key: 'game', title: 'Im Spielcode von GTA V', why: 'Das sagt nur, wo es krachte, nicht warum. Häufig sind es Inhalte des Servers (Autos, Kleidung, Karten) oder eigene Mods, manchmal ein Fehler im Spiel selbst.', steps: ['Passiert es nur auf einem Server, melde es dort mit dem Text unten.', 'Passiert es überall, entferne testweise eigene Mods.', 'Für den Support zählt die Signatur – kopier sie mit.'], sure: 'unklar', icon: 'gamepad' },
      fixes: [{ id: 'mods', label: 'Deine Mods', kind: 'page', arg: 'mods#files' }] },
    { id: 'm6', time: at(140, 2), build: 'b3095', server: null, title: 'NVIDIA GPU drivers encountered an error', signature: null, module: null, code: null, text: 'NVIDIA GPU drivers encountered an error – FiveM crashed due to NVIDIA GPU drivers.', legacyHash: null, reportId: null, stack: [],
      dump: 'c2d4e6f8-1a3b-4c5d-8e9f-0a1b2c3d4e5f.dmp', log: 'CitizenFX_log_2026-11-16T201744.log',
      cause: { key: 'gpu-driver', title: 'NVIDIA-Grafiktreiber', why: 'FiveM selbst nennt den NVIDIA-Grafiktreiber als Auslöser. Das liegt oft an der Treiberversion, manchmal an Übertaktung oder Overlays, die sich in die Grafik einklinken.', steps: ['Treiber aktualisieren – kam der Fehler nach einem Update, eine Version zurück.', oc, 'PC neu starten, dann FiveM.'], sure: 'klar', icon: 'gpucard' },
      fixes: [nv] },
    { id: 'm7', time: at(220, 12), build: null, server: null, title: null, signature: null, module: null, code: null, text: null, legacyHash: null, reportId: null, stack: [],
      dump: 'e7f1a2b3-c4d5-4e6f-a7b8-c9d0e1f2a3b4.dmp', log: null,
      cause: { key: 'nolog', title: 'Keine Details im Log', why: 'FiveM hat einen Crash-Dump geschrieben, aber in seinen Logs steht nichts dazu. Das Log ist schon weg: FiveM löscht Logs nach sieben Tagen, die letzten zehn Dumps behält es länger. Woran es lag, lässt sich so nicht sagen.', steps: ['Tritt es wieder auf, steht FiveMs Meldung im neuen Log – OrbitTweak liest sie dann mit.', 'Für den Support: den Dump über „Dump zeigen“ finden und mitschicken.'], sure: 'unklar', icon: 'crs-unknown' },
      fixes: [] },
  ];
  items.forEach(it => { it.support = support(it); });
  return { found: true, items, pattern: { key: 'gpu-driver', title: 'NVIDIA-Grafiktreiber', count: 2, of: 7 }, logs: 14, dumps: 6 };
}
MOCK_EXT['crash.list'] = async () => {
  await sleep(450);
  const m = CRS_MOCK.mode;
  if (m === 'error') return { error: 'FiveMs Absturz-Daten ließen sich nicht lesen. Versuch es gleich noch einmal.' };
  if (m === 'nofivem') return { found: false, items: [], pattern: null, logs: 0, dumps: 0 };
  if (m === 'empty') return { found: true, items: [], pattern: null, logs: 12, dumps: 0 };
  return crsMockList();
};
MOCK_EXT['crash.fix'] = async a => /^(driver:(nvidia|amd|intel)|plugins)$/.test(a.fix || '') ? { ok: true } : { ok: false, error: 'Diese Aktion gibt es nicht.' };
MOCK_EXT['crash.reveal'] = async a => a.dump || a.log ? { ok: true } : { ok: false, error: 'Diese Datei gibt es nicht.' };
