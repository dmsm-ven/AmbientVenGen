-- Ambient Forge, presets.db: one-time conversion of a database made by an older version.
--   1. tags move to their own dictionary table Tags (unique names), PresetTags refers to it by TagId;
--   2. the columns Name and Extension of FieldPresets are dropped (they are the end of FilePath).
-- File: %LOCALAPPDATA%\AmbientForge\presets.db. MAKE A COPY OF IT FIRST.
-- Run once, for example in DB Browser for SQLite ("Execute SQL"). ALTER TABLE ... DROP COLUMN needs SQLite 3.35+.

BEGIN TRANSACTION;

-- 1. the dictionary of tags
CREATE TABLE Tags (
    Id   INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT    NOT NULL UNIQUE COLLATE NOCASE
);

INSERT OR IGNORE INTO Tags (Name)
SELECT DISTINCT Tag FROM PresetTags ORDER BY Tag;

-- 2. the link table, now with TagId instead of the tag text
CREATE TABLE PresetTags_new (
    PresetId INTEGER NOT NULL REFERENCES FieldPresets (Id) ON DELETE CASCADE,
    TagId    INTEGER NOT NULL REFERENCES Tags (Id) ON DELETE CASCADE,
    PRIMARY KEY (PresetId, TagId)
);

INSERT OR IGNORE INTO PresetTags_new (PresetId, TagId)
SELECT pt.PresetId, t.Id
FROM PresetTags pt
JOIN Tags t ON t.Name = pt.Tag;

DROP TABLE PresetTags;
ALTER TABLE PresetTags_new RENAME TO PresetTags;
CREATE INDEX IF NOT EXISTS IX_PresetTags_TagId ON PresetTags (TagId);

-- 3. the columns that can be taken from FilePath
ALTER TABLE FieldPresets DROP COLUMN Extension;
ALTER TABLE FieldPresets DROP COLUMN Name;

COMMIT;

-- A check afterwards:
--   SELECT Name FROM Tags ORDER BY Name;                       -- the unique list for the filter
--   SELECT p.Id, p.FilePath, t.Name FROM FieldPresets p
--     JOIN PresetTags pt ON pt.PresetId = p.Id JOIN Tags t ON t.Id = pt.TagId;
