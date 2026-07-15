-- Mate death / revive cooldown system.
-- Adds a nullable revive_time column to mates.
ALTER TABLE `mates`
    ADD COLUMN `revive_time` DATETIME NULL DEFAULT NULL AFTER `created_at`;
