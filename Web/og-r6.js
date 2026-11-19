'use strict';
/* 2.30 – Rainbow Six Siege: browser preview data for the page "Andere Spiele" (og.js), the same shapes as Core/Games.
   Host: Core/Games/R6.cs (Windows part) and Core/Games/R6Logic.cs (items and texts, kept word for word here).
   The preview PC: Ubisoft Connect install, GeForce card, one GPU (so the GPU item is unavailable), two Ubisoft profiles. */
{
  /** Every GameItem field in the host's order (MainWindow serializes nulls too). */
  const item = o => ({
    id: '', title: '', sum: '', desc: '', kind: 'switch', value: null, options: null, min: null, max: null, step: null, unit: null,
    rec: false, recValue: null, effect: '', source: '', needsClosed: true, available: true, why: '', changed: false, ...o,
  });

  OG_MOCK['r6'] = {
    info: {
      id: 'r6',
      name: 'Rainbow Six Siege',
      installed: true,
      path: 'C:\\Program Files (x86)\\Ubisoft\\Ubisoft Game Launcher\\games\\Tom Clancy\'s Rainbow Six Siege',
      running: false,
      configFound: true,
      configPath: 'C:\\Users\\Max\\Documents\\My Games\\Rainbow Six - Siege\\3f2c9a1e-7b4d-4c8a-9e21-0a5b6c7d8e9f\\GameSettings.ini',
      note: '2 Ubisoft-Profile gefunden: OrbitTweak ändert nur das zuletzt gespielte',
      antiCheat: 'BattlEye und seit 2026 Ubisofts eigenes Kernel-Anti-Cheat. OrbitTweak ändert nur die GameSettings.ini, die auch das Einstellungsmenü des Spiels schreibt, und den Windows-Grafikeintrag für RainbowSix.exe. Spieldateien und das laufende Spiel fasst es nie an, und es steuert nie Maus oder Tastatur. Die GameSettings.ini schreibt es nur, wenn Siege geschlossen ist.',
    },
    items: [
      item({
        id: 'reflex', kind: 'choice', value: '0', rec: true, recValue: '1', effect: 'latency',
        title: 'NVIDIA Reflex',
        sum: 'Weniger Eingabeverzögerung auf GeForce-Karten, aber nicht mehr FPS',
        desc: 'Stellt im Spiel „NVIDIA Reflex Low Latency“ ein. Reflex lässt Prozessor und Grafikkarte keine Bilder auf Vorrat stapeln, so kommt deine Mausbewegung schneller auf den Bildschirm. NVIDIA nannte für Siege bis zu 25 % weniger Systemlatenz (2021, damals noch mit der Vulkan-Version gemessen), am meisten, wenn die Grafikkarte voll ausgelastet ist. Mehr FPS bringt es nicht, manchmal sind es sogar ein paar weniger. „An + Boost“ hält zusätzlich den Grafiktakt hoch, wenn der Prozessor bremst (in Siege bei hohen FPS häufig). Das spart vielleicht noch etwas Verzögerung, kostet aber Strom und Wärme. Geht nur mit einer NVIDIA-Grafikkarte ab GeForce GTX 10. Mit G-Sync und V-Sync im Treiber hält Reflex die FPS außerdem von selbst knapp unter der Bildwiederholrate.',
        source: 'NVIDIA: bis zu 25 % weniger Latenz in Siege, ab GeForce GTX 10; Schlüssel und Werte aus dem Kommentar der GameSettings.ini (2026)',
        options: [{ v: '0', t: 'Aus' }, { v: '1', t: 'An' }, { v: '2', t: 'An + Boost' }],
      }),
      item({
        id: 'vsync', kind: 'choice', value: '1', rec: true, recValue: '0', effect: 'latency',
        title: 'V-Sync',
        sum: 'Aus spart Eingabeverzögerung, dafür kann das Bild reißen',
        desc: 'Mit V-Sync wartet jedes fertige Bild auf den nächsten Bildaufbau des Monitors. Das verhindert Bildrisse, hält aber Bilder zurück und erhöht so die Verzögerung. Deshalb ist es in Shootern meist aus. Der Preis: Ohne V-Sync kann das Bild bei schnellen Drehungen reißen. Hast du einen G-Sync- oder FreeSync-Monitor, lass V-Sync hier aus und schalte es bei Bedarf in der Software deiner Grafikkarte ein, zusammen mit Reflex oder einem FPS-Limit knapp unter deiner Bildwiederholrate. „Jedes zweite Bild“ halbiert die Bildrate und ist nur etwas für sehr schwache PCs.',
        source: 'Ubisoft (2018): mit V-Sync wartet das Spiel auf den Bildschirm; Werte laut Kommentar in der GameSettings.ini',
        options: [{ v: '0', t: 'Aus' }, { v: '1', t: 'An' }, { v: '2', t: 'Jedes zweite Bild' }],
      }),
      item({
        id: 'fpslimit', kind: 'number', value: 0, min: 0, max: 500, step: 1, unit: 'FPS', effect: 'stability',
        title: 'FPS-Limit',
        sum: 'Deckelt die Bildrate: gleichmäßiger, kühler, weniger Prozessorlast',
        desc: 'Setzt das eingebaute Bildraten-Limit von Siege. Ubisoft rät selbst dazu, weil Siege ohne Limit und ohne V-Sync den Prozessor bis zum Anschlag auslasten kann. Mit Limit nutzt es nur so viel, wie es für die Ziel-FPS braucht. Das ergibt gleichmäßigere Bildzeiten, weniger Hitze und leisere Lüfter. Mit G-Sync oder FreeSync ist ein Limit ein paar FPS unter der Bildwiederholrate sinnvoll (z. B. 141 bei 144 Hz), damit der Monitor im variablen Bereich bleibt. Erreichst du das Limit nie, ändert es nichts. Liegt es weit unter dem, was dein PC schafft, kostet es FPS und ein wenig Reaktionszeit. 0 schaltet das Limit aus, sonst nimmt das Spiel mindestens 30.',
        source: 'Ubisoft-Beitrag „100% CPU usage“ (2018): FPSLimit in [DISPLAY] senkt die Prozessorlast; Mindestwert 30 laut Kommentar in der Datei',
      }),
      item({
        id: 'rawinput', kind: 'switch', value: false, rec: true, recValue: true, effect: 'comfort',
        title: 'Raw Input',
        sum: 'Siege liest die Maus direkt, ohne Windows-Beschleunigung',
        desc: 'Mit Raw Input liest Siege Maus und Tastatur direkt, statt den Umweg über den Windows-Mauszeiger zu nehmen. Die Zeigergeschwindigkeit und „Zeigerbeschleunigung verbessern“ aus Windows wirken dann nicht mehr aufs Zielen, jede Bewegung kommt eins zu eins an. Das empfehlen auch Seiten mit Profi-Einstellungen. Mehr FPS bringt es nicht. Hast du in Windows eine andere Zeigergeschwindigkeit als die Standardstufe, fühlt sich deine Empfindlichkeit danach anders an. Vereinzelt berichten Spieler, dass sich das Zielen mit Raw Input ungewohnt anfühlt. Dann schalte es wieder aus.',
        source: 'Option aus dem Spielmenü; ProSettings (Nov. 2025) empfiehlt An',
      }),
      item({
        id: 'windowmode', kind: 'choice', value: '2', effect: 'latency',
        title: 'Anzeigemodus',
        sum: 'Vollbild reagiert am direktesten, Rahmenlos ist am bequemsten',
        desc: 'Siege läuft seit 2025 nur noch mit DirectX 12. Im Vollbild gehört der Bildschirm dem Spiel allein: Bildwiederholrate, G-Sync und FreeSync greifen zuverlässig, die Verzögerung ist am niedrigsten. Rahmenlos ist unter Windows 11 mit DirectX 12 meist fast genauso schnell und Alt-Tab geht sofort. Es kann aber etwas Verzögerung kosten, wenn Overlays oder ein zweiter Bildschirm dazwischenfunken. Fenster ist nur etwas für nebenbei. Spürbar mehr FPS bringt keiner der Modi.',
        source: 'Werte laut Kommentar in der GameSettings.ini; nur noch DirectX 12 seit Y10S1 (Ubisoft)',
        options: [{ v: '0', t: 'Vollbild' }, { v: '2', t: 'Rahmenlos' }, { v: '1', t: 'Fenster' }],
      }),
      item({
        id: 'gpu', kind: 'switch', value: false, rec: true, recValue: true, effect: 'fps', needsClosed: false,
        available: false, why: 'Nur eine Grafikkarte erkannt: hier ändert sich nichts',
        title: 'Starke Grafikkarte für Siege',
        sum: 'Bei zwei Grafikchips läuft Siege sicher auf der starken Karte',
        desc: 'Trägt RainbowSix.exe in den Windows-Grafikeinstellungen auf „Hohe Leistung“ ein. Auf Laptops und PCs mit zwei Grafikchips (Prozessorgrafik und Grafikkarte) verhindert das, dass Siege auf dem schwachen Chip startet. Dort hättest du nur einen Bruchteil der FPS. Meist wählt Windows die starke Karte schon selbst, das hier ist das Sicherheitsnetz. Mit nur einer Grafikkarte ändert sich nichts. Es wirkt ab dem nächsten Spielstart.',
        source: 'Windows-Grafikeinstellung „Hohe Leistung“ pro App, wie beim FiveM-Tweak von OrbitTweak',
      }),
    ],
  };
  // like R6Logic.RecFor: "Empfohlene anwenden" turns Reflex on but keeps a chosen boost. The getter sits on the item
  // itself (inside item({ … }) the spread would read it once), so it follows og.set and og.revert.
  Object.defineProperty(OG_MOCK['r6'].items.find(i => i.id === 'reflex'), 'recValue', {
    get() { return this.value === '2' ? '2' : '1'; }, enumerable: true, configurable: true,
  });
}
