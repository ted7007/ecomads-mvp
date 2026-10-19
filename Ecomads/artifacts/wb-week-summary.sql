WITH days AS (
 SELECT o.date,o.order_sum,s.spend,q."State" AS spend_state
 FROM wb_store_daily_orders o JOIN wb_store_daily_spend s USING(store_id,date)
 JOIN wb_source_quality q ON q."StoreId"=o.store_id AND q."Source"='expenses' AND q."Entity"='' AND q."StartDate"=o.date AND q."EndDate"=o.date
 WHERE o.store_id='218f4679-4dc1-4543-b0e7-426e6f11d080' AND o.date BETWEEN '2026-10-12' AND '2026-10-18'
)
SELECT count(*) AS available_days,sum(order_sum) AS all_orders,
 count(*) FILTER(WHERE spend_state IN ('data','zero','accepted')) AS confirmed_expense_days,
 round(sum(spend) FILTER(WHERE spend_state IN ('data','zero','accepted'))*100/sum(order_sum) FILTER(WHERE spend_state IN ('data','zero','accepted')),2) AS preliminary_drr
FROM days;
SELECT next_attempt_at_utc AT TIME ZONE 'Europe/Moscow' AS next_cost_request_msk FROM wb_sync_jobs WHERE store_id='218f4679-4dc1-4543-b0e7-426e6f11d080' AND kind='expenses_month' AND status IN ('pending','running');
