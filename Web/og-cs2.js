'use strict';
/* 2.30 – Counter-Strike 2: browser preview data for the page "Andere Spiele" (og.js), the same shapes as Core/Games/Cs2.cs
   (camelCase, nulls included). The preview PC: NVIDIA card, one GPU, CS2 closed, settings found.
   ogCs2Preview('amd' | 'oldamd' | 'oldnvidia' | 'intel' | 'running' | 'nocfg' | 'missing' | 'nvidia') switches the preview to
   another state. */
OG_MOCK['cs2'] = {
  info: {
    id: 'cs2', name: 'Counter-Strike 2', installed: true,
    path: 'C:\\Program Files (x86)\\Steam\\steamapps\\common\\Counter-Strike Global Offensive',
    running: false, configFound: true,
    configPath: 'C:\\Program Files (x86)\\Steam\\userdata\\39734273\\730\\local\\cfg',
    note: 'Gilt für dein Steam-Konto „Max“. CS2 übernimmt die Werte beim nächsten Start.',
    antiCheat: 'Valve Anti-Cheat (VAC): OrbitTweak schreibt nur in cs2_video.txt und cs2_machine_convars.vcfg, also genau die Dateien, die das Einstellungsmenü von CS2 selbst schreibt, und setzt eine Windows-Grafikeinstellung. Es ändert keine Spieldateien und nichts am laufenden Spiel, setzt keine Startoptionen, und der Trusted Mode bleibt an.',
  },
  items: [
    {
      id: 'reflex', title: 'NVIDIA Reflex', kind: 'choice',
      sum: 'Senkt die Verzögerung zwischen Mausklick und Bild, ab GeForce GTX 900',
      desc: 'Schaltet die Latenzbremse, die in CS2 eingebaut ist (Einstellungen › Video › Erweitert › Frame-Pacing). Mit einer NVIDIA-Karte ist das Reflex, mit AMD ist es Anti-Lag 2. CS2 lässt dann keine Bilder mehr in der Warteschlange der Grafikkarte warten. Das senkt die Systemlatenz vor allem, wenn deine Grafikkarte voll ausgelastet ist (NVIDIA nennt bis zu 35 %), und kostet praktisch keine FPS. Standard ist „An“. „An + Boost“ gibt es nur bei NVIDIA: Es hält den Grafiktakt zusätzlich hoch. Das drückt die Verzögerung noch etwas weiter, kann laut Valve aber mehr Strom ziehen und die FPS leicht senken. Es lohnt sich also nur, wenn dir die niedrigste Latenz wichtiger ist als die höchsten FPS. Reflex im Spiel ersetzt den „Modus für niedrige Latenz“ im NVIDIA-Treiber. Du brauchst NVIDIA ab GTX 900 oder AMD ab RX 5000 mit aktuellem Treiber; mit Intel-Grafik bietet CS2 die Option nicht an. Wenn dir damit Ruckler auffallen, schalte es testweise aus.',
      value: '1', options: [{ v: '0', t: 'Aus' }, { v: '1', t: 'An (Standard)' }, { v: '2', t: 'An + Boost (nur NVIDIA)' }],
      min: null, max: null, step: null, unit: null,
      // like Cs2Logic.ReflexRec: "Empfohlene anwenden" turns it on but keeps a chosen boost (a getter, so it follows set and revert)
      rec: true, get recValue() { return this.value === '2' ? '2' : '1'; }, effect: 'latency',
      source: 'Eigene CS2-Einstellung (setting.r_low_latency). Tooltip von Valve: Boost kann mehr Strom ziehen und FPS kosten. NVIDIA: bis zu 35 % weniger Systemlatenz in CS2. AMD Anti-Lag 2 ist seit Treiber 24.6.1 in CS2 eingebaut.',
      needsClosed: true, available: true, why: '', changed: false,
    },
    {
      id: 'vsync', title: 'V-Sync', kind: 'switch',
      sum: 'Aus für die geringste Verzögerung, an nur zusammen mit G-Sync und Reflex',
      desc: 'Mit V-Sync wartet CS2 auf den Bildaufbau deines Monitors. Das verhindert Tearing (zerrissene Bilder), bringt aber ohne G-Sync oder FreeSync deutlich mehr Eingabeverzögerung. Deshalb ist es standardmäßig aus, und für die meisten bleibt es so am besten. Die Ausnahme: Mit einem G-Sync-Monitor und einer NVIDIA-Karte empfiehlt Valve im Spiel selbst, G-Sync, V-Sync und Reflex zusammen einzuschalten. Reflex begrenzt die FPS dann knapp unter deine Hz-Zahl. Du bekommst ein ruhiges Bild ohne Tearing bei kaum mehr Verzögerung, aber keine FPS mehr über der Bildwiederholrate. G-Sync selbst schaltest du im NVIDIA-Treiber ein, nicht hier.',
      value: false, options: null, min: null, max: null, step: null, unit: null,
      rec: false, recValue: null, effect: 'latency',
      source: 'Valve-Update vom 25.06.2024 und Hinweis im Spiel: G-Sync, V-Sync und Reflex zusammen empfohlen, sonst V-Sync aus.',
      needsClosed: true, available: true, why: '', changed: false,
    },
    {
      id: 'fpsmax', title: 'FPS-Limit im Spiel', kind: 'number',
      sum: 'Obergrenze für deine FPS im Match, 0 heißt kein Limit',
      desc: 'Das ist „Maximale FPS im Spiel“ aus den erweiterten Videoeinstellungen (fps_max), Standard ist 400. Mehr Bilder pro Sekunde verkürzen die Zeit bis zum nächsten Bild und damit die Verzögerung ein Stück, auch über deiner Hz-Zahl. Ein Limit, das dein PC sicher schafft, hält dafür die Frametimes gleichmäßiger und spart Strom, Hitze und Lüfterlärm. Das lohnt sich vor allem auf Laptops. 0 hebt das Limit ganz auf; im Spielmenü sind 64 bis 1000 einstellbar. Nutzt du G-Sync, V-Sync und Reflex zusammen, begrenzt CS2 ohnehin knapp unter deiner Hz-Zahl, dann ändert ein höheres Limit nichts.',
      value: 400, options: null, min: 0, max: 1000, step: 1, unit: 'FPS',
      rec: false, recValue: null, effect: 'fps',
      source: 'Valve-Update vom 25.06.2024 (Regler für fps_max). Standard 400 laut gameinfo.gi von CS2.',
      needsClosed: true, available: true, why: '', changed: false,
    },
    {
      id: 'contrast', title: 'Spielerkontrast verstärken', kind: 'switch',
      sum: 'Gegner heben sich in dunklen und kontrastarmen Ecken besser ab',
      desc: 'Das ist eine Option von CS2 selbst (Video › Erweitert): Ein Filter macht Spielermodelle vor kontrastarmem Hintergrund besser erkennbar. Sie ist standardmäßig an. Valve schreibt selbst, dass sie Grafikleistung kosten kann. Saubere Messungen dazu gibt es kaum; Einstellungs-Guides nennen höchstens 1 bis 2 % weniger FPS. Wenn auf einem sehr schwachen PC jedes Bild zählt, kannst du sie ausschalten. Dann siehst du Gegner in dunklen Ecken aber etwas schlechter.',
      value: true, options: null, min: null, max: null, step: null, unit: null,
      rec: true, recValue: true, effect: 'comfort',
      source: 'Tooltip von Valve im Spiel: bessere Erkennbarkeit, kann Leistung kosten. Standard an.',
      needsClosed: true, available: true, why: '', changed: false,
    },
    {
      id: 'netbuffer', title: 'Puffer gegen Paketverlust', kind: 'choice',
      sum: 'Nur bei wackliger Verbindung: weniger Aussetzer, dafür mehr Ping',
      desc: 'Das ist „Pufferung gegen Paketverlust/Jitter“ aus den Spieleinstellungen (cl_net_buffer_ticks). CS2 puffert dann 1 oder 2 Ticks an Daten zwischen dir und dem Server. Laut Valve fallen so weniger Ticks aus, dafür steigt deine effektive Ping-Zeit: bei 64 Ticks pro Sekunde um rund 16 ms je Tick. Das lohnt sich nur, wenn die Telemetrie-Anzeige im Spiel verpasste Ticks oder Paketverlust zeigt, typisch bei WLAN. Mit einer stabilen Verbindung lässt du es auf „Keiner“ (Standard).',
      value: '0', options: [{ v: '0', t: 'Keiner (Standard)' }, { v: '1', t: '1 Tick' }, { v: '2', t: '2 Ticks' }],
      min: null, max: null, step: null, unit: null,
      rec: false, recValue: null, effect: 'stability',
      source: 'Tooltip von Valve im Spiel: weniger verpasste Ticks, dafür höherer effektiver Ping.',
      needsClosed: true, available: true, why: '', changed: false,
    },
    {
      id: 'telemetry', title: 'FPS- und Ping-Anzeige', kind: 'choice',
      sum: 'CS2 blendet FPS und Ping selbst im HUD ein',
      desc: 'Schaltet die Telemetrie, die in CS2 eingebaut ist (Einstellungen › Spiel › Telemetrie), für „Frametime und FPS“ und für „Ping“: nie, nur bei schlechten Werten (Standard) oder immer. Das bringt selbst keine FPS. Du siehst aber, ob deine anderen Einstellungen etwas bewirken, und das ohne Overlay-Programme von Drittanbietern, die der Trusted Mode von CS2 oft blockiert.',
      value: '1', options: [{ v: '0', t: 'Nie' }, { v: '1', t: 'Nur bei schlechten Werten (Standard)' }, { v: '2', t: 'Immer' }],
      min: null, max: null, step: null, unit: null,
      rec: false, recValue: null, effect: 'comfort',
      source: 'CS2-Einstellung Spiel › Telemetrie. Laut Steam-Support blockiert der Trusted Mode Dateien von Drittprogrammen, die auf das Spiel zugreifen.',
      needsClosed: true, available: true, why: '', changed: false,
    },
    {
      id: 'gpu', title: 'Starke Grafikkarte für CS2', kind: 'switch',
      sum: 'Bei zwei Grafikchips läuft CS2 sicher auf der starken Karte',
      desc: 'Trägt cs2.exe in den Windows-Grafikeinstellungen auf „Hohe Leistung“ ein. Auf Laptops und PCs mit internem Grafikchip und zusätzlicher Grafikkarte verhindert das, dass CS2 versehentlich auf dem schwachen Chip läuft und du nur einen Bruchteil der FPS bekommst. Meist wählen Windows oder der Treiber die starke Karte schon selbst, dann ändert sich nichts. Mit nur einer Grafikkarte ist die Einstellung wirkungslos. Sie gilt ab dem nächsten Spielstart.',
      value: false, options: null, min: null, max: null, step: null, unit: null,
      rec: true, recValue: true, effect: 'fps',
      source: 'Windows-Grafikeinstellungen (Microsoft), derselbe Weg wie beim FiveM-Tweak. Wirkt nur mit zwei Grafikchips.',
      needsClosed: false, available: false, why: 'Nur eine Grafikkarte erkannt: hier ändert sich nichts', changed: false,
    },
  ],
};

