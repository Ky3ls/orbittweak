'use strict';
/* 2.30 – Fortnite: browser preview data for the page "Andere Spiele" (og.js), the same shapes and texts as
   Core/Games/Fortnite.cs + FortniteLogic.cs (the texts are generated from FortniteLogic.Defs, keep them in sync).
   Made-up PC: NVIDIA card, one GPU (desktop, so "Starke Grafikkarte erzwingen" is not available), 165 Hz monitor,
   Fortnite in DirectX 12 with V-Sync on. Like the host, Schatten follow the Rendermodus (none in Leistung), Reflex keeps
   "An + Boost" as its recommended value once it is set, and a running game refuses every ini item with the host's text. */
OG_MOCK['fortnite'] = {
  info: { id: 'fortnite', name: 'Fortnite', installed: true, path: 'C:\\Program Files\\Epic Games\\Fortnite', running: false, configFound: true, configPath: 'C:\\Users\\Max\\AppData\\Local\\FortniteGame\\Saved\\Config\\WindowsClient\\GameUserSettings.ini', note: 'Die Änderungen wirken ab dem nächsten Start von Fortnite.', antiCheat: 'Fortnite nutzt Easy Anti-Cheat. OrbitTweak ändert nur Werte, die Fortnite selbst in seinem Grafikmenü speichert (in der GameUserSettings.ini und nur bei geschlossenem Spiel), und die Windows-Grafikeinstellung pro App. Spieldateien und das laufende Spiel fasst es nie an.' },
  items: [
    {
      id: 'render',
      title: 'Rendermodus',
      sum: 'Leistung bringt meist mehr FPS, DirectX 12 das schönere Bild.',
      desc: 'Seit Update 40.10 (April 2026) gibt es in Fortnite kein DirectX 11 mehr, die Wahl ist DirectX 12 oder Leistung. Der Leistungsmodus rechnet mit einfacher Grafik: kein Lumen, kein Nanite, keine Schatten und einfachere Modelle. Über welches DirectX er läuft, wählt Fortnite seit Update 40.40 selbst passend zu deinem PC. Auf schwächeren Grafikkarten, Laptops und Onboard-Grafik bringt das deutlich mehr FPS und weniger Ruckler durch Shader-Kompilierung. DirectX 12 sieht viel besser aus, kostet aber mehr Leistung, und nach Spiel- oder Treiber-Updates ruckeln dort die ersten Runden, solange Fortnite seine Shader neu baut. Wie groß der Unterschied auf deinem PC ist, hängt von Hardware und Treiber ab. Vergleich beide Modi am besten selbst mit „FPS anzeigen“. Die Änderung wirkt ab dem nächsten Start von Fortnite.',
      kind: 'choice',
      value: 'dx12',
      options: [{ v: 'perf', t: 'Leistung' }, { v: 'dx12', t: 'DirectX 12' }],
      min: null,
      max: null,
      step: null,
      unit: null,
      rec: false,
      recValue: null,
      effect: 'fps',
      source: 'Epic: Patchnotes 37.00 (DX12-Leistungsmodus), 40.10 (DX11 entfernt) und 40.40 (Leistungsmodus wählt DirectX selbst); die Unreal Engine liest den Modus aus [D3DRHIPreference].',
      needsClosed: true,
      available: true,
      why: '',
      changed: false,
    },
    {
      id: 'reflex',
      title: 'NVIDIA Reflex',
      sum: 'Senkt die Eingabeverzögerung, vor allem bei voll ausgelasteter Grafikkarte.',
      desc: 'Reflex hält die Warteschlange zwischen Prozessor und Grafikkarte leer, damit deine Eingabe schneller auf dem Bildschirm landet. Am meisten bringt es, wenn die Grafikkarte voll ausgelastet ist. In einem Test von GameGPU (Oktober 2026, RTX 5090, Epic-Grafik, 1440p) sank die Systemlatenz in Fortnite von 41 auf 21 ms. Hängen deine FPS an einem Limit fest oder bremst der Prozessor, ist der Gewinn klein. „An + Boost“ hält zusätzlich den Grafiktakt hoch. Das hilft nur, wenn der Prozessor bremst, und kostet mehr Strom und Wärme. Auf Laptops nimmst du besser „An“. Reflex gibt es nur mit einer NVIDIA-Grafikkarte ab GeForce GTX 900.',
      kind: 'choice',
      value: '0',
      options: [{ v: '0', t: 'Aus' }, { v: '1', t: 'An' }, { v: '2', t: 'An + Boost' }],
      min: null,
      max: null,
      step: null,
      unit: null,
      rec: true,
      recValue: '1',
      effect: 'latency',
      source: 'NVIDIA (Reflex in Fortnite) und GameGPU-Latenztest 10/2026; Schlüssel aus Fortnites eigener GameUserSettings.ini.',
      needsClosed: true,
      available: true,
      why: '',
      changed: false,
    },
    {
      id: 'vsync',
      title: 'V-Sync',
      sum: 'Aus heißt weniger Verzögerung, aber Bildrisse sind möglich.',
      desc: 'Mit V-Sync wartet jedes Bild auf den Monitor. Das verhindert Bildrisse (Tearing), staut aber Bilder auf und erhöht die Eingabeverzögerung spürbar. Außerdem können die FPS dann nicht über die Hz-Zahl deines Monitors steigen. Für schnelle Shooter ist V-Sync aus die übliche Wahl. Der Preis: Ohne G-Sync oder FreeSync kann das Bild horizontal reißen. Mit G-Sync oder FreeSync bleibt das Bild ruhig, solange die FPS unter der Hz-Zahl bleiben. Setz dafür ein FPS-Limit ein paar FPS unter die Hz-Zahl.',
      kind: 'switch',
      value: true,
      options: null,
      min: null,
      max: null,
      step: null,
      unit: null,
      rec: true,
      recValue: false,
      effect: 'latency',
      source: 'Epic-Hilfe: V-Sync bindet die FPS an den Monitor. NVIDIA-Latenzleitfaden: V-Sync staut Bilder auf.',
      needsClosed: true,
      available: true,
      why: '',
      changed: false,
    },
    {
      id: 'fpslimit',
      title: 'FPS-Limit',
      sum: 'Ein festes Limit glättet die Bildzeiten und spart Strom.',
      desc: 'Ohne Limit rendert Fortnite so viele Bilder wie möglich. Schwanken deine FPS stark, fühlt sich ein Limit knapp unter dem, was dein PC stabil schafft, oft ruhiger an. Die Bildzeiten werden gleichmäßiger, und der PC wird weniger heiß und laut, auf Laptops hält der Akku länger. Mit G-Sync oder FreeSync stellst du das Limit ein paar FPS unter die Hz-Zahl des Monitors, dann bleibt das Bild ohne Tearing. Erkennt OrbitTweak deinen Monitor, bietet dir die Liste dafür auch seine Hz-Zahl und einen Wert 3 FPS darunter an. Der Preis: Mehr Bilder als das Limit bekommst du nicht, und ein unnötig tiefes Limit erhöht die Verzögerung leicht. Im Spielmenü geht das Limit bis 360 FPS. „Unbegrenzt“ hebt es auf, dafür muss V-Sync aus sein.',
      kind: 'choice',
      value: '144',
      options: [{ v: '0', t: 'Unbegrenzt' }, { v: '30', t: '30 FPS' }, { v: '60', t: '60 FPS' }, { v: '120', t: '120 FPS' }, { v: '144', t: '144 FPS' }, { v: '162', t: '162 FPS (G-Sync/FreeSync)' }, { v: '165', t: '165 FPS (Monitor)' }, { v: '240', t: '240 FPS' }, { v: '360', t: '360 FPS' }],
      min: null,
      max: null,
      step: null,
      unit: null,
      rec: false,
      recValue: null,
      effect: 'stability',
      source: 'Epic-Hilfe „FPS gesperrt oder begrenzt“ (max. 360, Unbegrenzt) und Blur Busters G-SYNC 101 (Limit unter der Hz-Zahl).',
      needsClosed: true,
      available: true,
      why: '',
      changed: false,
    },
    {
      id: 'shadows',
      title: 'Schatten',
      sum: 'In DirectX 12 einer der größten FPS-Fresser: Aus bringt viel.',
      desc: 'Auf hohen Stufen nutzt Fortnite in DirectX 12 virtuelle Schattenkarten (Virtual Shadow Maps), die die Grafikkarte stark belasten. In einer Community-Messung aus Kapitel 4 (2023) kosteten Schatten auf „Episch“ gut 40 % der FPS. Mit Schatten aus fällt diese Arbeit fast ganz weg, und viele Profis spielen so. Der Preis: Das Bild wirkt flacher, und kein Schatten verrät dir mehr einen Gegner hinter einer Kante. Die Voreinstellung im Spiel springt danach auf „Benutzerdefiniert“, das ist normal. Im Leistungsmodus gibt es keine Schatten-Einstellung.',
      kind: 'choice',
      value: '2',
      options: [{ v: '0', t: 'Aus' }, { v: '1', t: 'Mittel' }, { v: '2', t: 'Hoch' }, { v: '3', t: 'Episch' }],
      min: null,
      max: null,
      step: null,
      unit: null,
      rec: false,
      recValue: null,
      effect: 'fps',
      source: 'Epic-Techblog zu Virtual Shadow Maps in Fortnite. Die FPS-Zahl stammt aus einer älteren Community-Messung (2023).',
      needsClosed: true,
      available: true,
      why: '',
      changed: false,
    },
    {
      id: 'showfps',
      title: 'FPS anzeigen',
      sum: 'Zeigt dir die FPS, damit du jeden Tweak selbst prüfen kannst.',
      desc: 'Das ist Fortnites eigener FPS-Zähler aus den Videoeinstellungen. Er kostet praktisch keine Leistung. So siehst du am einfachsten, ob eine Änderung auf deinem PC wirklich etwas bringt, zum Beispiel Leistungsmodus gegen DirectX 12 oder Schatten aus.',
      kind: 'switch',
      value: false,
      options: null,
      min: null,
      max: null,
      step: null,
      unit: null,
      rec: false,
      recValue: null,
      effect: 'comfort',
      source: 'Fortnites eigene Videoeinstellung „FPS anzeigen“.',
      needsClosed: true,
      available: true,
      why: '',
      changed: false,
    },
    {
      id: 'gpu',
      title: 'Starke Grafikkarte erzwingen',
      sum: 'Auf PCs mit zwei GPUs läuft Fortnite sicher auf der schnellen.',
      desc: 'Auf PCs mit zwei Grafikchips entscheidet Windows, welcher ein Spiel rendert. Das sind meist Laptops mit Onboard-Grafik und einer zusätzlichen NVIDIA- oder AMD-Karte. Diese Einstellung ist dieselbe wie unter Einstellungen > System > Anzeige > Grafik > „Hohe Leistung“, nur für Fortnite. Meistens wählt der Treiber schon die richtige Karte. Landet Fortnite aber auf der Onboard-Grafik, hast du nur einen Bruchteil der FPS. Der Preis: mehr Akkuverbrauch. Die Einstellung wirkt ab dem nächsten Start, und an Fortnite selbst ändert sich nichts.',
      kind: 'switch',
      value: false,
      options: null,
      min: null,
      max: null,
      step: null,
      unit: null,
      rec: true,
      recValue: true,
      effect: 'fps',
      source: 'Windows-Grafikeinstellung pro App („Hohe Leistung“). Die gleiche Einstellung nutzt OrbitTweak schon für FiveM.',
      needsClosed: false,
      available: false,
      why: 'Nur bei PCs mit zwei Grafikkarten, z. B. Laptop mit Onboard- und Gaming-GPU.',
      changed: false,
    },
  ],
};

