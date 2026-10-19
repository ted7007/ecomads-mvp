SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 2;
SELECT "Source", "State", count(*) FROM wb_source_quality WHERE "StoreId"='218f4679-4dc1-4543-b0e7-426e6f11d080' GROUP BY "Source", "State" ORDER BY 1,2;
SELECT kind, status, start_date, end_date, next_campaign_offset, error_code, wait_reason FROM wb_sync_jobs WHERE store_id='218f4679-4dc1-4543-b0e7-426e6f11d080' ORDER BY created_at_utc DESC LIMIT 12;
SELECT "Kind", "State", "HttpStatus", count(*) FROM wb_response_snapshots GROUP BY "Kind", "State", "HttpStatus" ORDER BY 1,2;

