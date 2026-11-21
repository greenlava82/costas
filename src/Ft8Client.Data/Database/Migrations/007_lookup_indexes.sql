-- Indexes for the duplicate lookups every contact upsert makes: call and band compared without case, within the
-- duplicate window, and a deleted contact's QRZ log id. The existing ix_qso_call cannot serve a NOCASE comparison, so
-- each lookup could only narrow by date. Large imports (an ADIF file, a first LoTW download, a Wavelog sync) make one
-- lookup per record while they hold the write lock, so faster lookups mean shorter waits for every other writer
-- (Sentry COSTAS-6, "database is locked", 2026-11-21).
CREATE INDEX IF NOT EXISTS ix_qso_call_band_nocase ON qso(call COLLATE NOCASE, band COLLATE NOCASE, qso_date_on);
CREATE INDEX IF NOT EXISTS ix_qso_deleted_qrz_logid ON qso_deleted(qrz_logid) WHERE qrz_logid IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_qso_deleted_call_band_nocase ON qso_deleted(call COLLATE NOCASE, band COLLATE NOCASE, qso_date_on);
