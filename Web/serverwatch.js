'use strict';
/* 2.31 – Server page: Platz-Alarm (a full or offline favourite reports a free slot), Freunde online (friends in the
   favourites' public player lists) and Server auf den Desktop (a shortcut that connects in one click).
   Host: MainWindow.ServerWatch.cs, Core/ServerWatch.cs, Core/ServerShortcut.cs, Core/Notify.cs, Core/ServerWatchLogic.cs.
   The favourites list itself stays app.js's: this file only adds to its rows after each render. */

const SW = {
  watches: [], names: [], notify: false, partial: false, max: 3, maxFriends: 50,
  fr: null, frBusy: false, frErr: '', more: false, loaded: false,
  busy: new Set(),   // requests on their way (watch codes, 'd' + code for shortcuts, 'f' for friend edits)
};
const SW_CFX = /^cfx:[a-z0-9]{4,8}$/;
const swPlain = s => String(s ?? '').replace(/\^\d/g, '').replace(/~[a-zA-Z]~/g, '').replace(/\s+/g, ' ').trim();
const swFull = s => s.max > 0 && s.players >= s.max;
const swActive = w => w && (w.state === 'watching' || w.state === 'retry');
const swJoinAddr = code => 'cfx.re/join/' + String(code).slice(4);

/** "14 s", "1:20 min", "52 min", "1:52 h" */
function swDur(ms, long) {
  const s = Math.max(0, Math.round(ms / 1000));
  if (s < 60) return s + ' s';
  const m = Math.floor(s / 60);
  if (m < 60) return long ? m + ' min' : `${m}:${String(s % 60).padStart(2, '0')} min`;
  return `${Math.floor(m / 60)}:${String(m % 60).padStart(2, '0')} h`;
}
function swAgo(t) {
  const s = Math.max(0, Math.round((Date.now() - t) / 1000));
  return s < 10 ? 'gerade eben' : s < 60 ? `vor ${s} s` : s < 3600 ? `vor ${Math.floor(s / 60)} min` : `vor ${Math.floor(s / 3600)} h`;
}

/* ======================= state ======================= */
async function swLoad() {
  try {
    const r = await api('sw.state');
    SW.watches = r.watches || []; SW.max = r.max || 3; SW.maxFriends = r.maxFriends || 50;
    SW.names = r.friends?.names || []; SW.notify = !!r.friends?.notify; SW.partial = !!r.friends?.partial;
    SW.loaded = true;
  } catch { }
  swRender();
}
function swRender() { swRenderAlarm(); swDecorate(); swRenderFriends(); }

/* ======================= favourites rows ======================= */
// app.js draws the rows; after every draw the alarm and shortcut buttons are added to the cfx rows
{ const draw = renderServers; renderServers = function () { const r = draw.apply(this, arguments); swDecorate(); return r; }; }

function swDecorate() {
  const list = $('#srvList'); if (!list) return;
  const rows = [...list.children].filter(r => r.classList.contains('srv') && !r.classList.contains('skel'));
  rows.forEach((row, i) => {
    const s = S.servers[i]; if (!s) return;
    const cfx = SW_CFX.test(s.code || '');
    row.querySelectorAll('.sw-pa,.sw-st').forEach(e => e.remove());
    // shortcut button next to "remove", both shown on hover
    let tail = row.querySelector('.sw-tail');
    const rm = row.querySelector(':scope > .rm');
    if (!tail && rm) { tail = document.createElement('div'); tail.className = 'sw-tail'; rm.replaceWith(tail); tail.append(rm); }
    tail?.querySelector('.sw-desk')?.remove();
    if (cfx && tail) tail.insertAdjacentHTML('afterbegin', `<button class="ib sw-desk" type="button" data-swdesk="${esc(s.code)}" ${SW.busy.has('d' + s.code) ? 'disabled' : ''} title="Auf den Desktop: eine Verknüpfung, die FiveM mit deinem Boost startet und sofort verbindet. Windows fragt beim Klick wie beim normalen Start von OrbitTweak nach Adminrechten.">${ico('sw-desk')}</button>`);
    if (!cfx) return;
    // Platz-Alarm: in the line under the name, for full and offline servers (and while one runs)
    const ds = row.querySelector('.ds'); if (!ds) return;
    const w = SW.watches.find(x => x.code === s.code), on = swActive(w), busy = SW.busy.has(s.code);
    const fired = w && w.state === 'fired';
    let html = fired ? `<span class="chip g sw-st">${w.reason === 'online' ? 'Wieder online' : 'Platz frei'}</span>` : '';
    // (after a fired alarm a new one can start right here when the list still shows the server full or offline)
    if (on || (s.online && swFull(s)) || !s.online) {
      if (s.online && swFull(s) && !on && !fired) html = '<span class="chip w sw-st">Voll</span>';
      const tip = on ? 'Platz-Alarm läuft. Klicken beendet ihn.' : s.online ? 'Platz-Alarm: Bescheid geben, sobald ein Platz frei ist' : 'Platz-Alarm: Bescheid geben, sobald der Server wieder online ist';
      html += `<button class="sw-pa${on ? ' sw-on' : ''}" type="button" data-swbell="${esc(s.code)}" ${busy ? 'disabled' : ''} title="${tip}">${ico('sw-bell')}${on ? 'Alarm an' : 'Platz-Alarm'}</button>`;
    }
    if (!html) return;
    const after = ds.querySelector(':scope > .chip.r');   // after "Offline"
    if (after) after.insertAdjacentHTML('afterend', html); else ds.insertAdjacentHTML('afterbegin', html);
  });
}

