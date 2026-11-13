-- OrbitTweak API (Cloudflare D1)
CREATE TABLE IF NOT EXISTS codes (
  code        TEXT PRIMARY KEY,      -- ORB-XXXX-XXXX-XXXX
  role        TEXT NOT NULL,         -- admin | editor
  note        TEXT,
  hwid        TEXT,                  -- bound PC (sha256), NULL until first redeem
  created_at  INTEGER NOT NULL,
  bound_at    INTEGER,
  last_seen   INTEGER,
  created_by  TEXT,
  revoked     INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS mods (
  id          TEXT PRIMARY KEY,
  data        TEXT NOT NULL,         -- JSON entry as used by the app library
  status      TEXT NOT NULL,         -- live | pending
  added_by    TEXT,                  -- code that submitted it
  created_at  INTEGER NOT NULL
);
