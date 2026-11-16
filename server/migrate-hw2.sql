-- Keys survive a Windows reinstall on the same PC (hw2). Run once on an existing database:
--   wrangler d1 execute orbittweak --remote --file=migrate-hw2.sql
ALTER TABLE codes ADD COLUMN hw2 TEXT;
ALTER TABLE codes ADD COLUMN rebinds INTEGER NOT NULL DEFAULT 0;