/* ======================= Platz-Alarm ======================= */
async function swWatch(code) {
  const s = S.servers.find(x => x.code === code); if (!s || SW.busy.has(code)) return;
  SW.busy.add(code); swDecorate();
  try {
    const r = await api('sw.watch', { code, online: !!s.online, players: s.players || 0, max: s.max || 0, name: swPlain(s.name), auto: false });
    if (r.watches) SW.watches = r.watches;
    if (r.ok) toast('Platz-Alarm an', s.online ? 'Du bekommst Bescheid, sobald ein Platz frei ist.' : 'Du bekommst Bescheid, sobald der Server wieder online ist.');
    else toast(r.free ? 'Gerade ist Platz' : 'Kein Platz-Alarm', r.error || '', !!r.free);
  } catch (e) { toast('Kein Platz-Alarm', e.message, false); }
  finally { SW.busy.delete(code); swRender(); }
}
async function swUnwatch(code) {
  try { const r = await api('sw.unwatch', { code }); if (r.watches) SW.watches = r.watches; } catch (e) { toast('Nicht beendet', e.message, false); }
  swRender();
}
async function swAuto(code, on) {
  const w = SW.watches.find(x => x.code === code); if (w) w.auto = on; swRenderAlarm();
  try { const r = await api('sw.auto', { code, on }); if (r.watches) SW.watches = r.watches; } catch (e) { toast('Nicht geändert', e.message, false); }
  swRenderAlarm();
}

