'use strict';
/* 2.31 – Monitor-Check und RAM-Check: the dialogs behind their rows in the Diagnose list. The rows themselves come with
   "health" (Core/HwCheck.cs, rules in Core/HwCheckLogic.cs) and are drawn by renderHealth like every other row; their
   buttons land here through HEALTH_FIX.hw.
     hw:hz:<display>      „Beheben“: that one display to its highest refresh rate, then 15 seconds to keep it (the
                          host puts the old rate back by itself, the way Windows' own display settings do)
     hw:hzback:<display>  „Auf 60 Hz zurück“: the rate before OrbitTweak's switch
     hw:ram:chan|xmp|info „Anleitung“ / „Ansehen“: the modules Windows reports and what to do, in the BIOS or with a
                          screwdriver. OrbitTweak never switches XMP itself.
   Nothing changes without a click. */
(() => {
  const HW = { dev: null, name: '', from: 0, to: 0, until: 0, total: 0, tick: 0, wait: 0 };
  const FIVEM = 'FiveM läuft gerade. Ein Wechsel der Bildwiederholrate wirft das Spiel aus dem Vollbild oder lässt das Bild kurz schwarz werden: schließ FiveM zuerst.';
  const CONN = { hdmi: 'HDMI', displayport: 'DisplayPort', dvi: 'DVI', vga: 'VGA' };

  const open = () => $('#modal').classList.contains('show') && !!$('#mbox .hw-m');
  /** A dialog view; focus goes to its button `focus` (keyboard users start in the dialog, not on the page behind it). */
  const view = (html, cls = '', focus = '#hwOk') => {
    modal(`<div class="hw-m ${cls}">${html}</div>`);
    requestAnimationFrame(() => { if (open()) $(focus)?.focus({ preventScroll: true }); });
  };
  const head = (icon, kicker, title, tone = '') =>
    `<div class="hw-hd"><span class="hw-ic ${tone}">${ico(icon)}</span><div><div class="kicker">${esc(kicker)}</div><h1>${esc(title)}</h1></div></div>`;
  function stop() { clearInterval(HW.tick); clearTimeout(HW.wait); HW.tick = HW.wait = 0; }
  function close() { stop(); HW.dev = null; closeModal(); }

  /* ------------------------------------------------------------ monitor ------------------------------------------------------------ */
  function fail(title, text) {
    stop();
    view(`${head('alert', 'Monitor-Check', title, 'hw-bad')}<p>${esc(text)}</p>
      <div class="act"><button class="btn p lg" id="hwOk" data-esc>Schließen</button></div>`);
    $('#hwOk').onclick = close;
  }

  async function monitor(dev, btn) {
    btn.disabled = true;
    const r = await api('hw.displays').catch(e => ({ error: e.message }));
    btn.disabled = false;
    if (r.error) return fail('Nicht umgestellt', r.error);
    const d = (r.displays || []).find(x => x.device === dev);
    if (!d) return fail('Monitor nicht gefunden', 'Diesen Monitor findet Windows gerade nicht. Starte die Prüfung neu.');
    // the countdown of this display still runs (its window was replaced meanwhile): back to it, not a second switch
    if (d.pending && HW.dev === d.device && HW.until > Date.now()) return live();
    if (d.pending) return fail('Wartet noch auf „Behalten“', 'Dieser Monitor wurde gerade umgestellt und stellt sich in ein paar Sekunden von selbst zurück. Danach kannst du es noch einmal versuchen.');
    if (r.fivem) return fail('Erst FiveM schließen', FIVEM);
    if (d.best <= d.hz) return fail('Nichts umzustellen', `Der Monitor läuft schon mit ${d.hz} Hz, mehr bietet Windows hier nicht an.`);
    const secs = r.revertIn || 15, conn = CONN[d.conn];
    view(`${head('hw-hz', 'Monitor-Check', `Auf ${d.best} Hz umstellen?`)}
      <p>${esc(d.name)} läuft mit ${d.hz} Hz. Windows bietet bei ${d.w} × ${d.h}${conn ? ` über ${conn}` : ''} bis ${d.best} Hz an.</p>
      <div class="hw-rate"><div><b>${d.hz}</b><span>Hz jetzt</span></div>${ico('chev', 'i hw-to')}<div class="hw-on"><b>${d.best}</b><span>Hz danach</span></div></div>
      <ul class="hw-pts">
        <li>Das Bild wird kurz schwarz. Passt danach alles, bestätigst du. Sonst stellt OrbitTweak nach ${secs} Sekunden von selbst zurück.</li>
        <li>Der Monitor zeigt dann bis zu ${d.best} Bilder pro Sekunde. Flüssiger wird es, soweit FiveM auch so viele FPS schafft.</li>
        ${r.laptop ? '<li>Am Akku braucht ein Bildschirm mit mehr Hz etwas mehr Strom.</li>' : ''}
        <li>Nur dieser Monitor ändert sich. Auflösung und alles andere bleiben, wie sie sind.</li>
      </ul>
      <div class="act"><button class="btn lg" id="hwNo" data-esc>Abbrechen</button><button class="btn p lg" id="hwGo">${ico('hw-hz')}Auf ${d.best} Hz stellen</button></div>`, '', '#hwGo');
    $('#hwNo').onclick = close;
    $('#hwGo').onclick = () => switchHz(d);
  }

  async function switchHz(d) {
    const go = $('#hwGo'); go.disabled = true; $('#hwNo').disabled = true;
    go.innerHTML = `${ico('hw-hz')}Wird umgestellt…`;   // (not lastChild: the click ripple is a child of the button too)
    const r = await api('hw.hz.set', { device: d.device }).catch(e => ({ ok: false, error: e.message }));
    if (!r.ok) return fail(/FiveM/.test(r.error || '') ? 'Erst FiveM schließen' : 'Nicht umgestellt', r.error || 'Windows hat den Wechsel abgelehnt.');
    // the host's countdown started before its answer got here: the page ends half a second early, never late
    const total = (r.revertIn || 15) * 1000 - 500;
    Object.assign(HW, { dev: d.device, name: d.name, from: r.from, to: r.to, until: Date.now() + total, total });
    live();
  }

  /** The new rate is on screen: keep it, or go back; the bar runs out with the host's own countdown. Escape and a
   *  blind Enter go back (the picture may be black), like Windows' own „Änderungen beibehalten?“. */
  function live() {
    const secs = Math.max(0, Math.ceil((HW.until - Date.now()) / 1000));
    view(`${head('hw-hz', 'Monitor-Check', 'Passt das Bild?')}
      <p>Dein Monitor läuft jetzt mit ${HW.to} Hz. Ohne Bestätigung stellt OrbitTweak in <b class="hw-left" id="hwLeft">${secs}</b> <span id="hwUnit">${secs === 1 ? 'Sekunde' : 'Sekunden'}</span> auf ${HW.from} Hz zurück.</p>
      <div class="hw-bar"><i id="hwBar"></i></div>
      <div class="act"><button class="btn lg" id="hwBack" data-esc>${ico('undo')}Zurück auf ${HW.from} Hz</button><button class="btn p lg" id="hwKeep">${ico('check')}Behalten</button></div>`, 'hw-live', '#hwBack');
    // one transition over the time left (transform only), from the share still left (a resumed countdown is not full)
    const bar = $('#hwBar');
    bar.style.transform = `scaleX(${Math.min(1, Math.max(0, HW.until - Date.now()) / (HW.total || 1))})`;
    requestAnimationFrame(() => requestAnimationFrame(() => { bar.style.transitionDuration = Math.max(0, HW.until - Date.now()) + 'ms'; bar.style.transform = ''; bar.classList.add('hw-run'); }));
    stop();
    HW.tick = setInterval(() => {
      const left = Math.max(0, Math.ceil((HW.until - Date.now()) / 1000)), el = $('#hwLeft');
      if (el) { el.textContent = left; $('#hwUnit').textContent = left === 1 ? 'Sekunde' : 'Sekunden'; }
      if (left > 0) return;
      clearInterval(HW.tick); HW.tick = 0;
      // the host puts the old rate back now and says so (event "hwhz"); if that word gets lost, the page still moves on
      HW.wait = setTimeout(() => reverted({ ok: true, to: HW.from }), 6000);
      $$('#mbox .hw-m button').forEach(b => b.disabled = true);
    }, 250);
    $('#hwKeep').onclick = keep;
    $('#hwBack').onclick = back;
  }

  async function keep() {
    $$('#mbox .hw-m button').forEach(b => b.disabled = true);
    const r = await api('hw.hz.keep', { device: HW.dev }).catch(e => ({ ok: false, error: e.message }));
    if (!r.ok) return reverted({ ok: true, to: HW.from }, r.error);
    const name = HW.name, to = HW.to; close();
    toast(`Monitor läuft mit ${to} Hz`, name);
    runHealth();
  }

  async function back() {
    $$('#mbox .hw-m button').forEach(b => b.disabled = true);
    const r = await api('hw.hz.back', { device: HW.dev }).catch(e => ({ ok: false, error: e.message }));
    const name = HW.name; close();
    toast(r.ok ? `Zurück auf ${r.to} Hz` : 'Nicht zurückgestellt', r.ok ? name : r.error || '', r.ok);
    runHealth();
  }

  /** The countdown ran out: the host has put the old rate back. */
  function reverted(d, why) {
    stop();
    if (!open() || !HW.dev) { HW.dev = null; if (curPage === 'diag') runHealth(); return; }
    const ok = d.ok !== false;
    view(`${head('undo', 'Monitor-Check', ok ? `Wieder auf ${d.to || HW.from} Hz` : 'Nicht zurückgestellt', ok ? '' : 'hw-bad')}
      <p>${!ok ? esc(d.error || '') : why ? esc(why)
        : `Du hast nicht bestätigt, deshalb läuft der Monitor wieder wie vorher. War das Bild mit ${HW.to} Hz schwarz oder hat geflackert, liegt es meist an Kabel oder Anschluss.`}</p>
      <div class="act"><button class="btn p lg" id="hwOk" data-esc>Schließen</button></div>`);
    HW.dev = null;
    $('#hwOk').onclick = close;
    runHealth();
  }
  EVENT_EXT.hwhz = d => { if (HW.dev && d.device === HW.dev) reverted(d); else if (curPage === 'diag') runHealth(); };

  async function hzBack(item, btn) {
    btn.disabled = true;
    const dev = item.fix.split(':').slice(2).join(':');
    const r = await api('hw.hz.back', { device: dev }).catch(e => ({ ok: false, error: e.message }));
    btn.disabled = false;
    toast(r.ok ? `Zurück auf ${r.to} Hz` : 'Nicht zurückgestellt', r.ok ? item.name : r.error || '', r.ok);
    if (r.ok || /anders eingestellt/.test(r.error || '')) runHealth();
  }

  /* ------------------------------------------------------------ RAM ------------------------------------------------------------ */
  /** "A", "B" as the board prints them; with two memory controllers (Intel DDR5) the channels are counted instead. */
  function chanNames(mods) {
    const keys = [...new Set(mods.map(m => m.chan).filter(Boolean))].sort();
    return k => !k ? '' : /^[A-H0-9]$/.test(k) ? `Kanal ${k}` : `Kanal ${keys.indexOf(k) + 1}`;
  }
  function modList(r) {
    const cn = chanNames(r.modules);
    return `<div class="hw-mods">${r.modules.map((m, n) => {
      const slow = m.rated && m.rated > m.speed + 50;
      const where = [m.slot || m.bank || `Riegel ${n + 1}`, cn(m.chan)].filter(Boolean).join(' · ');
      return `<div class="hw-mod"><span class="hw-mi">${ico('ram')}</span>
        <div class="hw-mt"><b>${esc(where)}</b><span>${esc([m.maker, m.part].filter(Boolean).join(' ') || 'Ohne Teilenummer')}</span></div>
        <div class="hw-ms"><b>${m.gb} GB${m.gen ? ` · DDR${m.gen}` : ''}</b><span class="${slow ? 'hw-slow' : ''}">${m.speed ? `${m.speed} MT/s` : 'Takt unbekannt'}${slow ? ` von ${m.rated}` : ''}</span></div></div>`;
    }).join('')}</div>`;
  }
  const steps = list => `<ol class="hw-steps">${list.map(s => `<li>${s}</li>`).join('')}</ol>`;
  const sect = (title, html) => `<div class="hw-sec"><h2>${esc(title)}</h2>${html}</div>`;
  const COST = 'FiveM ist meist CPU-lastig: viele Spieler, Fahrzeuge und Skripte auf einmal. Dort zählt, wie schnell die CPU an ihre Daten kommt. '
    + 'Der Unterschied ist echt und kann groß sein, vor allem bei den niedrigsten FPS, also bei Rucklern in vollen Szenen. '
    + 'Wie viel es bei dir ausmacht, hängt von CPU, Server und Szene ab. OrbitTweak kann das nicht messen.';

  function ramBody(kind, r) {
    const parts = [...new Set(r.modules.map(m => m.part).filter(Boolean))];
    if (kind === 'chan' && r.channel === 'single') {
      const one = r.modules.length === 1;
      return {
        title: 'Nur ein Speicherkanal belegt',
        intro: one ? 'In deinem PC steckt ein einzelner Riegel. Damit arbeitet nur einer von zwei Speicherkanälen, und die CPU bekommt die halbe Speicherbandbreite.'
          : 'Deine Riegel stecken alle im selben Kanal. Damit arbeitet nur einer von zwei Speicherkanälen, und die CPU bekommt die halbe Speicherbandbreite.',
        html: sect('Was das in FiveM kostet', `<p>${COST}</p>`)
          + sect(one ? 'So kommst du auf zwei Kanäle' : 'So steckst du um', steps(one ? [
            `Besorg einen zweiten Riegel, am besten genau den gleichen: gleiche Größe und gleicher Takt${parts[0] ? `, am sichersten dieselbe Teilenummer (${esc(parts[0])})` : ''}.`,
            r.laptop ? 'Im Laptop geht das nur, wenn ein zweiter Steckplatz frei ist. Das steht im Handbuch oder auf der Herstellerseite zu deinem Modell.'
              : 'PC herunterfahren, Netzstecker ziehen und den Riegel in den Slot stecken, den das Handbuch deines Mainboards für zwei Riegel nennt. Meist sind das A2 und B2.',
            'Starte danach den Check hier noch einmal.',
          ] : [
            'PC herunterfahren und den Netzstecker ziehen.',
            'Steck die Riegel in die Slots, die das Handbuch deines Mainboards für zwei Riegel nennt. Meist sind das A2 und B2, also der zweite und vierte Slot von der CPU aus gezählt.',
            'Starte danach den Check hier noch einmal.',
          ])),
      };
    }
    if (r.xmp === 'off' && !r.laptop) return {
      title: r.gen === 5 ? 'XMP/EXPO ist wahrscheinlich aus' : 'XMP ist wahrscheinlich aus',
      intro: `Deine Riegel laufen mit ${r.configured} MT/s. Laut Teilenummer sind sie für ${r.rated} MT/s gebaut. Diesen Takt erreichen sie erst mit ihrem Speicherprofil, XMP bei Intel oder EXPO bei AMD. Ab Werk starten Mainboards meist mit dem langsameren Standard.`,
      html: sect('Was das in FiveM kostet', `<p>${COST}</p>`)
        + sect('So schaltest du es im BIOS ein', steps([
          'Starte den PC neu und drück beim Start mehrmals Entf oder F2, bis das BIOS erscheint.',
          'Schalte das Speicherprofil ein. Es heißt je nach Hersteller XMP, EXPO, D.O.C.P. (ASUS), A-XMP (MSI) oder Extreme Memory Profile (Gigabyte). Wähl Profil 1.',
          `Speichern mit F10 und neu starten. Der erste Start kann länger dauern${r.gen === 5 ? ', bei DDR5 auch ein paar Minuten mit schwarzem Bild: nicht ausschalten' : ''}.`,
        ]))
        + `<div class="hw-note">${ico('info')}<span>„Wahrscheinlich“, weil OrbitTweak nur Takt und Teilenummer vergleicht. Manche Mainboards und CPUs erlauben den vollen Takt nicht, etwa Intel-CPUs der 10. Generation und älter auf Boards mit H- oder B-Chipsatz. Läuft der PC mit dem Profil nicht stabil, lade im BIOS die Standardwerte. Einschalten kannst nur du es: OrbitTweak ändert nichts im BIOS.</span></div>`,
    };
    if (r.xmp === 'off') return {
      title: 'Deine Riegel könnten schneller laufen',
      intro: `Sie laufen mit ${r.configured} MT/s, laut Teilenummer können sie ${r.rated}. In Laptops legt meist das BIOS den Takt fest, einen Schalter für XMP gibt es dort selten, und viele Laptops laufen nicht schneller, als ihr Prozessor offiziell erlaubt.`,
      html: sect('Was du tun kannst', `<p>Schau im BIOS (meist F2 beim Start) nach einem Eintrag XMP oder Memory Profile. Gibt es keinen, bleibt es so. OrbitTweak kann daran nichts ändern.</p>`),
    };
    if (r.xmp === 'lower') return {
      title: 'Langsamer als angegeben',
      intro: `Deine Riegel laufen mit ${r.configured} MT/s, angegeben sind ${r.rated}. Das ist kein Standardtakt: entweder wurde er im BIOS so eingestellt, oder das Mainboard bremst, etwa mit vier Riegeln.`,
      html: sect('Was du tun kannst', `<p>Wolltest du das nicht selbst so, wähl im BIOS das Speicherprofil (XMP, EXPO oder D.O.C.P.). Läuft der PC damit nicht stabil, ist der niedrigere Takt die richtige Wahl.</p>`),
    };
    return {
      title: 'Deine RAM-Riegel',
      intro: r.xmp === 'unknown'
        ? `Sie laufen mit ${r.configured} MT/s. Ob sie mehr können, verrät ihre Teilenummer nicht: OrbitTweak kennt die Nummern der Gaming-Riegel von Corsair, G.Skill, Kingston, Crucial, TeamGroup, Patriot und ADATA. Riegel, die ab Werk in Komplett-PCs und Laptops stecken, laufen meist schon mit ihrem vollen Takt.`
        : 'So meldet Windows deine Riegel.',
      html: '',
    };
  }

  async function ram(kind, btn) {
    btn && (btn.disabled = true);
    view(`${head('ram', 'RAM-Check', 'Deine RAM-Riegel')}<div class="hw-wait">${ico('ram')}<b>Lese deine Riegel</b><span>Windows fragt die Steckplätze ab</span></div>
      <div class="act"><button class="btn lg" id="hwOk" data-esc>Schließen</button></div>`, 'hw-wide');
    $('#hwOk').onclick = close;
    const r = await api('hw.ram').catch(e => ({ ok: false, error: e.message }));
    btn && (btn.disabled = false);
    if (!open()) return;
    if (!r.ok || !(r.modules || []).length) {
      view(`${head('alert', 'RAM-Check', 'Riegel nicht lesbar', 'hw-bad')}<p>${esc(r.error || 'Windows meldet keine RAM-Riegel.')}</p>
        <div class="act"><button class="btn p lg" id="hwOk" data-esc>Schließen</button></div>`);
      $('#hwOk').onclick = close;
      return;
    }
    const b = ramBody(kind, r);
    view(`${head(kind === 'chan' ? 'hw-chan' : 'ram', 'RAM-Check', b.title)}
      <div class="hw-body"><p>${b.intro}</p>${modList(r)}${b.html}</div>
      <div class="act"><button class="btn p lg" id="hwOk" data-esc>Verstanden</button></div>`, 'hw-wide');
    $('#hwOk').onclick = close;
  }

  /* ------------------------------------------------------------ hooks ------------------------------------------------------------ */
  // Escape: the dialog's way out (Abbrechen, Zurück, Schließen). The palette takes its own keys before this runs.
  document.addEventListener('keydown', e => {
    if (e.key !== 'Escape' || e.defaultPrevented || !open()) return;
    const b = $('#mbox .hw-m [data-esc]');
    if (b && !b.disabled) { e.preventDefault(); b.click(); }
  });

  HEALTH_FIX.hw = (item, btn) => {
    const [, kind, ...rest] = String(item.fix).split(':'), arg = rest.join(':');
    if (kind === 'hz') return monitor(arg, btn);
    if (kind === 'hzback') return hzBack(item, btn);
    if (kind === 'ram') return ram(arg, btn);
  };

  /** Strg+K: open the Diagnose list at the monitor or RAM rows (checking first when it has not run yet). */
  window.hwFocus = async kind => {
    go('diag');
    for (let t = 0; !S.health && t < 150; t++) await sleep(100);
    await sleep(60);
    const want = kind === 'mon' ? /^(Monitor|Laptop-Bildschirm)/ : /^RAM-/;
    const rows = $$('#hList .hc').filter(r => want.test(r.querySelector('.tx b')?.textContent || ''));
    if (!rows.length) return toast(kind === 'mon' ? 'Kein Monitor-Ergebnis' : 'Kein RAM-Ergebnis',
      kind === 'mon' ? 'Windows hat keine Bildwiederholrate geliefert' : 'Windows verrät nicht genug über deine Riegel, um etwas zu sagen', false);
    rows[0].scrollIntoView({ block: 'center', behavior: 'smooth' });
    rows.forEach(r => { r.classList.remove('hw-flash'); void r.offsetWidth; r.classList.add('hw-flash'); });
    setTimeout(() => rows.forEach(r => r.classList.remove('hw-flash')), 2600);
  };

  /* ------------------------------------------------------------ browser preview ------------------------------------------------------------ */
  if (HOST) return;
  // ?hwmon=warn|multi|ok|hdmi60|switched|laptop|none  ?hwram=xmp|single|same|ok|laptop|unknown|lower|none|error  ?hwerr=fivem|refuse
  // ?hwfast: the countdown takes 4 instead of 15 seconds
  const q = new URLSearchParams(location.search), MON = q.get('hwmon') || 'warn', RAM = q.get('hwram') || 'xmp', ERR = q.get('hwerr') || '';
  const SECS = q.has('hwfast') ? 4 : 15;
  const D1 = '\\\\.\\DISPLAY1', D2 = '\\\\.\\DISPLAY2';
  const disp = (device, friendly, conn, w, h, hz, best, extra = {}) => ({ device, friendly, conn, primary: device === D1, w, h, hz, best, from: null, ...extra });
  const M = {
    pending: null,
    displays: {
      warn: [disp(D1, 'DELL S2721DGF', 'displayport', 2560, 1440, 60, 144)],
      multi: [disp(D1, 'DELL S2721DGF', 'displayport', 2560, 1440, 144, 144), disp(D2, 'LG 24MK430H', 'hdmi', 1920, 1080, 60, 60)],
      ok: [disp(D1, 'AOC 27G2SPU', 'displayport', 1920, 1080, 165, 165)],
      hdmi60: [disp(D1, 'AOC 24G2W1G4', 'hdmi', 1920, 1080, 60, 60)],
      switched: [disp(D1, 'DELL S2721DGF', 'displayport', 2560, 1440, 144, 144, { from: 60 })],
      laptop: [disp(D1, '', 'internal', 1920, 1080, 60, 165)],
      none: [],
    }[MON] || [],
  };
  const named = (d, i, all) => {
    const head = d.conn === 'internal' && !d.friendly ? 'Laptop-Bildschirm' : 'Monitor';
    return (all.length > 1 ? `${head} ${i + 1}` : head) + (d.friendly ? ` · ${d.friendly}` : '');
  };
  // the same rows as HwCheckLogic.MonitorRow
  function monRow(d, i, all) {
    const name = named(d, i, all), top = Math.max(d.hz, d.best);
    if (d.from) return { name, value: `${d.hz} Hz, von OrbitTweak umgestellt`, st: 'ok', fix: 'hw:hzback:' + d.device, act: `Auf ${d.from} Hz zurück` };
    if (d.best - d.hz >= 10 && d.best >= d.hz * 1.08) return { name, value: `Läuft mit ${d.hz} Hz, kann aber ${d.best} Hz`, st: 'warn', fix: 'hw:hz:' + d.device };
    if (top <= 75 && d.conn !== 'internal') return { name, value: `${d.hz} Hz, mehr bietet Windows hier nicht an. Kann dein Monitor laut Datenblatt mehr, bremst meist ` + (d.conn === 'hdmi' ? 'das HDMI-Kabel oder der Anschluss (DisplayPort probieren)' : 'Kabel oder Anschluss'), st: 'info' };
    return { name, value: top <= 75 ? `${d.hz} Hz, mehr bietet Windows hier nicht an` : `${d.hz} Hz, das Höchste, was Windows anbietet`, st: 'ok' };
  }
  const mod = (slot, bank, gb, gen, speed, part, maker, chan, rated) => ({ slot, bank, gb, gen, speed, part, maker, chan, rated });
  const RAMS = {
    xmp: { laptop: false, channel: 'multi', channels: 2, xmp: 'off', configured: 2133, rated: 3600, gen: 4,
      modules: [mod('DIMM_A2', 'P0 CHANNEL A', 16, 4, 2133, 'F4-3600C16-16GTZNC', 'G.Skill', 'A', 3600), mod('DIMM_B2', 'P0 CHANNEL B', 16, 4, 2133, 'F4-3600C16-16GTZNC', 'G.Skill', 'B', 3600)],
      rows: [{ name: 'RAM-Kanäle', value: 'Dual Channel · 2 × 16 GB', st: 'ok' }, { name: 'RAM-Takt', value: 'XMP wahrscheinlich aus: 2133 statt 3600 MT/s', st: 'warn', fix: 'hw:ram:xmp', act: 'Anleitung', guide: true }] },
    single: { laptop: false, channel: 'single', channels: 1, xmp: 'on', configured: 3200, rated: 3200, gen: 4,
      modules: [mod('ChannelA-DIMM1', 'BANK 1', 16, 4, 3200, 'KF3200C16D4/16GX', 'Kingston', 'A', 3200)],
      rows: [{ name: 'RAM-Kanäle', value: 'Single Channel: nur ein Riegel (16 GB)', st: 'warn', fix: 'hw:ram:chan', act: 'Anleitung', guide: true }, { name: 'RAM-Takt', value: '3200 MT/s DDR4, wie auf den Riegeln angegeben', st: 'ok' }] },
    same: { laptop: false, channel: 'single', channels: 1, xmp: 'off', configured: 4800, rated: 6000, gen: 5,
      modules: [mod('Controller0-ChannelA-DIMM0', 'BANK 0', 16, 5, 4800, 'CMK32GX5M2B6000C36', 'Corsair', 'C0-A', 6000), mod('Controller0-ChannelA-DIMM1', 'BANK 0', 16, 5, 4800, 'CMK32GX5M2B6000C36', 'Corsair', 'C0-A', 6000)],
      rows: [{ name: 'RAM-Kanäle', value: 'Single Channel: beide Riegel im selben Kanal', st: 'warn', fix: 'hw:ram:chan', act: 'Anleitung', guide: true }, { name: 'RAM-Takt', value: 'XMP/EXPO wahrscheinlich aus: 4800 statt 6000 MT/s', st: 'warn', fix: 'hw:ram:xmp', act: 'Anleitung', guide: true }] },
    ok: { laptop: false, channel: 'multi', channels: 2, xmp: 'on', configured: 6000, rated: 6000, gen: 5,
      modules: [mod('Controller0-ChannelA-DIMM1', 'BANK 0', 16, 5, 6000, 'CMK32GX5M2B6000C36', 'Corsair', 'C0-A', 6000), mod('Controller1-ChannelA-DIMM1', 'BANK 0', 16, 5, 6000, 'CMK32GX5M2B6000C36', 'Corsair', 'C1-A', 6000)],
      rows: [{ name: 'RAM-Kanäle', value: 'Dual Channel · 2 × 16 GB', st: 'ok' }, { name: 'RAM-Takt', value: '6000 MT/s DDR5, wie auf den Riegeln angegeben', st: 'ok' }] },
    laptop: { laptop: true, channel: 'multi', channels: 2, xmp: 'off', configured: 2666, rated: 3200, gen: 4,
      modules: [mod('ChannelA-DIMM0', 'BANK 0', 8, 4, 2666, 'KF3200C20S4/8G', 'Kingston', 'A', 3200), mod('ChannelB-DIMM0', 'BANK 2', 8, 4, 2666, 'KF3200C20S4/8G', 'Kingston', 'B', 3200)],
      rows: [{ name: 'RAM-Kanäle', value: 'Dual Channel · 2 × 8 GB', st: 'ok' }, { name: 'RAM-Takt', value: '2666 MT/s DDR4, die Riegel können 3200. Laptops bieten XMP im BIOS aber meist nicht an', st: 'info', fix: 'hw:ram:xmp' }] },
    unknown: { laptop: false, channel: 'unknown', channels: 0, xmp: 'unknown', configured: 2666, rated: 0, gen: 4,
      modules: [mod('DIMM1', '', 8, 4, 2666, 'M378A1K43CB2-CTD', 'Samsung', null, null), mod('DIMM2', '', 8, 4, 2666, 'M378A1K43CB2-CTD', 'Samsung', null, null)],
      rows: [{ name: 'RAM-Takt', value: '2666 MT/s DDR4. Ob deine Riegel mehr können, verrät ihre Teilenummer nicht', st: 'info', fix: 'hw:ram:info' }] },
    lower: { laptop: false, channel: 'multi', channels: 2, xmp: 'lower', configured: 3466, rated: 3600, gen: 4,
      modules: [mod('DDR4_A2', 'BANK 1', 16, 4, 3466, 'CMK32GX4M2D3600C18', 'Corsair', 'A', 3600), mod('DDR4_B2', 'BANK 3', 16, 4, 3466, 'CMK32GX4M2D3600C18', 'Corsair', 'B', 3600)],
      rows: [{ name: 'RAM-Kanäle', value: 'Dual Channel · 2 × 16 GB', st: 'ok' }, { name: 'RAM-Takt', value: '3466 MT/s DDR4, angegeben sind 3600: von Hand gesenkt oder vom Mainboard gebremst', st: 'info', fix: 'hw:ram:xmp' }] },
    none: { modules: [], rows: [] },
  };
  RAMS.error = { ...RAMS.xmp, error: true };
  const R = RAMS[RAM] || RAMS.xmp;

  const score = items => { const r = items.filter(i => i.st !== 'info'); return r.length ? Math.round(r.reduce((s, i) => s + (i.st === 'ok' ? 100 : i.st === 'warn' ? 50 : 0), 0) / r.length) : 100; };
  const baseCall = Mock.call;
  Mock.call = async function (cmd, a) {
    const r = await baseCall.call(this, cmd, a);
    if (cmd !== 'health' || !r || !Array.isArray(r.items)) return r;
    const items = r.items.slice(), at = items.findIndex(i => i.name === 'Speicherintegrität');
    items.splice(at < 0 ? items.length : at, 0, ...M.displays.map(monRow), ...R.rows.map(x => ({ ...x })));
    return { ...r, items, score: score(items) };
  };
  MOCK_EXT['hw.displays'] = async () => {
    await sleep(180);
    return { fivem: ERR === 'fivem', laptop: MON === 'laptop', revertIn: SECS,
      displays: M.displays.map((d, i, all) => ({ device: d.device, name: named(d, i, all), conn: d.conn, primary: d.primary, w: d.w, h: d.h, hz: d.hz, best: d.best, switchedFrom: d.from, pending: M.pending?.device === d.device })) };
  };
  MOCK_EXT['hw.hz.set'] = async a => {
    await sleep(700);
    const d = M.displays.find(x => x.device === a.device);
    // the same shapes as MainWindow.HwCheck.cs (HzResult: ok, error, device, from, to; set adds revertIn)
    const no = error => ({ ok: false, error, device: a.device, from: 0, to: 0 });
    if (ERR === 'fivem') return { ...no(FIVEM), revertIn: SECS };
    if (M.pending) return { ...no('Ein Monitor wartet noch auf „Behalten“. Bestätige ihn zuerst oder warte, bis OrbitTweak ihn zurückgestellt hat.'), revertIn: SECS };
    if (ERR === 'refuse' || !d) return { ...no('Diesen Modus lehnt Windows für den Monitor ab.'), revertIn: SECS };
    const from = d.from || d.hz;
    d.hz = d.best; d.from = from;
    M.pending = { device: d.device, t: setTimeout(() => { M.pending = null; d.hz = from; d.from = null; onEvent('hwhz', { ok: true, error: null, device: d.device, from: d.best, to: from }); }, SECS * 1000) };
    return { ok: true, error: null, device: d.device, from, to: d.best, revertIn: SECS };
  };
  MOCK_EXT['hw.hz.keep'] = async a => {
    if (!M.pending || M.pending.device !== a.device) return { ok: false, error: 'Zu spät: OrbitTweak hat die alte Bildwiederholrate schon wieder eingestellt.', device: a.device, from: 0, to: 0 };
    clearTimeout(M.pending.t); M.pending = null;
    const d = M.displays.find(x => x.device === a.device);
    return { ok: true, error: null, device: a.device, from: d.from, to: d.hz };
  };
  MOCK_EXT['hw.hz.back'] = async a => {
    await sleep(500);
    const pending = M.pending?.device === a.device;
    if (ERR === 'fivem' && !pending) return { ok: false, error: FIVEM, device: a.device, from: 0, to: 0 };
    const d = M.displays.find(x => x.device === a.device);
    if (!d || !d.from) return { ok: false, error: 'OrbitTweak hat an diesem Monitor nichts umgestellt.', device: a.device, from: 0, to: 0 };
    if (pending) { clearTimeout(M.pending.t); M.pending = null; }
    const was = d.hz; d.hz = d.from; d.from = null;
    return { ok: true, error: null, device: a.device, from: was, to: d.hz };
  };
  MOCK_EXT['hw.ram'] = async () => {
    await sleep(420);
    if (R.error || !R.modules.length) return { ok: false, error: 'Windows hat die RAM-Riegel nicht verraten (WMI antwortet nicht). Starte die Prüfung später noch einmal.' };
    const { rows, error, ...rest } = R;
    return { ok: true, ...rest, modules: R.modules.map(m => ({ ...m })) };
  };
})();
