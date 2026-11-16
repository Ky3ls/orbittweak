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

## Endpunkte
- `GET /api/mods` – freigegebene Mods (öffentlich)
- `POST /api/redeem {code, hwid}` – Code einlösen, bindet ihn an den PC, liefert Token
- `GET /api/me` – eigene Rolle
- `POST /api/mods` – Mod hinzufügen (Editor: wartet auf Freigabe, Admin: sofort live). Der Server lädt die Datei einmal, prüft das Format und speichert Größe + SHA-256.
- Admin: `GET/POST /api/codes` (POST: `role`, `note`, `days`, `count`), `POST /api/codes/revoke|unbind`, `GET /api/mods/pending`, `POST /api/mods/approve|delete`
