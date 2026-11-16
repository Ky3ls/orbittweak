# OrbitTweak API (Cloudflare Worker + D1)

Speichert Zugangscodes (an die HWID gebunden) und die Mods, die Admins/Editoren zur Bibliothek hinzufügen.

## Einrichten (einmalig)
```
npm i -g wrangler
wrangler login
wrangler d1 create orbittweak          # database_id in wrangler.toml eintragen
wrangler d1 execute orbittweak --remote --file=schema.sql
wrangler secret put TOKEN_SECRET       # langen Zufallswert eingeben
wrangler deploy
```
Ersten Admin-Code anlegen (Code frei wählen im Format ORB-XXXX-XXXX-XXXX):
```
wrangler d1 execute orbittweak --remote --command "INSERT INTO codes (code, role, note, created_at) VALUES ('ORB-ABCD-EFGH-JKLM','admin','Owner',strftime('%s','now'))"
```
Danach die Worker-URL (z. B. `https://orbittweak-api.<name>.workers.dev`) in `mods.json` unter `"api"` eintragen und pushen. Alle Apps nutzen den Server dann automatisch.

## Keys mit Laufzeit (ab 2.24)
Keys können eine Laufzeit haben (1, 7, 30 Tage …, leer = Lifetime). Die Zeit startet beim ersten Einlösen, „PC lösen" startet sie nicht neu, abgelaufene Keys werden abgelehnt. Eine bestehende Datenbank einmal erweitern und den Worker neu hochladen:
```
wrangler d1 execute orbittweak --remote --file=migrate-key-runtime.sql
wrangler deploy
```
Neue Keys mit Laufzeit erzeugst du im Admin-Bereich der App (Laufzeit + Anzahl, bis zu 50 auf einmal) oder per Befehl:
```
wrangler d1 execute orbittweak --remote --command "INSERT INTO codes (code, role, note, created_at, days) VALUES ('ORB-TEST-0030-DAYS','user','Shop',strftime('%s','now'),30)"
```

## Key bleibt nach Windows-Neuinstallation (hw2)
Die HWID enthält die Windows-MachineGuid und ändert sich deshalb bei jeder Neuinstallation. Die App schickt zusätzlich `hw2` (Header `X-HW2` und beim Einlösen im Body): ein Hash nur aus Hardware-Daten (CPU-Name, Mainboard-Hersteller/-Modell/-Seriennummer, System-Seriennummer und -UUID aus der SMBIOS-Tabelle). Keine MachineGuid, keine MAC-Adressen, keine Laufwerks-Seriennummern. Hat ein PC nur Platzhalter wie „To be filled by O.E.M." oder „Default string", schickt die App kein `hw2`, dann bleibt alles wie bisher.

- Einlösen oder Anmelden mit neuer HWID, aber gleichem `hw2` → der Key zieht automatisch auf die neue HWID um, die App bekommt einen neuen Token (`/api/me` liefert dann `token`, `/api/redeem` liefert `rebound: true`). In der Code-Liste steht „Neuinstallation erkannt" (Spalte `rebinds`).
- Anderer PC (anderes `hw2`) → wie bisher „schon an einen anderen PC gebunden".
- Keys, die vor hw2 gebunden wurden, bekommen ihr `hw2` bei der nächsten Anmeldung vom selben PC.
- „PC lösen" löscht auch `hw2`. Alte App-Versionen ohne `hw2` funktionieren unverändert.

Bestehende Datenbank einmal erweitern, dann den Worker hochladen (der neue Worker läuft auch ohne Migration, nur ohne Umzug):
```
wrangler d1 execute orbittweak --remote --file=migrate-hw2.sql
wrangler deploy
```
Test der Server-Logik (Node 22, ohne Cloudflare): `node --no-warnings server/test-hw2.mjs`

## Endpunkte
- `GET /api/mods` – freigegebene Mods (öffentlich)
- `POST /api/redeem {code, hwid, hw2}` – Code einlösen, bindet ihn an den PC, liefert Token
- `GET /api/me` – eigene Rolle (nach einer Neuinstallation auch einen neuen `token`)
- `POST /api/mods` – Mod hinzufügen (Editor: wartet auf Freigabe, Admin: sofort live). Der Server lädt die Datei einmal, prüft das Format und speichert Größe + SHA-256.
- Admin: `GET/POST /api/codes` (POST: `role`, `note`, `days`, `count`), `POST /api/codes/revoke|unbind`, `GET /api/mods/pending`, `POST /api/mods/approve|delete`
