BEGIN;
SELECT pg_advisory_xact_lock(hashtextextended('218f4679-4dc1-4543-b0e7-426e6f11d080',0));
WITH pairs AS (
 SELECT DISTINCT jsonb_build_object('AdvertId',c.wb_campaign_id::bigint,'NmId',n.wb_nomenclature_id::bigint) AS pair
 FROM campaign_nomenclature_statistics d JOIN campaigns c ON c.id=d.campaign_id JOIN nomenclatures n ON n.id=d.nomenclature_id
 WHERE c.store_id='218f4679-4dc1-4543-b0e7-426e6f11d080' AND n.store_id=c.store_id AND c.wb_status IN (7,9,11)
 AND c.wb_campaign_id ~ '^[0-9]+$' AND n.wb_nomenclature_id ~ '^[0-9]+$'
 AND d.date>='2026-10-12' AND d.date<'2026-10-19'
 AND EXISTS (SELECT 1 FROM wb_campaign_daily_checks checks WHERE checks.campaign_id=c.id AND checks.date BETWEEN '2026-10-12' AND '2026-10-18' AND checks.result IN ('data','zero'))
), changed AS (
 UPDATE wb_sync_jobs j SET start_date='2026-10-12',end_date='2026-10-18',pair_ids_json=(SELECT jsonb_agg(pair) FROM pairs),updated_at_utc=now()
 WHERE j.id='74d0b2b6-1243-4cf3-b963-af658e9ee1c4' AND j.status='pending' AND j.next_campaign_offset=0 AND j.last_request_at_utc IS NULL
 AND NOT EXISTS (SELECT 1 FROM wb_response_snapshots r WHERE r."JobId"=j.id) AND EXISTS(SELECT 1 FROM pairs)
 RETURNING j.id,j.stage,j.next_attempt_at_utc
)
INSERT INTO wb_sync_job_events(job_id,occurred_at_utc,stage,error_code,processed_count,attempt_number)
SELECT id,now(),stage,'priority_latest_week',0,0 FROM changed RETURNING job_id;
COMMIT;
SELECT id,start_date,end_date,jsonb_array_length(pair_ids_json) AS pairs,next_attempt_at_utc AT TIME ZONE 'Europe/Moscow' AS next_msk FROM wb_sync_jobs WHERE id='74d0b2b6-1243-4cf3-b963-af658e9ee1c4';
