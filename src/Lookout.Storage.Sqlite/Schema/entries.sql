-- Lookout entry storage schema.
-- WAL mode is applied programmatically on every connection open.

CREATE TABLE IF NOT EXISTS entries (
    id            TEXT    NOT NULL PRIMARY KEY,
    type          TEXT    NOT NULL,
    timestamp_utc INTEGER NOT NULL,
    request_id    TEXT    NULL,
    duration_ms   REAL    NULL,
    tags_json     TEXT    NOT NULL DEFAULT '{}',
    content_json  TEXT    NOT NULL DEFAULT '{}'
);

CREATE INDEX IF NOT EXISTS ix_entries_request_id
    ON entries (request_id);

CREATE INDEX IF NOT EXISTS ix_entries_timestamp_utc
    ON entries (timestamp_utc DESC);

-- Content-less FTS5 index over tags and content payload.
-- contentless_delete=1 permits row removal via the delete trigger below.
CREATE VIRTUAL TABLE IF NOT EXISTS entries_fts USING fts5 (
    tags_json,
    content_json,
    content = '',
    contentless_delete = 1
);

-- Keep the FTS index in sync when a row is inserted.
CREATE TRIGGER IF NOT EXISTS trg_entries_fts_insert
AFTER INSERT ON entries BEGIN
    INSERT INTO entries_fts (rowid, tags_json, content_json)
    VALUES (new.rowid, new.tags_json, new.content_json);
END;

-- Keep the FTS index in sync when a row is deleted.
-- contentless_delete=1 enables DELETE on the FTS virtual table directly.
CREATE TRIGGER IF NOT EXISTS trg_entries_fts_delete
AFTER DELETE ON entries BEGIN
    DELETE FROM entries_fts WHERE rowid = old.rowid;
END;