function swLine(w) {
  const now = Date.now(), nums = w.max ? `${w.players} von ${w.max}` : '';
  if (w.state === 'watching') {
    const head = w.kind === 'slot' ? `<span class="sw-k">Voll${w.max ? ` · ${w.players}/${w.max}` : ''}</span>` : '<span class="sw-k">Offline</span>';
    return `${head} · prüft in ${swDur(w.next - now)} · noch ${swDur(w.ends - now, true)}`;
  }
  if (w.state === 'retry') return `<span class="sw-bad">${esc(w.error || 'Cfx.re antwortet nicht')}</span> · neuer Versuch in ${swDur(w.next - now)}`;
  if (w.state === 'fired') return `<span class="sw-good">${w.reason === 'online' ? 'Wieder online' : 'Platz frei'}</span>${nums ? ' · ' + nums : ''} · ${swAgo(w.done)}${w.launched ? ' · FiveM gestartet' : ''}`;
  return esc(({ timeout: 'Nach 2 Stunden beendet', fivem: 'Beendet: FiveM wurde gestartet', game: 'Beendet: du bist im Spiel' })[w.reason] || w.error || 'Beendet');
}
function swWatchRow(w) {
  const on = swActive(w), fired = w.state === 'fired';
  const cls = fired ? 'sw-fired' : w.state === 'ended' ? 'sw-ended' : w.state === 'retry' ? 'sw-retry' : '';
  return `<div class="sw-w ${cls}">
    <div class="sw-wi">${ico(fired ? 'check' : 'sw-bell')}</div>
    <div class="sw-wx"><b>${esc(w.name)}</b><span data-swline="${esc(w.code)}"${on ? ' title="Schaut etwa alle 20 Sekunden bei Cfx.re nach, nach Fehlern seltener"' : ''}>${swLine(w)}</span></div>
    ${on ? `<div class="sw-auto" title="Startet FiveM mit deinem Boost und verbindet, sobald der Platz frei ist. Nur wenn FiveM nicht läuft, ein laufendes FiveM wird nie angefasst."><span>Automatisch verbinden</span><div class="tg sm${w.auto ? ' on' : ''}" role="switch" tabindex="0" aria-checked="${!!w.auto}" aria-label="Automatisch verbinden" data-swauto="${esc(w.code)}"></div></div>` : ''}
    ${fired && !w.launched ? `<button class="btn sm sw-go" type="button" data-swjoin="${esc(w.code)}">${ico('play')}Verbinden</button>` : ''}
    <button class="ib sw-x" type="button" data-swend="${esc(w.code)}" title="${on ? 'Platz-Alarm beenden' : 'Aus der Liste nehmen'}" aria-label="${on ? 'Platz-Alarm beenden' : 'Aus der Liste nehmen'}">${ico('x')}</button>
  </div>`;
}
function swRenderAlarm() {
  const el = $('#swAlarm'); if (!el) return;
  const ws = SW.watches;
  el.classList.toggle('hidden', !ws.length);
  if (!ws.length) { el.innerHTML = ''; return; }
  const act = ws.filter(swActive).length;
  el.innerHTML = `
    <div class="sw-ah"><h2 class="sw-t">Platz-Alarm</h2><span class="sw-cnt">${act ? `${act} von ${SW.max} aktiv` : 'Keiner aktiv'}</span></div>
    <p class="sw-sub">Läuft, solange OrbitTweak offen ist, und wird nicht gespeichert. Endet nach 2 Stunden oder wenn FiveM startet.</p>
    <div class="sw-wl">${ws.map(swWatchRow).join('')}</div>
    <p class="sw-note">${ico('info')}<span>Ein freier Platz kann nach Sekunden wieder weg sein. Server mit eigener Warteschlange zählen anders: Die Liste zeigt dort oft „voll“, obwohl du über die Warteschlange reinkommst.</span></p>`;
}
// the countdowns: once a second, only text, only while the page is shown and something runs
setInterval(() => {
  if (curPage !== 'servers' || document.hidden || !SW.watches.length) return;
  for (const w of SW.watches) { const el = document.querySelector(`[data-swline="${CSS.escape(w.code)}"]`); if (el) el.innerHTML = swLine(w); }
}, 1000);

/* ======================= Freunde online ======================= */
async function swFriends(force) {
  if (!SW.names.length || SW.frBusy) return;
  if (!force && SW.fr && SW.fr.next && Date.now() < SW.fr.next) return;   // the host never searches more often either
  SW.frBusy = true; swRenderFriends();
  try { SW.fr = await api('sw.friends'); SW.frErr = ''; }
  catch (e) { SW.frErr = e.message || 'Fehler'; }
  finally { SW.frBusy = false; swRenderFriends(); }
}
// only while the Server page is shown (the host looks at most every 60 s, ten servers per round)
setInterval(() => { if (curPage === 'servers' && !document.hidden) swFriends(false); }, 10000);

