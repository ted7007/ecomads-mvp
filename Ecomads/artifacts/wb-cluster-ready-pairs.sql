\pset pager off
WITH pairs AS (
 SELECT split_part("Entity",':',1)::uuid AS campaign_id,split_part("Entity",':',2)::uuid AS article_id,
 count(*) FILTER(WHERE "State" IN ('data','zero','accepted')) AS confirmed_cluster_days
 FROM wb_source_quality WHERE "StoreId"='218f4679-4dc1-4543-b0e7-426e6f11d080' AND "Source"='clusters' AND "StartDate">='2026-10-12' AND "EndDate"<='2026-10-18' GROUP BY 1,2
)
SELECT c.wb_campaign_id,n.wb_nomenclature_id,p.confirmed_cluster_days,
 (SELECT count(DISTINCT s.date) FROM campaign_nomenclature_statistics s WHERE s.campaign_id=p.campaign_id AND s.nomenclature_id=p.article_id AND s.date>='2026-10-12' AND s.date<'2026-10-19') AS product_days
FROM pairs p JOIN campaigns c ON c.id=p.campaign_id JOIN nomenclatures n ON n.id=p.article_id
WHERE p.confirmed_cluster_days=7 ORDER BY 1,2;
SELECT wb_campaign_id,wb_created_at_utc FROM campaigns WHERE store_id='218f4679-4dc1-4543-b0e7-426e6f11d080' AND wb_campaign_id IN ('33406134','37548227');
