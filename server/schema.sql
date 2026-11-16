-- OrbitTweak API (Cloudflare D1)
CREATE TABLE IF NOT EXISTS codes (
  code        TEXT PRIMARY KEY,      -- ORB-XXXX-XXXX-XXXX
  role        TEXT NOT NULL,         -- admin | editor | user
  note        TEXT,
  hwid        TEXT,                  -- bound PC (sha256), NULL until first redeem
  created_at  INTEGER NOT NULL,
  bound_at    INTEGER,
  last_seen   INTEGER,
  created_by  TEXT,
  revoked     INTEGER NOT NULL DEFAULT 0,
  days        INTEGER,               -- runtime in days, NULL = lifetime
  expires_at  INTEGER,               -- set on first redeem: bound_at + days
  hw2         TEXT,                  -- hardware-only hash of the bound PC (survives a Windows reinstall), NULL if unknown
  rebinds     INTEGER NOT NULL DEFAULT 0  -- how often the key moved to a new HWID of the same PC (reinstall)
);
CREATE TABLE IF NOT EXISTS mods (
  id          TEXT PRIMARY KEY,
  data        TEXT NOT NULL,         -- JSON entry as used by the app library
  status      TEXT NOT NULL,         -- live | pending
  added_by    TEXT,                  -- code that submitted it
  created_at  INTEGER NOT NULL
);