function swCoverage(fr) {
  const c = { names: 0, anon: 0, hidden: 0, empty: 0, offline: 0, error: 0, wait: 0 };
  for (const s of fr.servers || []) c[s.listing] = (c[s.listing] || 0) + 1;
  const looked = c.names + c.anon + c.hidden + c.empty, parts = [];
  if (looked) parts.push(looked === 1 ? '1 Server durchsucht' : `${looked} Server durchsucht`);
  if (c.anon + c.hidden) parts.push(`${c.anon + c.hidden} ${c.anon + c.hidden === 1 ? 'zeigt' : 'zeigen'} keine Namen`);
  if (c.offline) parts.push(`${c.offline} offline`);
  if (c.error) parts.push(`${c.error} nicht erreichbar`);
  if (c.wait) parts.push(`${c.wait} in der nächsten Runde`);
  if (fr.ip) parts.push(fr.ip === 1 ? 'IP-Server nicht durchsuchbar' : `${fr.ip} IP-Server nicht durchsuchbar`);
  const named = (fr.servers || []).filter(s => s.listing === 'names').map(s => swPlain(s.name));
  const blind = (fr.servers || []).filter(s => s.listing === 'anon' || s.listing === 'hidden').map(s => swPlain(s.name));
  const why = c.anon + c.hidden ? `<details class="sw-why"${!c.names ? ' open' : ''}><summary>Warum keine Namen?</summary><p>Cfx.re zeigt bei den meisten Servern statt echter Namen nur „Player“ oder „Anon1“, manche Server verstecken ihre Liste ganz (Stand November 2026: bei 17 von 18 geprüften Servern). Dort kann OrbitTweak niemanden finden, auch wenn deine Freunde gerade spielen.</p>${blind.length ? `<p><b>Ohne Namen:</b> ${esc(blind.join(', '))}</p>` : ''}${named.length ? `<p><b>Mit Namen:</b> ${esc(named.join(', '))}</p>` : ''}</details>` : '';
  return `<p class="sw-cov">${esc(parts.join(' · '))}</p>${why}`;
}
function swFriendRow(f) {
  const alias = swPlain(f.player).toLowerCase() !== swPlain(f.friend).toLowerCase() ? ` <small>als ${esc(f.player)}</small>` : '';
  const ping = f.ping == null ? '' : `<span class="sw-ping ${f.ping < 50 ? 'g' : f.ping < 100 ? 'w' : 'r'}" title="Dein Ping zu diesem Server">${f.ping} ms</span> · `;
  return `<div class="sw-f">
    <div class="sw-fa">${esc((swPlain(f.friend)[0] || '?').toUpperCase())}</div>
    <div class="sw-fx"><b>${esc(swPlain(f.friend))}${alias}</b><span title="${esc(swPlain(f.server))}">${ping}${esc(swPlain(f.server))}</span></div>
    <button class="btn sm sw-go" type="button" data-swjoinf="${esc(f.code)}" title="FiveM mit deinem Boost starten und verbinden">Beitreten</button>
  </div>`;
}
function swRenderFriends() {
  const el = $('#swFriends'); if (!el) return;
  const fr = SW.fr, n = SW.names.length;
  const cool = fr && fr.next && Date.now() < fr.next;
  let body;
  if (!n) body = '<p class="sw-fp">Trag die FiveM-Namen deiner Freunde ein. OrbitTweak sucht sie in den öffentlichen Spielerlisten deiner Favoriten.</p>';
  else if (fr && !fr.total) body = `<p class="sw-fp">Füg zuerst Server mit Join-Code (cfx.re/join/…) zu den Favoriten hinzu: Nur die haben eine öffentliche Spielerliste.${fr.ip ? ' IP-Server lassen sich nicht durchsuchen.' : ''}</p>`;
  else if (!fr && SW.frErr) body = `<p class="sw-ferr">${ico('alert')}<span>${esc(SW.frErr)}</span></p>`;
  else if (!fr || (!fr.at && (SW.frBusy || fr.busy))) body = `<div class="sw-fl"><div class="sw-f sw-skel"><div class="sw-fa"></div><div class="sw-fx"><i></i><i></i></div></div></div><p class="sw-cov">Suche in deinen Favoriten…</p>`;
  else {
    const looked = (fr.servers || []).some(s => ['names', 'anon', 'hidden', 'empty'].includes(s.listing));
    const list = fr.found.length ? `<div class="sw-fl">${fr.found.map(swFriendRow).join('')}</div>`
      : looked ? `<p class="sw-fp sw-none">Gerade ist keiner deiner Freunde auf deinen Favoriten zu sehen.</p>` : '';
    const err = fr.error ? `<p class="sw-ferr">${ico('alert')}<span>${esc(fr.error)} OrbitTweak versucht es später noch einmal.</span></p>` : '';
    body = list + err + swCoverage(fr);
  }
  const shown = SW.more ? SW.names : SW.names.slice(0, 10);
  const names = `<div class="sw-names">
      <div class="sw-nh"><span>Deine Freunde</span>${n ? `<small>${n} von ${SW.maxFriends}</small>` : ''}</div>
      ${n ? `<div class="sw-chips">${shown.map(x => `<span class="sw-chip"><span class="sw-cn">${esc(x)}</span><button type="button" data-swrm="${esc(x)}" title="${esc(x)} entfernen" aria-label="${esc(x)} entfernen">${ico('x')}</button></span>`).join('')}${n > 10 ? `<button type="button" class="sw-more" data-swmore>${SW.more ? 'Weniger' : `+${n - 10} weitere`}</button>` : ''}</div>` : ''}
      <div class="sw-add"><input class="in" id="swName" maxlength="40" autocomplete="off" spellcheck="false" placeholder="FiveM-Name hinzufügen" aria-label="FiveM-Name hinzufügen"${n >= SW.maxFriends ? ' disabled' : ''}><button class="ib" type="button" data-swadd title="Hinzufügen" aria-label="Hinzufügen"${n >= SW.maxFriends ? ' disabled' : ''}>${ico('plus')}</button></div>
    </div>`;
  const opts = n ? `<div class="sw-opts">
      <div class="sw-opt"><div><b>Auch Teilnamen finden</b><p>„Max“ findet dann auch „MaxPower“. Aus: nur genau der Name (Groß- und Kleinschreibung egal).</p></div><div class="tg sm${SW.partial ? ' on' : ''}" role="switch" tabindex="0" aria-checked="${SW.partial}" aria-label="Auch Teilnamen finden" data-swopt="partial"></div></div>
      <div class="sw-opt"><div><b>Melden, wenn ein Freund online kommt</b><p>Schaut alle 3 Minuten nach, solange OrbitTweak läuft und du nicht spielst.</p></div><div class="tg sm${SW.notify ? ' on' : ''}" role="switch" tabindex="0" aria-checked="${SW.notify}" aria-label="Melden, wenn ein Freund online kommt" data-swopt="notify"></div></div>
    </div>` : '';
  const had = document.activeElement?.id === 'swName', val = $('#swName')?.value || '';
  const looked = !!fr && (fr.servers || []).some(s => ['names', 'anon', 'hidden', 'empty'].includes(s.listing));
  el.innerHTML = `<div class="sw-fh"><h2 class="sw-t">Freunde online</h2>${n && looked ? `<span class="sw-cnt" title="Gerade gefunden">${fr.found.length}</span>` : ''}<span class="sw-sp"></span>${n ? `<button class="ib sw-fre" type="button" data-swfre ${cool || SW.frBusy ? 'disabled' : ''} title="${cool ? 'Neu suchen geht einmal pro Minute' : 'Neu suchen'}" aria-label="Neu suchen">${ico('refresh')}</button>` : ''}</div>${body}${names}${opts}`;
  if (val) $('#swName').value = val;
  if (had) $('#swName').focus();
}
async function swAddFriend() {
  const inp = $('#swName'); const v = (inp?.value || '').trim(); if (!v || SW.busy.has('f')) return;
  SW.busy.add('f');
  try {
    const r = await api('sw.friendAdd', { name: v });
    if (r.names) SW.names = r.names;
    if (!r.ok) { toast('Nicht hinzugefügt', r.error || '', false); return; }
    if ($('#swName')) $('#swName').value = '';
    swRenderFriends(); $('#swName')?.focus();
    swFriends(true);   // the host checks the new name against the lists it already has (no extra request within a minute)
  } catch (e) { toast('Nicht hinzugefügt', e.message, false); }
  finally { SW.busy.delete('f'); swRenderFriends(); }
}
async function swRemoveFriend(name) {
  try { const r = await api('sw.friendRemove', { name }); if (r.names) SW.names = r.names; } catch (e) { toast('Nicht entfernt', e.message, false); }
  if (SW.fr) SW.fr.found = SW.fr.found.filter(f => swPlain(f.friend).toLowerCase() !== swPlain(name).toLowerCase());
  swRenderFriends();
}
async function swOpt(key, on) {
  SW[key] = on; swRenderFriends();
  try { const r = await api('sw.friendOpts', { [key]: on }); SW.notify = !!r.notify; SW.partial = !!r.partial; } catch (e) { SW[key] = !on; toast('Nicht geändert', e.message, false); }
  swRenderFriends();
  if (key === 'partial') swFriends(true);
}