/* The host refuses what CS2 does not take (Core/Games/Cs2Logic.cs); the preview does the same for this game. */
{
  const base = MOCK_EXT['og.set'];
  MOCK_EXT['og.set'] = async a => {
    if (a.game !== 'cs2') return base(a);
    const m = OG_MOCK['cs2'];
    const i = m.items.find(x => x.id === a.id);
    let value = a.value;
    if (i && i.available && !(i.needsClosed && m.info.running)) {
      if (i.kind === 'number') {
        value = Number(value);
        if (!Number.isInteger(value) || !(value === 0 || (value >= 64 && value <= 1000))) throw new Error('Wähle 0 (kein Limit) oder 64 bis 1000 FPS.');
      } else if (i.kind === 'choice') {
        value = String(value);
        if (!i.options.some(o => o.v === value)) {
          if (i.id === 'reflex' && value === '2') throw new Error('AMD Anti-Lag 2 hat keinen Boost. Wähle Aus oder An.');
          throw new Error(i.id === 'reflex' ? 'Wähle Aus, An oder An + Boost.' : i.id === 'netbuffer' ? 'Wähle Keiner, 1 Tick oder 2 Ticks.' : 'Wähle Nie, Nur bei schlechten Werten oder Immer.');
        }
      } else if (typeof value !== 'boolean') throw new Error('Ungültiger Wert.');
    }
    return base({ ...a, value });
  };
}

