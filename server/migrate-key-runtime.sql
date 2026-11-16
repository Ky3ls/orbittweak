-- Keys with a runtime (2.24). Run once on an existing database:
--   wrangler d1 execute orbittweak --remote --file=migrate-key-runtime.sql
ALTER TABLE codes ADD COLUMN days INTEGER;
ALTER TABLE codes ADD COLUMN expires_at INTEGER;
