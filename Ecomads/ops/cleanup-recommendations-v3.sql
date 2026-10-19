-- Run with psql after a verified pg_dump backup. Preview is the default.
-- Apply: psql -v apply_cleanup=true -v backup_verified=true -f cleanup-recommendations-v3.sql
\set ON_ERROR_STOP on
\if :{?apply_cleanup}
\else
  \set apply_cleanup false
\endif
\if :{?backup_verified}
\else
  \set backup_verified false
\endif

BEGIN;
-- Keep the preview and deletion on the same set, also excluding concurrent decision writes.
LOCK TABLE wb_recommendation_runs, wb_snapshot_decisions, wb_recommendation_decisions IN SHARE ROW EXCLUSIVE MODE;
CREATE TEMP TABLE recommendation_cleanup_ids ON COMMIT DROP AS
SELECT r."Id" FROM wb_recommendation_runs r
WHERE r."SchemaVersion" < 3
  AND NOT EXISTS (SELECT 1 FROM wb_snapshot_decisions d WHERE d."RunId" = r."Id")
  -- Legacy name-based decisions cannot be mapped reliably to an object. Preserve this seller's history.
  AND NOT EXISTS (SELECT 1 FROM wb_recommendation_decisions d WHERE d.seller_id = r."SellerId");
SELECT count(*) AS old_runs_to_delete FROM recommendation_cleanup_ids;
SELECT count(*) AS old_runs_preserved FROM wb_recommendation_runs
WHERE "SchemaVersion" < 3 AND "Id" NOT IN (SELECT "Id" FROM recommendation_cleanup_ids);

\if :apply_cleanup
  \if :backup_verified
    DELETE FROM wb_recommendation_runs r USING recommendation_cleanup_ids c WHERE r."Id" = c."Id";
    COMMIT;
  \else
    \echo 'Backup verification is required. No records deleted.'
    ROLLBACK;
    \quit 1
  \endif
\else
  ROLLBACK;
\endif
