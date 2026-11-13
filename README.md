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