/* ======================= Server auf den Desktop ======================= */
async function swDesk(code) {
  const s = S.servers.find(x => x.code === code); if (SW.busy.has('d' + code)) return;
  SW.busy.add('d' + code); swDecorate();
  try {
    const r = await api('sw.shortcut', { code, name: swPlain(s?.name) });
    if (r.ok) toast(r.replaced ? 'Verknüpfung erneuert' : 'Verknüpfung auf dem Desktop', `${r.file} · ein Doppelklick startet FiveM mit Boost und verbindet${r.serverIcon ? '' : ' (mit dem OrbitTweak-Symbol)'}`);
    else toast('Keine Verknüpfung', r.error || '', false);
  } catch (e) { toast('Keine Verknüpfung', e.message, false); }
  finally { SW.busy.delete('d' + code); swDecorate(); }
}

/* ======================= clicks ======================= */
document.addEventListener('click', e => {
  const t = e.target.closest('[data-swbell],[data-swdesk],[data-swend],[data-swjoin],[data-swjoinf],[data-swauto],[data-swadd],[data-swrm],[data-swopt],[data-swfre],[data-swmore]');
  if (!t || t.disabled) return;
  const d = t.dataset;
  if (d.swbell) { const w = SW.watches.find(x => x.code === d.swbell); swActive(w) ? swUnwatch(d.swbell) : swWatch(d.swbell); }
  else if (d.swdesk) swDesk(d.swdesk);
  else if (d.swend) swUnwatch(d.swend);
  else if (d.swjoin) launch(swJoinAddr(d.swjoin));
  else if (d.swjoinf) launch(swJoinAddr(d.swjoinf));
  else if (d.swauto) swAuto(d.swauto, !t.classList.contains('on'));
  else if ('swadd' in d) swAddFriend();
  else if (d.swrm) swRemoveFriend(d.swrm);
  else if (d.swopt) swOpt(d.swopt, !t.classList.contains('on'));
  else if ('swfre' in d) swFriends(true);
  else if ('swmore' in d) { SW.more = !SW.more; swRenderFriends(); }
});
document.addEventListener('keydown', e => {
  if (e.key === 'Enter' && e.target.id === 'swName') { e.preventDefault(); swAddFriend(); return; }
  if ((e.key === ' ' || e.key === 'Enter') && e.target.matches?.('[data-swauto],[data-swopt]')) { e.preventDefault(); e.target.click(); }
});

