# OrbitTweak

FiveM Performance Suite für Windows 10/11: One-Click-Boost, Server-Hub, Launcher, GTA-V-Grafik-Tuner, Fadenkreuz-Overlay, Live-Farben, Input-Lab (WASD), Cleaner und Health-Check.

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

## Bildnachweise
- Hintergründe: Screenshots aus Grand Theft Auto V. GTA V © Rockstar Games.
- Controller-Grafiken: Gamepad Viewer Skin „Dracula“ (https://github.com/dracula/gamepad-viewer), MIT-Lizenz, Copyright (c) 2021 Dracula Theme. Lizenztext liegt unter `Web/pad/LICENSE.txt`.
