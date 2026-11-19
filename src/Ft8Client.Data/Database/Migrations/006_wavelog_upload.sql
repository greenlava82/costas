-- Wavelog/Cloudlog delivery per contact, kept apart from QRZ and eQSL, so the Log view can show it (owner request
-- 2026-11-18). Contacts still waiting in the upload queue are marked queued; downloaded ones are already there.
ALTER TABLE qso ADD COLUMN wavelog_upload_state TEXT NOT NULL DEFAULT 'none';
ALTER TABLE qso ADD COLUMN wavelog_upload_error TEXT;
UPDATE qso SET wavelog_upload_state = 'queued' WHERE id IN (SELECT qso_id FROM upload_queue WHERE target = 'cloudlog');
UPDATE qso SET wavelog_upload_state = 'uploaded' WHERE source = 'wavelog';