/* ======================= host events, page hook, palette ======================= */
EVENT_EXT.swWatch = d => { SW.watches = d.watches || []; if (curPage === 'servers') { swRenderAlarm(); swDecorate(); } };
EVENT_EXT.swFire = d => {
  const name = swPlain(d.name), nums = d.max ? `${d.players} von ${d.max}` : '';
  toast(d.kind === 'online' ? 'Server wieder online' : 'Platz frei', [name, nums, d.auto ? 'FiveM startet' : ''].filter(Boolean).join(' · '));
};
EVENT_EXT.swConnect = d => {
  if (d.ok) { addFreed(d.freed); toast('FiveM wird gestartet', `Verbindet mit ${swPlain(d.name)}${d.summary ? ' · ' + d.summary : ''}`); }
  else toast('Nicht verbunden', d.error || '', false);
};
EVENT_EXT.swFriend = d => {
  toast(`${swPlain(d.friend)} ist online`, 'auf ' + swPlain(d.server));
  if (curPage === 'servers') api('sw.friends').then(r => { SW.fr = r; swRenderFriends(); }).catch(() => { });
};
{ const before = PAGE_SHOW.servers; PAGE_SHOW.servers = () => { before?.(); swLoad().then(() => swFriends(false)); }; }

/** Strg+K: open the page at the right card. */
function swOpen(what) {
  go('servers');
  setTimeout(() => {
    const el = what === 'friends' ? $('#swFriends') : what === 'alarm' && SW.watches.length ? $('#swAlarm') : $('#srvList');
    el?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    if (what === 'friends' && !SW.names.length) $('#swName')?.focus();
    if (what === 'desk' || (what === 'alarm' && !SW.watches.length)) {
      const l = $('#srvList'); if (!l) return;
      l.classList.remove('sw-hint', 'sw-hint-desk'); void l.offsetWidth;
      l.classList.add(what === 'desk' ? 'sw-hint-desk' : 'sw-hint');
      clearTimeout(l._swh); l._swh = setTimeout(() => l.classList.remove('sw-hint', 'sw-hint-desk'), 2600);
      if (what === 'alarm' && !$('#srvList .sw-pa')) toast('Platz-Alarm', 'Geht bei vollen oder offline Favoriten mit Join-Code: dort steht er unter dem Namen.');
      if (what === 'desk' && !$('#srvList .sw-desk')) toast('Server auf den Desktop', 'Geht bei Favoriten mit Join-Code (cfx.re/join/…).');
    }
  }, 120);
}

/* ======================= browser preview (mock backend) =======================
   ?sw=empty   no friends, no alarm      ?sw=live   two alarms running, friends found
   ?sw=error   Cfx.re fails (429/timeout)  ?sw=done  alarms fired and ended    ?sw=anon  every list without names */
