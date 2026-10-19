\pset pager off
SELECT s.id,s.name,s.auto_refresh_enabled,s.auto_refresh_blocked,(s.api_key IS NOT NULL) connected FROM stores s WHERE s.seller_id=(SELECT seller_id FROM stores WHERE id='218f4679-4dc1-4543-b0e7-426e6f11d080');
SELECT c.wb_status, count(*) FROM campaigns c WHERE store_id='218f4679-4dc1-4543-b0e7-426e6f11d080' GROUP BY c.wb_status;
SELECT q."Source",q."State",q."StartDate",count(*) FROM wb_source_quality q WHERE q."StoreId"='218f4679-4dc1-4543-b0e7-426e6f11d080' AND q."StartDate">='2026-10-12' AND q."StartDate"<='2026-10-18' GROUP BY 1,2,3 ORDER BY 1,3,2;
SELECT d.date,count(*),sum(d.spend),sum(d.revenue),sum(d.orders) FROM campaign_statistics d JOIN campaigns c ON c.id=d.campaign_id WHERE c.store_id='218f4679-4dc1-4543-b0e7-426e6f11d080' AND d.date>='2026-10-12' AND d.date<'2026-10-19' GROUP BY d.date ORDER BY 1;
SELECT date,spend,loaded_at_utc FROM wb_store_daily_spend WHERE store_id='218f4679-4dc1-4543-b0e7-426e6f11d080' AND date BETWEEN '2026-10-12' AND '2026-10-18' ORDER BY date;
SELECT date,order_sum,order_count,loaded_at_utc FROM wb_store_daily_orders WHERE store_id='218f4679-4dc1-4543-b0e7-426e6f11d080' AND date BETWEEN '2026-10-12' AND '2026-10-18' ORDER BY date;
SELECT kind,status,start_date,end_date,next_campaign_offset,error_code,wait_reason,next_attempt_at_utc FROM wb_sync_jobs WHERE store_id='218f4679-4dc1-4543-b0e7-426e6f11d080' ORDER BY created_at_utc DESC LIMIT 8;
SELECT "Id","Kind","State","RequestedAtUtc","HttpStatus",length("Body") FROM wb_response_snapshots WHERE "StoreId"='218f4679-4dc1-4543-b0e7-426e6f11d080' ORDER BY "RequestedAtUtc" DESC LIMIT 12;
