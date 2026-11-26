-- WSPR spots: signals heard by wsprd in a 2-minute slot (docs/10-wspr.md section 4). One row per signal per slot and
-- profile callsign. upload_state is 0 (not for upload), 1 (waiting for WSPRnet), 2 (uploaded) or 3 (WSPRnet refused it);
-- the uploader (W2) works from state 1 only. A spot heard twice on the same frequency in the same slot is one row.
CREATE TABLE wspr_spot (
  id               INTEGER PRIMARY KEY,
  profile_callsign TEXT    NOT NULL,
  slot_utc         TEXT    NOT NULL,
  dial_hz          INTEGER NOT NULL,
  freq_hz          INTEGER NOT NULL,
  snr              INTEGER NOT NULL,
  dt               REAL    NOT NULL,
  drift            INTEGER NOT NULL,
  callsign         TEXT    NOT NULL,
  grid             TEXT,
  dbm              INTEGER NOT NULL,
  is_hashed        INTEGER NOT NULL DEFAULT 0,
  distance_km      REAL,
  upload_state     INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_wspr_spot_heard ON wspr_spot(profile_callsign, slot_utc, callsign, freq_hz);
CREATE INDEX ix_wspr_spot_time ON wspr_spot(slot_utc);
CREATE INDEX ix_wspr_spot_upload ON wspr_spot(upload_state, slot_utc) WHERE upload_state = 1;