/** Preview of other states of this PC (for working on the page): the same texts the host gives in that case. */
function ogCs2Preview(kind) {
  const m = OG_MOCK['cs2'], it = id => m.items.find(x => x.id === id);
  const START = 'Starte CS2 einmal und schließ es wieder, dann legt es seine Einstellungen an.';
  const MISSING = 'Counter-Strike 2 wurde auf diesem PC nicht gefunden.';
  Object.assign(m.info, { installed: true, running: false, configFound: true, note: 'Gilt für dein Steam-Konto „Max“. CS2 übernimmt die Werte beim nächsten Start.',
    path: 'C:\\Program Files (x86)\\Steam\\steamapps\\common\\Counter-Strike Global Offensive', configPath: 'C:\\Program Files (x86)\\Steam\\userdata\\39734273\\730\\local\\cfg' });
  for (const i of m.items) if (i.id !== 'gpu') Object.assign(i, { available: true, why: '' });
  Object.assign(it('gpu'), { available: false, why: 'Nur eine Grafikkarte erkannt: hier ändert sich nichts' });
  const r = it('reflex');
  const nvidia = [{ v: '0', t: 'Aus' }, { v: '1', t: 'An (Standard)' }, { v: '2', t: 'An + Boost (nur NVIDIA)' }];
  // Cs2Logic.ReflexTitle, ReflexSum and LowLatencyWhy per vendor; 'old…' is a card older than GTX 900 or RDNA (by DeviceID)
  const SUM = 'Senkt die Verzögerung zwischen Mausklick und Bild';
  Object.assign(r, { title: 'NVIDIA Reflex', sum: SUM + ', ab GeForce GTX 900', options: nvidia });
  if (kind === 'amd' || kind === 'oldamd') { Object.assign(r, { title: 'AMD Anti-Lag 2', sum: SUM + ', ab Radeon RX 5000', options: nvidia.slice(0, 2) }); if (r.value === '2') r.value = '1'; }
  if (kind === 'oldamd') Object.assign(r, { available: false, why: 'Anti-Lag 2 gibt es erst ab Radeon RX 5000 (RDNA): auf diesem Grafikchip bietet CS2 es nicht an' });
  if (kind === 'oldnvidia') Object.assign(r, { available: false, why: 'Reflex gibt es erst ab GeForce GTX 900: auf dieser Grafikkarte bietet CS2 es nicht an' });
  if (kind === 'intel') Object.assign(r, { title: 'NVIDIA Reflex / AMD Anti-Lag 2', sum: SUM + ', ab NVIDIA GTX 900 oder AMD RX 5000', options: nvidia, available: false, why: 'Nur mit NVIDIA- oder AMD-Grafikkarte' });
  if (kind === 'running') Object.assign(m.info, { running: true, note: 'CS2 läuft gerade. Schließ es, bevor du hier etwas änderst, sonst überschreibt es die Werte beim Beenden.' });
  if (kind === 'nocfg') {
    Object.assign(m.info, { configFound: false, note: START });
    for (const i of m.items) if (i.id !== 'gpu') Object.assign(i, { available: false, why: START });
  }
  if (kind === 'missing') {
    Object.assign(m.info, { installed: false, configFound: false, path: null, configPath: null, note: MISSING + ' Installiere es über Steam und starte es einmal.' });
    for (const i of m.items) Object.assign(i, { available: false, why: MISSING });
  }
  return m;
}
