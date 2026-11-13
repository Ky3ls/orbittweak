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

## Endpunkte
- `GET /api/mods` – freigegebene Mods (öffentlich)
- `POST /api/redeem {code, hwid}` – Code einlösen, bindet ihn an den PC, liefert Token
- `GET /api/me` – eigene Rolle
- `POST /api/mods` – Mod hinzufügen (Editor: wartet auf Freigabe, Admin: sofort live). Der Server lädt die Datei einmal, prüft das Format und speichert Größe + SHA-256.
- Admin: `GET/POST /api/codes`, `POST /api/codes/revoke|unbind`, `GET /api/mods/pending`, `POST /api/mods/approve|delete`