const SWM = (() => {
  const mode = new URLSearchParams(location.search).get('sw') || '';
  const now = Date.now();
  const W = (code, name, kind, state, extra = {}) => ({ code, name, kind, state, reason: null, error: null, players: kind === 'slot' ? 256 : 0, max: kind === 'slot' ? 256 : 0, online: kind === 'slot', auto: false, started: now - 14 * 60e3, ends: now - 14 * 60e3 + 2 * 3600e3, last: now - 6e3, next: now + 14e3, done: 0, launched: false, ...extra });
  const M = {
    mode,
    servers: [
      { code: 'cfx:k4mx2p', connect: 'cfx.re/join/k4mx2p', addr: 'cfx.re/join/k4mx2p', name: '^8Nova^7City ^1| ^7Roleplay ^3[GER]', desc: 'Die lebendigste Stadt Deutschlands', players: 256, max: 256, online: true, ping: 22, locale: 'DE', tags: ['roleplay', 'economy'], icon: null },
      { code: 'cfx:r2b8qa', connect: 'cfx.re/join/r2b8qa', addr: 'cfx.re/join/r2b8qa', name: '^4Blue^7Line ^5PvP ^7Arena', desc: 'Gangwar · FFA · Ranked', players: 64, max: 128, online: true, ping: 31, locale: 'DE', tags: ['pvp'], icon: null },
      { code: 'cfx:aa7c3d', connect: 'cfx.re/join/aa7c3d', addr: 'cfx.re/join/aa7c3d', name: 'cfx.re/join/aa7c3d', desc: 'Server offline / nicht gefunden', players: 0, max: 0, online: false, ping: null, locale: '', tags: [], icon: null },
      { code: 'ip:185.12.40.7:30120', connect: '185.12.40.7:30120', addr: '185.12.40.7:30120', name: 'Kumpel-Server', desc: 'Privat', players: 3, max: 32, online: true, ping: 18, locale: 'DE', tags: [], icon: null },
    ],
    names: mode === 'empty' ? [] : ['Shadow', 'Lena', 'Kev', 'mia_rp', 'Tom'],
    notify: mode === 'live' || mode === 'done', partial: false,
    watches: mode === 'live' ? [W('cfx:k4mx2p', 'NovaCity | Roleplay [GER]', 'slot', 'watching', { auto: false }), W('cfx:aa7c3d', 'GreenValley RP', 'online', 'watching', { auto: true, next: now + 7e3, started: now - 97 * 60e3, ends: now + 23 * 60e3 })]
      : mode === 'error' ? [W('cfx:k4mx2p', 'NovaCity | Roleplay [GER]', 'slot', 'retry', { error: 'Cfx.re bremst gerade (zu viele Anfragen)', next: now + 128e3 })]
      : mode === 'done' ? [W('cfx:k4mx2p', 'NovaCity | Roleplay [GER]', 'slot', 'fired', { reason: 'free', players: 254, done: now - 12e3, next: 0 }), W('cfx:aa7c3d', 'GreenValley RP', 'online', 'ended', { reason: 'timeout', done: now - 30 * 60e3, next: 0 })]
      : [],
  };
  return M;
})();
MOCK_EXT['servers.list'] = async () => SWM.servers.map(s => ({ ...s }));
MOCK_EXT['sw.state'] = async () => ({ watches: SWM.watches, friends: { names: [...SWM.names], notify: SWM.notify, partial: SWM.partial }, max: 3, maxFriends: 50 });
MOCK_EXT['sw.watch'] = async a => {
  const s = SWM.servers.find(x => x.code === a.code), code = a.code;
  if (!SW_CFX.test(code || '')) return { ok: false, error: 'Platz-Alarm gibt es nur für Server mit Join-Code (cfx.re/join/…).', watches: SWM.watches };
  if (SWM.watches.some(w => w.code === code && swActive(w))) return { ok: true, watches: SWM.watches };
  if (SWM.watches.filter(swActive).length >= 3) return { ok: false, error: 'Es laufen schon 3 Platz-Alarme. Beende erst einen.', watches: SWM.watches };
  if (s && s.online && !swFull(s)) return { ok: false, free: true, error: `Gerade ist ein Platz frei (${s.players} von ${s.max}). Verbinde dich einfach.`, watches: SWM.watches };
  const now = Date.now(), slot = !!(s && s.online);
  SWM.watches = SWM.watches.filter(w => w.code !== code).concat({ code, name: swPlain(s?.name) || code, kind: slot ? 'slot' : 'online', state: 'watching', reason: null, error: null, players: s?.players || 0, max: s?.max || 0, online: slot, auto: !!a.auto, started: now, ends: now + 2 * 3600e3, last: now, next: now + 20e3, done: 0, launched: false });
  return { ok: true, watches: SWM.watches };
};
MOCK_EXT['sw.unwatch'] = async a => { const had = SWM.watches.some(w => w.code === a.code); SWM.watches = SWM.watches.filter(w => w.code !== a.code); return { ok: had, watches: SWM.watches }; };
MOCK_EXT['sw.auto'] = async a => { const w = SWM.watches.find(x => x.code === a.code); if (w) w.auto = !!a.on; return { ok: !!w, watches: SWM.watches }; };
MOCK_EXT['sw.friends'] = async () => {
  const now = Date.now(), m = SWM.mode;
  if (m === 'error') return { at: now - 4e3, next: now + 56e3, busy: false, error: 'Cfx.re ist gerade nicht erreichbar.', total: 3, ip: 1, friends: SWM.names.length, found: [], servers: SWM.servers.filter(s => SW_CFX.test(s.code)).map(s => ({ code: s.code, name: s.code.slice(4), listing: 'error', players: 0, max: 0, at: now - 4e3, error: 'Cfx.re antwortet nicht (Zeitüberschreitung)' })) };
  const listing = { 'cfx:k4mx2p': 'anon', 'cfx:r2b8qa': m === 'anon' ? 'hidden' : 'names', 'cfx:aa7c3d': 'offline' };
  const servers = SWM.servers.filter(s => SW_CFX.test(s.code)).map(s => ({ code: s.code, name: swPlain(s.name), listing: listing[s.code], players: s.players, max: s.max, at: now - 4e3, error: null }));
  // BlueLine shows real names (the same rules as ServerWatchLogic.Find: exact, or with partial names on from 3 characters;
  // "Lena" only finds "Lena_RP" with partial names on)
  const players = ['Shadow', 'Lena_RP', 'Mia_RP', 'xXSniperXx', 'Player'];
  const found = m === 'anon' ? [] : SWM.names.map(friend => {
    const k = friend.toLowerCase(), real = players.filter(p => p !== 'Player');
    const player = real.find(p => p.toLowerCase() === k) || (SWM.partial && k.length >= 3 ? real.find(p => p.toLowerCase().includes(k)) : null);
    return player ? { friend, player, code: 'cfx:r2b8qa', server: 'BlueLine PvP Arena', ping: 31, players: 64, max: 128 } : null;
  }).filter(Boolean);
  return { at: now - 4e3, next: now + 56e3, busy: false, error: null, total: 3, ip: 1, friends: SWM.names.length, found, servers };
};
MOCK_EXT['sw.friendAdd'] = async a => {
  const v = swPlain(a.name);
  if (!v) return { ok: false, error: 'Gib einen Namen ein.', names: SWM.names };
  if (v.length > 40) return { ok: false, error: 'Der Name ist zu lang (höchstens 40 Zeichen).', names: SWM.names };
  if (SWM.names.some(n => n.toLowerCase() === v.toLowerCase())) return { ok: false, error: 'Der Name steht schon in deiner Liste.', names: SWM.names };
  if (SWM.names.length >= 50) return { ok: false, error: 'Höchstens 50 Namen.', names: SWM.names };
  SWM.names.push(v); return { ok: true, names: [...SWM.names] };
};
MOCK_EXT['sw.friendRemove'] = async a => { const k = swPlain(a.name).toLowerCase(), n = SWM.names.length; SWM.names = SWM.names.filter(x => x.toLowerCase() !== k); return { ok: SWM.names.length < n, names: [...SWM.names] }; };
MOCK_EXT['sw.friendOpts'] = async a => { if (typeof a.notify === 'boolean') SWM.notify = a.notify; if (typeof a.partial === 'boolean') SWM.partial = a.partial; return { ok: true, notify: SWM.notify, partial: SWM.partial }; };
MOCK_EXT['sw.shortcut'] = async a => {
  await sleep(400);
  if (SWM.mode === 'error') return { ok: false, error: 'Den Desktop-Ordner gibt es nicht.' };
  const s = SWM.servers.find(x => x.code === a.code), file = (swPlain(s?.name).replace(/[<>:"/\\|?*]/g, ' ').replace(/\s+/g, ' ').trim() || 'FiveM-Server') + '.lnk';
  return { ok: true, path: 'C:\\Users\\Max\\Desktop\\' + file, file, serverIcon: a.code !== 'cfx:aa7c3d', replaced: false };
};
