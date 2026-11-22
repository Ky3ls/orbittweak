'use strict';
/* 2.27 – Schnellsuche: a command palette on Strg+K (and the search button in the title bar).
   It finds pages, settings tabs, tweaks, actions and favourite servers by their name or by the problem someone has
   ("ruckler", "platz", "absturz") and runs them through the same functions the pages use.
   While it is closed nothing of it exists or runs: the sheet is made on the first open, the index on every open, and
   the list, the index and the tweak observer are dropped again on close. One keydown listener stays, nothing else. */
(() => {
  const RECENT_KEY = 'orbitPalRecent';
  const P = { el: null, input: null, list: null, verb: null, count: null, open: false, items: [], flat: [], sel: 0, prev: null, obs: null, srvTried: false, hideT: 0, px: -1, py: -1 };

  /* ---------------------------------------------------------------- matching ---------------------------------------------------------------- */
  /** Lower case without accents, ß as ss, and an e after a, o or u dropped, so "ae", "oe", "ue" typed for ä, ö, ü
   *  meet the umlaut (both the text and the query go through this, so they always agree). Any other character becomes
   *  one space. st/en hold, for every character of the result, where it came from in the original text. */
  function fold(s) {
    s = String(s ?? '');
    let t = ''; const st = [], en = [];
    for (let i = 0; i < s.length; i++) {
      const lo = s[i].toLowerCase();
      const c = lo === 'ß' ? 'ss' : lo.normalize('NFD').replace(/[̀-ͯ]/g, '');
      for (const ch of c) {
        const k = /[a-z0-9]/.test(ch) ? ch : ' ', last = t[t.length - 1];
        if ((k === 'e' && last && 'aou'.includes(last)) || (k === ' ' && (!t || last === ' '))) { if (en.length) en[en.length - 1] = i + 1; continue; }
        t += k; st.push(i); en.push(i + 1);
      }
    }
    return { t, st, en };
  }
  const wordStart = (t, i) => i === 0 || t[i - 1] === ' ';
  /** How well one typed word fits one text: 1 a whole word, .8 the start of a word, .5 inside a word (only from
   *  `inner` letters on: "ram" inside "Programme" is noise, "speicher" inside "Arbeitsspeicher" is not); with `loose`
   *  also its letters in order with little in between (well below a real match). [quality, ranges] or null. */
  function hit(tok, t, loose, inner = 3) {
    let best = null;
    for (let i = t.indexOf(tok); i >= 0; i = t.indexOf(tok, i + 1)) {
      const end = i + tok.length, ws = wordStart(t, i);
      if (!ws && tok.length < inner) continue;
      const q = ws ? (end === t.length || t[end] === ' ' ? 1 : .8) + (i === 0 ? .05 : 0) : .5;
      if (!best || q > best[0]) best = [q, [[i, end]]];
      if (q >= 1) break;
    }
    if (best || !loose || tok.length < 2) return best;
    for (let s = t.indexOf(tok[0]); s >= 0; s = t.indexOf(tok[0], s + 1)) {
      const pos = [s]; let j = s;
      for (let k = 1; k < tok.length && j >= 0; k++) { j = t.indexOf(tok[k], j + 1); if (j >= 0) pos.push(j); }
      if (pos.length < tok.length) break;   // from a later start the letters cannot all be found either
      const span = pos[pos.length - 1] - s + 1;
      if (span > tok.length * 3 + 2) continue;
      const q = .42 * tok.length / span + (wordStart(t, s) ? .08 : 0);
      if (!best || q > best[0]) best = [q, pos.map(p => [p, p + 1])];
    }
    return best;
  }
  /** The best fit of one typed word in an entry: the title counts most, then its keywords, then the description.
   *  `loose` (letters in order) only runs when nothing matched for real, so "gpu" does not also bring up
   *  "Wiederherstellun(g)s(pu)nkt", while a typo like "rmfr" still finds "RAM freigeben". */
  function tokScore(tok, it, loose) {
    let best = null;
    const tryOn = (f, w, loose, title, cut = 0) => {
      const h = hit(cut ? tok.slice(0, -cut) : tok, f.t, loose && !cut, title ? 3 : 4);
      if (!h || (cut && h[0] < .8)) return;
      const s = h[0] * w * (cut ? .7 : 1);
      if (!best || s > best.s) best = { s, r: title ? h[1] : null };
    };
    tryOn(it.ft, 1, loose, true); tryOn(it.fk, .72, false, false); tryOn(it.fs, .45, false, false);
    // another ending ("abstürze", "ruckelt") still finds the word it comes from, as the start of a word
    // (never shorter than four letters: "platz" must not find "Planung" or "PlayStation")
    if (!best && tok.length >= 5) for (const cut of [1, 2]) { if (tok.length - cut < 4) continue; tryOn(it.ft, 1, false, true, cut); tryOn(it.fk, .72, false, false, cut); if (best) break; }
    return best;
  }
  /** The title with the matched letters marked (ranges are in the folded text; st/en lead back to the original). */
  function marked(it, ranges) {
    const raw = it.title;
    if (!ranges.length) return esc(raw);
    const f = it.ft, spans = ranges.map(([a, b]) => [f.st[a], f.en[b - 1]]).sort((x, y) => x[0] - y[0]);
    let out = '', at = 0;
    for (const [a, b] of spans) {
      if (b <= at) continue;
      const from = Math.max(a, at);
      out += esc(raw.slice(at, from)) + '<mark>' + esc(raw.slice(from, b)) + '</mark>'; at = b;
    }
    return out + esc(raw.slice(at));
  }

  /* ---------------------------------------------------------------- the index ---------------------------------------------------------------- */
  // [page, title, icon, line below, words people use for the problem]
  const PAGES = [
    ['home', 'Übersicht', 'home', 'Live-Status, Boost und Schnellaktionen', 'start startseite dashboard home status überblick cpu ram ping'],
    ['servers', 'Server', 'server', 'Favoriten mit Spielerzahl und Ping', 'verbinden join connect favoriten serverliste ping spieler cfx status online'],
    ['launcher', 'Launcher', 'rocket', 'FiveM mit Boost starten, Build und Pure Mode', 'starten spiel start build pure kanal beta canary release pfad verbinden'],
    ['f8', 'F8-Befehle', 'terminal', 'FiveM-Einstellungen, die in keinem Menü stehen', 'konsole befehle commands fps anzeige drawfps cl'],
    ['stats', 'Spielzeit', 'chart', 'Wie lange und wann du spielst', 'statistik stunden zeit spielzeit verlauf woche tage sitzungen'],
    ['gfx', 'Grafik', 'monitor', 'GTA-V-Presets und Fenstermodus', 'grafik farben helligkeit vibrance texture texturen schatten presets fenster randlos vollbild qualität fps settings'],
    ['display', 'Live-Farben', 'sun', 'Farbintensität, Helligkeit und Gamma im Spiel', 'farben helligkeit vibrance digital gamma kontrast sättigung nachtsicht dunkel'],
    ['xhair', 'Fadenkreuz', 'crosshair', 'Eigenes Fadenkreuz über FiveM', 'crosshair zielen overlay reticle punkt kreuz sniper'],
    ['mods', 'Mods & ReShade', 'puzzle', 'Mods, Looks und Minimaps installieren', 'mods reshade minimap karte sounds kill effekte installieren packs rpf grafikpack'],
    ['clip', 'Clipper', 'film', 'Kill-Clips automatisch aufnehmen', 'aufnahme clip clips video kill montage highlight aufnehmen replay teilen'],
    ['pad', 'Controller', 'gamepad', 'Controller-Anzeige als Overlay', 'gamepad controller xbox playstation ps4 ps5 dualshock dualsense overlay tasten'],
    ['input', 'Input-Lab', 'keyboard', 'Tastatur und Maus testen', 'tastatur maus snap tap polling rate hz reaktion wasd tester'],
    ['games', 'Minigames', 'bullseye', 'Aim-Training vor dem Spielen', 'aim training zielen reaktion aufwärmen warm up minigame'],
    ['tweaks', 'Tweaks', 'zap', 'Windows-Einstellungen fürs Spiel', 'fps leistung boost windows optimieren schalter performance'],
    ['og', 'Andere Spiele', 'gamestack', 'Tweaks für Fortnite, Counter-Strike 2 und Rainbow Six Siege', 'fortnite cs2 counter-strike cs r6 rainbow six siege spiele games reflex vsync fps limit'],
    ['cleaner', 'Cleaner', 'broom', 'Caches und Reste entfernen', 'speicher platz cache temp müll aufräumen festplatte ssd voll ram arbeitsspeicher löschen'],
    ['autostart', 'Autostart', 'power', 'Programme, die mit Windows starten', 'hochfahren start langsam programme autostart windows boot store apps teams whatsapp'],
    ['diag', 'Diagnose', 'heart', 'Ursachen für Ruckler, Abstürze und Ladeprobleme', 'absturz abstürze crash ruckler ruckeln stottern fehler logs probleme check lädt laden hängt freeze'],
    ['settings', 'Einstellungen', 'settings', 'Konto, Design, Verhalten und Updates', 'optionen einstellungen settings'],
    ['admin', 'Admin', 'shield', 'Lizenz-Keys und Mod-Freigaben', 'keys lizenz freigaben codes'],
  ];
  // [id, open, title, icon, line below, words]
  const SUBS = [
    ['st:account', () => openSet('account'), 'Konto', 'key', 'Einstellungen › Lizenz und PC', 'lizenz key pc id hwid abmelden account'],
    ['st:design', () => openSet('design'), 'Design', 'sparkle', 'Einstellungen › Farbe und Hintergrund', 'farbe theme hintergrund akzent aussehen winter anime bild'],
    ['st:general', () => openSet('general'), 'Verhalten', 'sliders', 'Einstellungen › Leistung, Boost und Discord', 'leistung boost discord status standby wiederherstellungspunkt verhalten'],
    ['st:updates', () => openSet('updates'), 'Updates', 'download', 'Einstellungen › Version und Automatik', 'version aktualisieren update neu automatisch'],
    ['st:safety', () => openSet('safety'), 'Sicherheit', 'history', 'Einstellungen › Wiederherstellen', 'wiederherstellen zurücksetzen backup systemwiederherstellung rückgängig'],
    ['bk:card', () => { openSet('safety'); setTimeout(() => $('#bkCard')?.scrollIntoView({ block: 'start', behavior: 'smooth' }), 80); }, 'Einstellungs-Backup', 'bk-archive', 'Einstellungen › Sicherheit › FiveM-Einstellungen', 'backup sicherung sicherungen tastenbelegung keybinds binds tasten grafik einstellungen zurückholen wiederherstellen fivem cfg verloren'],
    ['st:about', () => openSet('about'), 'Über OrbitTweak', 'info', 'Einstellungen › Hinweise und Credits', 'credits hinweise info lizenzen über'],
    ['cl:gal', () => { go('clip'); pageTab('clip', 'gal'); }, 'Deine Clips', 'play', 'Clipper › Ansehen, teilen, Video machen', 'videos clips ansehen teilen montage highlight galerie'],
    ['cl:kills', () => { go('clip'); pageTab('clip', 'kills'); }, 'Kill-Erkennung', 'crosshair', 'Clipper › Wann ein Clip entsteht', 'kill erkennung killfeed name tod automatisch'],
    ['cl:setup', () => { go('clip'); pageTab('clip', 'setup'); }, 'Aufnahme', 'settings', 'Clipper › Recorder, Taste und Qualität', 'aufnahme recorder taste qualität obs shadowplay nvidia game bar länge'],
    ['md:lib', () => { go('mods'); modTab('lib'); }, 'Mod-Bibliothek', 'layers', 'Mods & ReShade › Looks und Mods', 'bibliothek mods looks installieren'],
    ['md:rs', () => { go('mods'); modTab('rs'); }, 'ReShade', 'sparkle', 'Mods & ReShade › Tasten und Optionen', 'reshade shader filter looks preset schärfe'],
    ['md:mm', () => { go('mods'); modTab('mm'); }, 'Eigene Minimap', 'map', 'Mods & ReShade › Aus einem Bild', 'minimap karte map bild logo eigene'],
    ['md:files', () => { go('mods'); modTab('files'); }, 'Meine Mods', 'puzzle', 'Mods & ReShade › Installiert und Downloads', 'installiert downloads dateien entfernen meine'],
    ['sw:alarm', () => swOpen('alarm'), 'Platz-Alarm', 'sw-bell', 'Server › Bescheid, sobald ein Platz frei ist', 'voll server platz frei slot warteschlange queue alarm benachrichtigung wieder online offline'],
    ['sw:friends', () => swOpen('friends'), 'Freunde online', 'users', 'Server › Freunde in deinen Favoriten finden', 'freunde kumpel spieler online wo spielt name finden mitspieler'],
    ['sw:desk', () => swOpen('desk'), 'Server auf den Desktop', 'sw-desk', 'Server › Verknüpfung, die mit einem Klick verbindet', 'verknüpfung desktop shortcut icon schnellstart lnk direkt verbinden'],
    ['pw:perf', () => pwOpen(), 'Engpass-Analyse', 'pw-neck', 'Spielzeit › Was deine FPS begrenzt', 'engpass bottleneck limit gpu cpu grafikkarte prozessor auslastung vram grafikspeicher leistung fps begrenzt ruckler'],
    ['dg:crash', () => crsFocus(), 'Absturz-Analyse', 'alert', 'Diagnose › Letzte Abstürze mit Ursache', 'absturz abstürze abgestürzt crash gecrasht ursache warum treiber signatur dump support err'],
    ['hw:mon', () => window.hwFocus?.('mon'), 'Monitor-Check', 'hw-hz', 'Diagnose › Läuft dein Monitor mit allen Hz?', 'hz hertz 60 75 120 144 165 240 bildwiederholrate refresh rate monitor bildschirm flüssig'],
    ['hw:ram', () => window.hwFocus?.('ram'), 'RAM-Check', 'hw-chan', 'Diagnose › Dual Channel und XMP/EXPO', 'ram arbeitsspeicher riegel dual single channel kanal xmp expo docp takt mhz bios'],
  ];
  const SCENE_SUB = { winter: 'Nordlicht, Raureif, Schnee', anime: 'Abendhimmel, Kirschblüten' };
  const SUGGEST = ['ac:boost', 'ac:fivem', 'ac:health', 'ac:ram', 'pg:tweaks', 'pg:clip'];
  const GROUP = { recent: 'Zuletzt', sug: 'Vorschläge', page: 'Seiten', action: 'Aktionen', tweak: 'Tweaks', server: 'Server' };
  const PRIOR = { page: .03, action: .02, server: .01, tweak: 0 };

  function openSet(tab) { go('settings'); setTab(tab); }
  /** The design tab's own swatch takes the click, so saving and everything else it does stays in one place. */
  function setTheme(k) {
    const pick = () => document.querySelector(`#themes [data-th="${k}"], #sceneThemes [data-th="${k}"]`);
    if (!pick()) renderSettings();
    pick()?.click();
  }
  const plain = v => String(v || '').replace(/\^\d/g, '').replace(/\s+/g, ' ').trim();   // FiveM colour codes (^1…^9) out
  const srvName = s => plain(s.name || s.code) || 'Unbekannter Server';
  /** Players only when the server says how many fit (an IP server with locked info reports 0 of 0), then its line. */
  const srvSub = s => !s.online ? 'Gerade offline'
    : [s.max ? `${s.players ?? 0} von ${s.max} Spielern` : '', plain(s.desc)].filter(Boolean).join(' · ') || 'Online';
  const shortDesc = d => { const s = String(d || '').split(/ – |; |\. /)[0]; return s.length > 110 ? s.slice(0, 108).replace(/\s+\S*$/, '') + '…' : s; };
  const navShown = p => { const n = $(`.nav[data-p="${p}"]`); return !n || !n.classList.contains('hidden'); };
  /** cfx.re/join/…, IP or IP:Port, host:Port */
  const ADDR = /^(?:https?:\/\/)?(?:cfx\.re\/join\/[a-z0-9]{3,}|(?:\d{1,3}\.){3}\d{1,3}(?::\d{2,5})?|[a-z0-9-]+(?:\.[a-z0-9-]+)+:\d{2,5})\/?$/i;
  const connectItem = raw => {
    const addr = raw.replace(/^https?:\/\//i, '').replace(/\/+$/, '');   // FiveM wants "cfx.re/join/…", not a web link
    return { id: 'cn:' + addr, g: 'server', icon: 'link', title: `Mit „${addr}“ verbinden`, noRecent: true, verb: 'Verbinden',
      sub: S.fmOn ? 'FiveM läuft schon: erst beenden, dann verbinden' : 'FiveM mit deinem Boost starten und direkt verbinden', run: () => launch(addr) };
  };

  /** Everything the palette can find, built fresh on each open (pages and states may have changed). */
  function index() {
    const L = [], cur = curPage, setOn = $('#setTabs .tab.on')?.dataset.st;
    const add = o => L.push(o);
    for (const [p, title, icon, sub, kw] of PAGES) {
      if (!$('#p-' + p) || !navShown(p)) continue;
      add({ id: 'pg:' + p, g: 'page', icon, title, sub, kw, verb: 'Öffnen', here: cur === p, run: () => go(p) });
    }
    for (const [id, run, title, icon, sub, kw] of SUBS) {
      const [kind, k] = id.split(':');
      const here = kind === 'st' ? cur === 'settings' && setOn === k
        : kind === 'cl' ? cur === 'clip' && !!$(`[data-tabs="clip"] .tab.on[data-tab="${k}"]`)
        : cur === 'mods' && !!$(`#modTabs .tab.on[data-mt="${k}"]`);
      add({ id, g: 'page', icon, title, sub, kw, verb: 'Öffnen', here, run });
    }
    // actions: the same functions as the buttons on the pages
    const fm = !!S.fmOn, noFm = !!S.fivem && S.fivem.found === false;
    add({ id: 'ac:boost', g: 'action', icon: 'zap', title: 'Boost starten', sub: 'Empfohlene Tweaks, Temp-Dateien, DNS und RAM in einem Schritt', kw: 'optimieren fps leistung schneller boost alles', verb: 'Ausführen', run: () => { go('home'); return boost(); } });
    // (FiveM not found: the entry leads to the launcher, where the path is chosen, instead of a start that must fail)
    add(fm ? { id: 'ac:fivem', g: 'action', icon: 'power', title: 'FiveM beenden', sub: S.fmGame ? 'Schließt das laufende Spiel, mit Rückfrage' : 'Schließt FiveM, mit Rückfrage', kw: 'beenden schließen stoppen killen spiel hängt eingefroren', verb: 'Ausführen', run: () => killFiveM() }
      : { id: 'ac:fivem', g: 'action', icon: 'play', title: 'FiveM starten', sub: noFm ? 'FiveM nicht gefunden: Pfad im Launcher wählen' : 'Mit deinem Pre-Launch-Boost', kw: 'starten spielen launch öffnen spiel los', verb: noFm ? 'Öffnen' : 'Starten', run: () => noFm ? go('launcher') : launch('') });
    add({ id: 'ac:ram', g: 'action', icon: 'ram', title: 'RAM freigeben', sub: S.settings.purgeStandby === false ? 'Working-Sets der Programme leeren' : 'Working-Sets und Standby-Liste leeren', kw: 'arbeitsspeicher speicher voll freigeben nachladen', verb: 'Ausführen', run: () => cleanRam() });
    add({ id: 'ac:cache', g: 'action', icon: 'trash', title: 'FiveM-Cache leeren', sub: 'Hilft bei Ladeproblemen, der nächste Start lädt etwas länger', kw: 'cache leeren laden ladeprobleme hängt texturen platz', verb: 'Ausführen', run: () => clearFmCache() });
    add({ id: 'ac:health', g: 'action', icon: 'heart', title: 'Systemcheck starten', sub: 'Prüft Treiber, Energieplan, Cache und mehr', kw: 'diagnose check prüfen absturz crash ruckler fehler probleme scan', verb: 'Ausführen', run: () => { const had = !!S.health; go('diag'); if (had) return runHealth(); } });
    if (typeof ncOpen === 'function') add({ id: 'ac:netcheck', g: 'action', icon: 'nc-pulse', title: 'Netz-Check: Verbindung testen', sub: 'Zeigt in einer Minute, ob ein Lag im WLAN, beim Anbieter oder beim Server anfängt', kw: 'lag laggt lagt ping verbindung internet wlan wifi router anbieter provider paketverlust packet loss jitter netz netzwerk test messen rubberband', verb: 'Öffnen', run: () => ncOpen() });
    add({ id: 'ac:upd', g: 'action', icon: 'download', title: 'Nach Updates suchen', sub: 'Du nutzt ' + ($('#verCur')?.textContent || 'OrbitTweak'), kw: 'update aktualisieren version neu', verb: 'Ausführen', run: () => { openSet('updates'); return checkUpdate(true); } });
    add({ id: 'ac:xh', g: 'action', icon: 'crosshair', title: XH.on ? 'Fadenkreuz ausschalten' : 'Fadenkreuz einschalten', sub: 'Overlay über FiveM · Server-Regeln beachten', kw: 'crosshair zielen overlay an aus', st: XH.on ? 'An' : '', verb: XH.on ? 'Ausschalten' : 'Einschalten', run: () => xhToggle() });
    if (typeof gwSet === 'function') add({ id: 'ac:border', g: 'action', icon: 'max', title: 'Randloses Vollbild', sub: fm ? 'FiveM sofort randlos, ohne Neustart' : 'Geht nur, während FiveM läuft', kw: 'fenster fenstermodus vollbild randlos borderless alt tab', verb: 'Ausführen', run: () => gwSet('borderless') });
    add({ id: 'ac:restore', g: 'action', icon: 'shield', title: 'Wiederherstellungspunkt erstellen', sub: 'Windows-Snapshot, zu dem du jederzeit zurückkannst', kw: 'backup sicherung snapshot wiederherstellen', verb: 'Ausführen', run: () => restorePoint() });
    if (typeof bkCreate === 'function') add({ id: 'ac:bk', g: 'action', icon: 'bk-archive', title: 'FiveM-Einstellungen sichern', sub: 'Tastenbelegung, Grafik und CitizenFX.ini als Sicherung auf deinem PC', kw: 'backup sicherung sichern speichern tastenbelegung keybinds binds grafik einstellungen', verb: 'Ausführen', run: () => { openSet('safety'); return bkCreate(); } });
    add({ id: 'ac:revert', g: 'action', icon: 'undo', title: 'Alle Tweaks zurücksetzen', sub: 'Alles, was OrbitTweak geändert hat, auf den Wert davor – mit Rückfrage', kw: 'rückgängig zurück standard reset probleme', verb: 'Ausführen', run: () => revertAll() });
    add({ id: 'ac:wizard', g: 'action', icon: 'sparkle', title: 'Setup-Assistent starten', sub: 'OrbitTweak nochmal von vorne einrichten', kw: 'einrichten assistent setup erste schritte', verb: 'Ausführen', run: () => wizard(0) });
    if (typeof crsCopyLast === 'function') add({ id: 'ac:crashcopy', g: 'action', icon: 'copy', title: 'Letzten Absturz kopieren', sub: 'Kurzer Text für den Support, ohne deinen Windows-Namen', kw: 'absturz crash support discord kopieren fehler signatur', verb: 'Kopieren', run: () => crsCopyLast() });
    const th = themeKey(S.settings.accent);
    for (const [k, [n, a, b]] of Object.entries(THEMES))
      add({ id: 'th:' + k, g: 'action', sw: [a, b], title: `Design „${n}“`, sub: SCENE_SUB[k] || 'Akzentfarbe der App', kw: 'design theme farbe aussehen wechseln akzent', st: k === th ? 'Aktiv' : '', verb: 'Wählen', run: () => setTheme(k) });
    // tweaks: switched in place, the palette stays open
    for (const t of S.tweaks || []) {
      const cat = (typeof CATS !== 'undefined' && CATS[t.cat]) ? CATS[t.cat][0] : '';
      const badge = t.badge === 'NEUSTART' ? 'neustart' : t.badge === 'LIVE' ? 'live' : '';
      add({ id: 'tw:' + t.id, g: 'tweak', tw: t.id, icon: t.icon || 'zap', title: t.name, sub: t.sum || shortDesc(t.desc), kw: [t.sum, cat, 'tweak', badge].filter(Boolean).join(' '), desc: t.desc, verb: 'Umschalten', keep: true });
    }
    // favourite servers
    for (const s of S.servers || []) {
      const name = srvName(s);
      add({ id: 'sv:' + s.code, g: 'server', icon: 'server', img: s.icon || null, title: name,
        sub: srvSub(s),
        kw: ['verbinden join server', ...(s.tags || []), s.locale, s.addr, s.code].filter(Boolean).join(' '), srv: s, verb: 'Verbinden',
        run: () => s.online ? joinServer(s.code) : toast('Server gerade offline', name, false) });
    }
    // a tweak's description often names FPS, crashes or stutter only to say it does not help (or to warn): those words
    // do not find it, its short line and keywords still do
    const descOf = it => it.g === 'tweak' ? (it.desc || '').replace(/\bFPS\b|abstürz\w*|ruck(e)?l\w*/gi, ' ') : (it.desc || '');
    for (const it of L) { it.ft = fold(it.title); it.fk = fold(it.kw); it.fs = fold((it.sub || '') + ' ' + descOf(it)); }
    return L;
  }

  /* ---------------------------------------------------------------- recent ---------------------------------------------------------------- */
  function recent() {
    try { const a = JSON.parse(localStorage.getItem(RECENT_KEY) || '[]'); return Array.isArray(a) ? a.filter(x => typeof x === 'string').slice(0, 8) : []; } catch { return []; }
  }
  function remember(id) {
    try { localStorage.setItem(RECENT_KEY, JSON.stringify([id, ...recent().filter(x => x !== id)].slice(0, 8))); } catch { }
  }

  /* ---------------------------------------------------------------- drawing ---------------------------------------------------------------- */
  function tileHtml(it) {
    if (it.sw) return `<span class="pal-ic pal-sw" style="--sa:${it.sw[0]};--sb:${it.sw[1]}"><i></i></span>`;
    if (it.img) return `<span class="pal-ic pal-img" style="background-image:url('${esc(String(it.img).replace(/["'()\\\s]/g, c => '%' + c.charCodeAt(0).toString(16).padStart(2, '0')))}')"></span>`;
    return `<span class="pal-ic">${ico(it.icon)}</span>`;
  }
  function rightHtml(it) {
    let r = '';
    if (it.g === 'tweak') {
      const t = (S.tweaks || []).find(x => x.id === it.tw);
      if (t) {
        if (t.badge === 'NEUSTART') r += '<span class="eff w">Neustart</span>';
        else if (t.badge === 'LIVE') r += '<span class="eff">Live</span>';
        r += `<span class="tg pal-tg ${t.applied ? 'on' : ''}" data-id="${esc(t.id)}" role="switch" aria-checked="${!!t.applied}" aria-label="${esc(t.name)}"></span>`;
      }
    } else if (it.srv) {
      const s = it.srv;
      r += s.online ? (s.ping != null ? `<span class="pal-st"><i class="pal-dot ${s.ping < 50 ? 'g' : s.ping < 100 ? 'w' : 'r'}"></i>${s.ping} ms</span>` : '') : '<span class="pal-st">Offline</span>';
    } else if (it.here) r += '<span class="pal-st">Geöffnet</span>';
    else if (it.st) r += `<span class="pal-st on">${esc(it.st)}</span>`;
    return r + '<kbd class="pal-ent">↵</kbd>';
  }
  function rowHtml(it, i, title) {
    const t = it.g === 'tweak' ? (S.tweaks || []).find(x => x.id === it.tw) : null;
    const dis = (t && t.usable === false && !t.applied) || (it.srv && !it.srv.online);
    const sub = t && t.usable === false && !t.applied ? (t.hint || 'Auf diesem PC nicht möglich') : it.sub;
    return `<div class="pal-row${dis ? ' dis' : ''}" data-pi="${i}" id="pal-o${i}" role="option" aria-selected="false"${it.tw ? ` data-tw="${esc(it.tw)}"` : ''}>${tileHtml(it)}`
      + `<span class="pal-tx"><b>${title}</b>${sub ? `<small>${esc(sub)}</small>` : ''}</span><span class="pal-r">${rightHtml(it)}</span></div>`;
  }
  /** Search and draw. Keeps the chosen entry chosen when the list only refreshes (servers arriving, for example). */
  function render(keep) {
    const keepId = keep ? P.flat[P.sel]?.id : null;
    const toks = fold(P.input.value).t.split(' ').filter(Boolean);
    let groups = [];
    if (!toks.length) {
      const byId = new Map(P.items.map(it => [it.id, it])), rec = recent().map(id => byId.get(id)).filter(Boolean).slice(0, 5);
      const sug = SUGGEST.map(id => byId.get(id)).filter(it => it && !rec.includes(it)).slice(0, 5);
      if (rec.length) groups.push(['recent', rec.map(it => ({ it, r: [] }))]);
      if (sug.length) groups.push(['sug', sug.map(it => ({ it, r: [] }))]);
    } else {
      const rec = recent(), whole = toks.join(' ');
      const pass = loose => {
        const by = {};
        for (const it of P.items) {
          let s = 0, r = [], ok = true;
          for (const tok of toks) { const h = tokScore(tok, it, loose); if (!h) { ok = false; break; } s += h.s; if (h.r) r = r.concat(h.r); }
          if (!ok) continue;
          const ri = rec.indexOf(it.id);
          // (equal scores keep the order of the index: pages as in the side bar, designs as in Settings)
          s = s / toks.length + PRIOR[it.g] + (ri >= 0 ? .1 * (1 - ri / 8) : 0) + (it.ft.t.trim() === whole ? .1 : 0);
          (by[it.g] ||= []).push({ it, r, s });
        }
        return by;
      };
      let by = pass(false);
      if (!Object.keys(by).length) by = pass(true);
      // an address typed in: connect to it straight away, like "Direkt verbinden" on the servers page
      const addr = P.input.value.trim();
      if (ADDR.test(addr)) (by.server ||= []).unshift({ it: connectItem(addr), r: [], s: 9 });
      groups = Object.entries(by).map(([g, l]) => [g, l.sort((a, b) => b.s - a.s).slice(0, 8)]).sort((a, b) => b[1][0].s - a[1][0].s);
      let left = 30;
      groups = groups.map(([g, l]) => { const c = l.slice(0, Math.max(0, left)); left -= c.length; return [g, c]; }).filter(([, l]) => l.length);
    }
    P.flat = []; let html = '';
    for (const [g, l] of groups) {
      html += `<div class="pal-g" role="presentation">${GROUP[g]}</div>`;
      for (const { it, r } of l) { html += rowHtml(it, P.flat.length, marked(it, r)); P.flat.push(it); }
    }
    if (!P.flat.length) {
      const q = P.input.value.trim();
      html = `<div class="pal-empty">${ico('search')}<b>${q ? `Nichts gefunden für „${esc(q)}“` : 'Noch nichts geladen'}</b><span>${q ? 'Versuch ein anderes Wort, zum Beispiel „ruckler“, „platz“ oder „fps“.' : 'Gleich noch einmal versuchen.'}</span></div>`;
    }
    P.list.innerHTML = html;
    const n = P.flat.length;
    P.count.textContent = toks.length ? (n === 1 ? '1 Treffer' : n + ' Treffer') : '';
    const ki = keepId ? P.flat.findIndex(it => it.id === keepId) : -1;
    select(ki >= 0 ? ki : 0, true);
    if (ki < 0) P.list.scrollTop = 0;
  }
  function select(i, scroll) {
    const n = P.flat.length;
    P.sel = n ? Math.max(0, Math.min(i, n - 1)) : 0;
    P.list.querySelector('.pal-row.sel')?.classList.remove('sel');
    P.list.querySelector('.pal-row[aria-selected="true"]')?.setAttribute('aria-selected', 'false');
    const row = P.list.querySelector(`[data-pi="${P.sel}"]`);
    if (row) { row.classList.add('sel'); row.setAttribute('aria-selected', 'true'); P.input.setAttribute('aria-activedescendant', row.id); }
    else P.input.removeAttribute('aria-activedescendant');
    P.verb.textContent = n ? P.flat[P.sel].verb : 'Öffnen';
    if (row && scroll) {
      // the first entry of a group brings its label along; only the list scrolls, never the page behind it
      const lab = row.previousElementSibling?.classList.contains('pal-g') ? row.previousElementSibling : null;
      const L = P.list, top = P.sel === 0 ? 0 : (lab || row).offsetTop - 6, bot = row.offsetTop + row.offsetHeight + 6;
      if (top < L.scrollTop) L.scrollTop = top;
      else if (bot > L.scrollTop + L.clientHeight) L.scrollTop = bot - L.clientHeight;
    }
  }
  function move(d, wrap = true) {
    const n = P.flat.length; if (!n) return;
    let i = P.sel + d;
    i = wrap ? (i + n) % n : Math.max(0, Math.min(i, n - 1));
    select(i, true);
  }
  /** After a tweak was switched (the Tweaks page draws its list again), the switches here follow the real state. */
  function syncTweaks() {
    if (!P.open) return;
    for (const row of P.list.querySelectorAll('.pal-row[data-tw]')) {
      const t = (S.tweaks || []).find(x => x.id === row.dataset.tw), tg = row.querySelector('.tg');
      if (!t || !tg) continue;
      tg.classList.toggle('on', !!t.applied); tg.classList.remove('busy'); tg.setAttribute('aria-checked', String(!!t.applied));
      row.classList.toggle('dis', t.usable === false && !t.applied);
    }
  }

  /* ---------------------------------------------------------------- run ---------------------------------------------------------------- */
  function run(i) {
    const it = P.flat[i]; if (!it) return;
    if (!it.noRecent) remember(it.id);
    if (it.keep) {   // a tweak: through the Tweaks page's own switch handler (usable, toast with "Neustart" and all)
      const tg = P.list.querySelector(`[data-pi="${i}"] .tg[data-id]`);
      if (tg && !tg.classList.contains('busy')) tg.click();   // (still being switched: a second Enter waits for it)
      return;
    }
    close(false);
    try {
      const r = it.run();
      if (r && typeof r.catch === 'function') r.catch(e => toast('Das hat nicht geklappt', e && e.message || '', false));
    } catch (e) { toast('Das hat nicht geklappt', e && e.message || '', false); }
  }

  /* ---------------------------------------------------------------- open / close ---------------------------------------------------------------- */
  const shown = sel => !!document.querySelector(sel);
  /** Not over the key screen, the splash or an open dialog. */
  function blocked() {
    const sp = $('#splash');
    return shown('#lock.show') || shown('#modal.show') || (!!sp && !sp.classList.contains('out') && sp.style.display !== 'none');
  }
  function build() {
    const el = document.createElement('div');
    el.className = 'pal'; el.id = 'pal'; el.hidden = true;
    el.innerHTML = `<div class="pal-dim"></div>
      <div class="pal-sheet" role="dialog" aria-modal="true" aria-label="Schnellsuche">
        <label class="pal-q">${ico('search')}<input id="palQ" type="text" placeholder="Seite, Tweak, Aktion oder Problem suchen" autocomplete="off" spellcheck="false" role="combobox" aria-expanded="true" aria-controls="palList" aria-autocomplete="list"><kbd>Esc</kbd></label>
        <div class="pal-list" id="palList" role="listbox" aria-label="Ergebnisse"></div>
        <div class="pal-foot"><span><kbd>↑</kbd><kbd>↓</kbd>Auswählen</span><span><kbd>↵</kbd><em id="palVerb">Öffnen</em></span><span><kbd>Esc</kbd>Schließen</span><i></i><span class="pal-n" id="palN"></span></div>
      </div>`;
    document.body.appendChild(el);
    P.el = el; P.input = el.querySelector('#palQ'); P.list = el.querySelector('#palList'); P.verb = el.querySelector('#palVerb'); P.count = el.querySelector('#palN');
    el.querySelector('.pal-dim').addEventListener('click', () => close(true));
    P.input.addEventListener('input', () => render(false));
    // the input keeps the focus: a click on the list does not take it away
    el.querySelector('.pal-sheet').addEventListener('mousedown', e => { if (e.target !== P.input) e.preventDefault(); });
    P.list.addEventListener('pointermove', e => {
      if (e.clientX === P.px && e.clientY === P.py) return;   // the list scrolled under a resting pointer
      P.px = e.clientX; P.py = e.clientY;
      const r = e.target.closest('[data-pi]'); if (r && +r.dataset.pi !== P.sel) select(+r.dataset.pi, false);
    });
    P.list.addEventListener('click', e => {
      const r = e.target.closest('[data-pi]'); if (!r) return;
      const i = +r.dataset.pi;
      if (e.target.closest('.tg[data-id]')) { select(i, false); remember(P.flat[i].id); return; }   // the switch itself: the Tweaks handler takes this click
      run(i);
    });
  }
  function open() {
    if (P.open || blocked()) return;
    if (!P.el) build();
    clearTimeout(P.hideT);
    P.prev = document.activeElement;
    P.items = index();
    P.input.value = '';
    P.el.hidden = false; P.el.classList.remove('pal-off'); P.el.classList.add('pal-on');
    P.open = true;
    render(false);
    P.input.focus({ preventScroll: true });
    P.obs = new MutationObserver(syncTweaks);
    for (const s of ['#tgrid', '#inputTweaks']) { const n = $(s); if (n) P.obs.observe(n, { childList: true }); }
    // favourites not loaded yet: once per session, the same way the servers page does it
    if (!(S.servers || []).length && !P.srvTried && typeof loadServers === 'function') {
      P.srvTried = true;
      Promise.resolve(loadServers()).catch(() => { }).then(() => { if (P.open && (S.servers || []).length) { P.items = index(); render(true); } });
    }
  }
  function close(restore) {
    if (!P.open) return;
    P.open = false;
    P.obs?.disconnect(); P.obs = null;
    P.el.classList.remove('pal-on'); P.el.classList.add('pal-off');
    const prev = P.prev; P.prev = null;
    if (restore && prev && prev.isConnected && prev !== document.body && typeof prev.focus === 'function') prev.focus({ preventScroll: true });
    else if (document.activeElement === P.input) P.input.blur();
    clearTimeout(P.hideT);
    P.hideT = setTimeout(() => {
      if (P.open) return;
      P.el.hidden = true; P.el.classList.remove('pal-off');
      P.list.innerHTML = ''; P.items = []; P.flat = [];
    }, 170);
  }

  /* ---------------------------------------------------------------- keys ---------------------------------------------------------------- */
  // capture on the window: runs before every other key handler (the Input-Lab tester, the key screen), so nothing
  // swallows Strg+K and the keys the palette uses never reach the page below
  addEventListener('keydown', e => {
    const k = e.key || '';
    if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && (k.toLowerCase() === 'k' || (e.code === 'KeyK' && !/^[a-z]$/i.test(k)))) {
      e.preventDefault(); e.stopPropagation();
      if (P.open) close(true); else open();
      return;
    }
    if (!P.open) return;
    if (blocked()) { close(false); return; }   // a dialog or the key screen came up meanwhile: it gets the keys
    if (e.isComposing) return;
    let used = true;
    if (k === 'Escape') close(true);
    else if (k === 'ArrowDown' || (k === 'Tab' && !e.shiftKey)) move(1);
    else if (k === 'ArrowUp' || (k === 'Tab' && e.shiftKey)) move(-1);
    else if (k === 'PageDown') move(5, false);
    else if (k === 'PageUp') move(-5, false);
    else if (k === 'Enter') { if (!e.repeat) run(P.sel); }   // a held Enter must not switch a tweak back and forth
    else {
      used = false;
      if (document.activeElement !== P.input && k.length === 1 && !e.ctrlKey && !e.metaKey) P.input.focus({ preventScroll: true });
    }
    if (used) { e.preventDefault(); e.stopPropagation(); }
  }, true);

  window.palOpen = open;
  window.palClose = () => close(true);
})();
