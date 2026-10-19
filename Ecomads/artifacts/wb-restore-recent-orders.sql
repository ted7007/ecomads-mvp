BEGIN;
SELECT pg_advisory_xact_lock(hashtextextended('218f4679-4dc1-4543-b0e7-426e6f11d080',0));
WITH queued AS (
 INSERT INTO wb_sync_jobs (id,store_id,kind,start_date,end_date,campaign_ids_json,pair_ids_json,status,stage,wait_reason,next_campaign_offset,attempt_count,imported_rows,items_with_data,items_without_data,created_at_utc,updated_at_utc,next_attempt_at_utc)
 SELECT gen_random_uuid(),s.id,'funnel_recent','2026-10-12','2026-10-18','[]'::jsonb,NULL,'pending',
 CASE WHEN slots.next_at>now()+interval '5 seconds' THEN 'waiting' ELSE 'queued' END,
 CASE WHEN slots.next_at>now()+interval '5 seconds' THEN 'rate_limit' ELSE NULL END,
 0,0,0,0,0,now(),now(),GREATEST(now(),slots.next_at)
 FROM stores s LEFT JOIN LATERAL (SELECT last_request_at_utc+interval '30 minutes' AS next_at FROM wb_method_slots WHERE store_id=s.id AND method='funnel_history') slots ON true
 WHERE s.id='218f4679-4dc1-4543-b0e7-426e6f11d080' AND s.auto_refresh_enabled AND NOT s.auto_refresh_blocked
 AND NOT EXISTS (SELECT 1 FROM wb_sync_jobs WHERE store_id=s.id AND kind='funnel_recent' AND status IN ('pending','running'))
 RETURNING id,stage
)
INSERT INTO wb_sync_job_events (job_id,occurred_at_utc,stage,processed_count,attempt_number)
SELECT id,now(),stage,0,0 FROM queued RETURNING job_id;
COMMIT;

