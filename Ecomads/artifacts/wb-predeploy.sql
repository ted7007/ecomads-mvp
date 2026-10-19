SELECT id, auto_refresh_enabled, auto_refresh_blocked, (api_key IS NOT NULL) AS connected FROM stores WHERE id='218f4679-4dc1-4543-b0e7-426e6f11d080';
SELECT status, count(*) FROM wb_sync_jobs WHERE store_id='218f4679-4dc1-4543-b0e7-426e6f11d080' GROUP BY status;
