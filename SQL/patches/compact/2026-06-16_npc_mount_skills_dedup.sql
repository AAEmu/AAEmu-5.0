-- Target: Game/Data/compact.sqlite3 static client data.
-- Removes exact duplicate npc mount-skill pairs.
DELETE FROM npc_mount_skills
WHERE rowid NOT IN (SELECT MIN(rowid) FROM npc_mount_skills GROUP BY npc_id, mount_skill_id);
