# OrbitTweak

FiveM Performance Suite für Windows 10/11: One-Click-Boost, Server-Hub, Launcher, GTA-V-Grafik-Tuner, Fadenkreuz-Overlay, Live-Farben, automatischer Kill-Clipper mit eingebauter Aufnahme, eigene Minimap aus einem Bild, Input-Lab (WASD), Cleaner und Health-Check.

Alle Optimierungen nutzen offizielle Windows-Einstellungen und lassen sich einzeln zurücksetzen. OrbitTweak injiziert nichts. FiveM-Dateien werden nur verändert, wenn du selbst über den Mod-Manager Mods oder ReShade installierst.

> **Hinweis:** Manche FiveM-Server verbieten Fadenkreuz-/Minimap-Overlays, Snap Tap und vor allem Mods (Pure-Mode). Prüfe vorher die Regeln deines Servers, die Nutzung erfolgt auf eigene Verantwortung.

## Download
Die aktuelle `OrbitTweak.exe` gibt es unter [Releases](https://github.com/Ky3ls/orbittweak/releases/latest). Die App aktualisiert sich danach selbst.

## Bauen
```
dotnet publish -c Release -o publish
```
Benötigt das .NET 8 SDK. Die Oberfläche liegt in `Web/` und wird per WebView2 angezeigt.

## Updates veröffentlichen
1. `<Version>` in `OrbitTweak.csproj` erhöhen und neu bauen.
2. GitHub-Release mit Tag `v<version>` anlegen und `OrbitTweak.exe` anhängen.
3. Zeilen im Release-Text, die mit `- ` beginnen, zeigt die App als Änderungen an.

## Mod-Bibliothek
Die App lädt die Bibliothek aus `mods.json` in diesem Repo (Fallback: eingebaute Kopie). Neue Mods einfach dort eintragen, die App zeigt sie ohne Update an:

```json
{ "id": "kill-redflash", "type": "file", "name": "Red Flash Kill-Effekt", "cat": "kill",
  "author": "Autor", "desc": "Kurze Beschreibung", "tags": ["pvp"], "popular": 80, "added": "2026-11-20",
  "img": "https://…/vorschau.jpg", "url": "https://github.com/…/releases/download/v1/redflash.zip",
  "sha256": "…", "size": 1234567 }
```
Kategorien: `reshade`, `kill`, `minimap`, `gfx`, `sound`, `other`. Erlaubt sind nur https-Links; Archive (.zip/.rar/.7z) müssen .rpf-Dateien enthalten. Nur Mods eintragen, die geteilt werden dürfen.

## Kill-Clipper
OrbitTweak nimmt das Spielfenster selbst auf (Windows Graphics Capture und der Hardware-Encoder der Grafikkarte, so wie OBS oder Discord) und behält nur die letzten Sekunden im Arbeitsspeicher. Bei einem Kill landet der Clip von selbst in `Videos\OrbitTweak`, per Clip-Taste geht es auch. FiveM muss dafür im Modus Randlos oder Fenster laufen. Wer lieber NVIDIA, Game Bar, AMD oder OBS nutzt, kann das unter Clipper → Einrichtung umstellen.

Kills werden ohne Kill-Feed erkannt, an dem, was das Spiel beim Kill direkt am Fadenkreuz zeigt. Die meisten PvP-Server blenden dort eine Meldung ein, zum Beispiel „KILLED Name“ mit „+3 Streak“. OrbitTweak liest nach eigenen Schüssen fünfmal pro Sekunde nur den Streifen in der Bildmitte mit der Windows-Texterkennung (auf zwei Arten, das Bild wie es ist und nur seine weiße Schrift; solange keine Meldung zu sehen ist, wechseln sich beide ab, das halbiert den Aufwand) und sucht darin nach Kill-Wörtern (einstellbar unter Clipper → Einrichtung → Kill-Wörter; Vorgabe: KILLED, KILL, GETÖTET, ELIMINATED, ELIMINIERT, ERLEDIGT). Zusätzlich wird das rote X erkannt, das GTA Online beim Kill am Fadenkreuz zeigt (FiveM-Server zeigen es meist nicht). Nichts wird aus dem Spiel gelesen, es ist ein Bildschirmausschnitt wie bei einem Screenshot. Solche Clips heißen „Kill …“, auch ein Kill mit einem einzigen Schuss wird so gespeichert.

Dazu kommt ein Sicherheitsnetz, das nur auf die eigenen Schüsse achtet (linke Maustaste oder Controller-Trigger): Solange auf einem Server noch kein Kill erkannt wurde, wird jedes Gefecht gespeichert („Gefecht …“). Sobald die Erkennung dort einmal angeschlagen hat, bleibt das Netz 30 Minuten still, damit keine Gefechte gespeichert werden, in denen man stirbt. Ein Gefecht mit erkanntem Kill wird nicht doppelt gespeichert. Wahlweise gibt es weiter „Kill-Feed lesen“ (Texterkennung, braucht den Ingame-Namen).

Wird FiveM direkt nach einem Kill geschlossen oder minimiert, bleibt die Aufnahme bestehen, bis der Clip geschrieben ist (höchstens 30 Sekunden). Ein Clip wird zuerst als `….mp4.part` geschrieben und erst umbenannt, wenn er vollständig ist.

**Highlight-Video:** Unter Clipper → Deine Clips fügt „Highlight-Video“ die Kill-Clips von heute oder alle zu einem einzigen Video zusammen (`Highlights Datum Uhrzeit.mp4`), wahlweise nur die letzten 8 Sekunden jedes Clips. Doppelt gespeicherte Kills (`… (2).mp4`) und unvollständige Dateien werden übersprungen. Das Zusammenfügen läuft in einem Hilfsprozess mit niedriger Priorität (dieselbe exe mit `--join`), der Code liegt in `Core/Highlight.cs`. Mit „Nach jeder Runde automatisch erstellen“ entsteht das Video von selbst, sobald FiveM geschlossen wird: aus den Kill-Clips, die seit dem Start des Spiels gespeichert wurden (ab zwei Clips).

**Kill-Einblendung:** Bei jedem erkannten Kill steht knapp zwei Sekunden lang „KILL 3“ oben in der Bildmitte, in der Akzentfarbe der App. Gezählt wird pro Runde, ein neu gestartetes FiveM beginnt wieder bei 1. Die Einblendung ist ein eigenes, durchklickbares Fenster über dem Spiel, so wie das Fadenkreuz (`Core/KillBanner.cs`); im Clip ist sie nicht zu sehen, weil nur das Spielfenster aufgenommen wird. Abschalten: Clipper → Einrichtung → „Kill-Einblendung im Spiel“.

**Teilen für Discord:** Der Kopieren-Knopf an einem Clip (oder „Für Discord kopieren“ in der Vorschau) legt den Clip als Datei in die Zwischenablage, in Discord reicht dann Strg+V. Ist der Clip größer als die Grenze unter „Teilen bis“ (10, 50 oder 500 MB), wird vorher eine kleinere Kopie gerechnet: Die Datenrate ergibt sich aus Grenze und Länge, bei wenig Platz wird das Bild auf 720p oder 540p und 30 Bilder pro Sekunde verkleinert. Das Original bleibt, wie es ist. Die Kopien liegen in `%LocalAppData%\OrbitTweak\share` und werden nach zwei Tagen entfernt.

**Galerie:** „Deine Clips“ lässt sich nach Kills, Gefechten und Highlights filtern. Eigene Clips (aus `Videos\OrbitTweak`) haben einen Papierkorb-Knopf, und im Filter „Gefechte“ gibt es „Alle in den Papierkorb“. Verschoben wird nur in den Windows-Papierkorb und nur aus diesem einen Ordner.

Was der Clipper bemerkt und gespeichert hat, steht in `%LocalAppData%\OrbitTweak\clipper.log` (in der App: Clipper → Einrichtung → Protokoll).

Die Aufnahme läuft in einem eigenen Hilfsprozess (dieselbe exe mit `--recorder`). Sollte in der Aufnahmekette von Windows oder dem Grafiktreiber etwas abstürzen, startet nur der Hilfsprozess neu, die App läuft weiter.

Der Code liegt in `Core/Recorder.cs` und `Core/RecorderHost.cs` (Aufnahme), `Core/KillText.cs` (Kill-Meldung lesen), `Core/KillMark.cs` (Ablauf und rotes X) und `Core/KillFeed.cs` (Kill-Feed). Zum Testen ohne Spiel gibt es im Debug-Build drei Umgebungsvariablen: `ORBIT_TEST_GAME=<Prozessname>` (ein Fenster dieses Prozesses zählt als Spiel), `ORBIT_TEST_FIRE=<Datei>` (genau solange die Datei existiert, gilt „es wird geschossen“; die echte Maus zählt dann nicht) und `ORBIT_TEST_DATA=<Ordner>` (eigener Ordner für Einstellungen und Arbeitsdaten, damit ein Testlauf die echten Einstellungen nicht anfasst). Eine solche Testinstanz lässt außerdem die echte Tastatur in Ruhe: Ihr Tastaturhaken reagiert nur auf besonders markierte Testtasten (F13 bis F16 stehen für W, A, S, D) und darf neben der normalen App laufen. Sie tippt auch selbst nichts: keine F8-Befehle und keine Tastenkürzel anderer Aufnahmeprogramme.

## WASD-Tweak
Im Input-Lab gibt es den WASD-Tweak in drei Teilen:

- **Laggen:** Solange FiveM vorne ist, lässt OrbitTweak gehaltene Lauftasten (W, A, S, D) immer wieder ganz kurz los, zum Beispiel alle 90 ms für 30 ms. Die Figur läuft dadurch abgehackt, im Stop-and-go. Aussetzer und Abstand sind einstellbar (Leicht, Mittel, Stark oder per Regler). Eine Taste wird erst angefasst, wenn sie 250 ms gehalten ist, Tippen im Chat bleibt also unberührt. Eine frei wählbare Taste schaltet das Laggen im Spiel an und aus, oben im Bild erscheint dann kurz „LAGGEN AN“ oder „LAGGEN AUS“. Das läuft über denselben Tastaturhaken wie Snap Tap (`Core/SnapTap.cs`); was mit den Tasten passiert, entscheidet `Core/WasdLogic.cs`. Diese Logik hat keine Windows-Aufrufe und wird in einer Simulation mit einer Million zufälliger Tastenereignisse geprüft: Das Spiel bekommt nie eine Taste gedrückt, die nicht gehalten wird.
- **Schnelle Tastenwiederholung:** schaltet die Windows-Anschlagverzögerung (Filter Keys) mit Wiederholung alle 15 ms ein, wie in manchen WASD-Packs. Wirkt sofort und gilt auch beim Tippen. Die Tastenkombination der Anschlagverzögerung bleibt, wie sie war, das Klickgeräusch pro Tastendruck wird nie eingeschaltet. Der Tweak „Bedienungshilfe-Popups aus“ schaltet nur das Kürzel aus, die schnelle Tastenwiederholung bleibt an.
- **Tastatur-Puffer:** `KeyboardDataQueueSize` des Tastaturtreibers in Stufen (50, 30, 25, 20, 16; Windows-Standard 100). Wirkt erst nach „Neu starten“ von Windows. Ein kleinerer Puffer macht Eingaben nicht schneller, er läuft nur früher über; die Stufen gibt es nur, weil WASD-Packs sie setzen.

Die Windows-Einstellungen liegen in `Core/WasdTweak.cs`. Manche Server verbieten WASD-Tweaks.

OrbitTweak läuft nur einmal: Ein zweiter Start holt das vorhandene Fenster nach vorn. Zwei Instanzen würden sich mit zwei Tastaturhaken gegenseitig Tastenereignisse wegnehmen (Tasten scheinen sich dann von selbst zu drücken) und jeden Clip doppelt speichern.

## Eigene Minimap aus einem Bild
Unter Mods → „Eigene Minimap“ gibt es zwei Wege:

**Bild drüberlegen (Standard):** OrbitTweak legt ein Bild (PNG, JPG, WebP) im Spiel einfach über die Minimap, als Overlay-Fenster wie das Fadenkreuz. An den Spieldateien wird nichts geändert, durchsichtige Stellen im PNG bleiben durchsichtig. Position und Größe werden in Pixeln ab der linken unteren Ecke des Spielbilds eingestellt („An die Minimap anlegen“ setzt die übliche Stelle), dazu Einpassen, Form (eckig, abgerundet, rund) und Deckkraft. „Auf dem Bildschirm zeigen“ blendet das Bild zum Ausrichten auch ein, wenn FiveM nicht vorne ist. Das Overlay ist nur zu sehen, solange FiveM im Modus Randlos oder Fenster vorne ist, und erscheint nicht in den Clips. Der Code dafür ist `CrosshairOverlay` in `Core/LiveFx.cs` und der Abschnitt „PICTURE OVER THE MINIMAP“ in `Web/app.js`.

**Karte ersetzen (Mod):** OrbitTweak baut aus dem Bild eine echte Minimap-Mod: die sechs Kartenkacheln von GTA (2 breit, 3 hoch, je 4500 Spieleinheiten) als Land- und Meer-Textur plus die Übersichtskarte, verpackt als `OrbitTweak-Minimap.rpf` in FiveMs `mods`-Ordner. „Bild wiederholen“ ist für Logos und Fotos gedacht, „Bild ist die ganze Karte“ für fertige Karten im Format 2:3 (Norden oben). Der Code liegt in `Core/MinimapBuilder.cs`.

## Bildnachweise
- Hintergründe: offizielle Screenshots aus Grand Theft Auto V (Steam-Store, Full HD). GTA V © Rockstar Games.
- Schrift der Überschriften im Anime-Theme: M PLUS Rounded 1c (nur lateinische Zeichen), Copyright 2016 The Rounded M+ Project Authors, SIL Open Font License 1.1. Lizenztext liegt unter `Web/fonts/OFL-MPLUSRounded1c.txt`.
- Banner-Bild im Anime-Theme: eigene Zeichnung, wird in `Web/app.js` (`animeScene()`) erzeugt.
- Controller-Grafiken: Gamepad Viewer Skin „Dracula“ (https://github.com/dracula/gamepad-viewer), MIT-Lizenz, Copyright (c) 2021 Dracula Theme. Lizenztext liegt unter `Web/pad/LICENSE.txt`.