/* The host re-reads the file after each change; here the two rules that depend on another value are applied after
   og.set and og.revert. The game-independent mock commands stay in og.js. */
{
  const FN_RUNNING = 'Fortnite läuft gerade. Schließ das Spiel ganz, sonst überschreibt Fortnite die Änderung beim Beenden.';
  const FN_PERF_SHADOWS = 'Im Leistungsmodus hat Fortnite keine Schatten-Einstellung.';
  const sync = () => {
    const m = OG_MOCK['fortnite'], by = id => m.items.find(i => i.id === id);
    const r = by('render'), s = by('shadows'), x = by('reflex');
    if (r && s) { s.available = r.value !== 'perf'; s.why = s.available ? '' : FN_PERF_SHADOWS; }
    if (x) x.recValue = x.value === '2' ? '2' : '1';
  };
  const baseSet = MOCK_EXT['og.set'], baseRevert = MOCK_EXT['og.revert'];
  if (baseSet) MOCK_EXT['og.set'] = async a => {
    if (a && a.game === 'fortnite') {
      const m = OG_MOCK['fortnite'], i = m.items.find(x => x.id === a.id);
      if (i && i.available && i.needsClosed && m.info.running) throw new Error(FN_RUNNING);
    }
    const r = await baseSet(a);
    if (a && a.game === 'fortnite') sync();
    return r;
  };
  if (baseRevert) MOCK_EXT['og.revert'] = async a => {
    if (a && a.game === 'fortnite') {
      const m = OG_MOCK['fortnite'];
      if (m.info.running && m.items.some(i => i.changed && i.needsClosed)) throw new Error(FN_RUNNING);
    }
    const r = await baseRevert(a);
    if (a && a.game === 'fortnite') sync();
    return r;
  };
}
