"""Deterministic planning model, not an observed WB traffic measurement."""
import argparse
import json
import math
from pathlib import Path


def operational_day(campaigns=40, pairs=80):
    """Ideal 2-minute worker cadence, successful single-page sources except unsupported FBS.

    Actual serial passes slow down at scale: these are demand estimates, not achieved rates.
    FBW report is assumed ready on the first status check. FBS attempts can return 403.
    """
    counts = dict(campaign_list=0, settings_bids=0, settings_minus=0, budgets=0,
                  balance=0, stocks_fbs_attempts=0, fbw_create=0, fbw_status=0,
                  fbw_download=0, demand_products=0)
    last = {}
    report_stage = 0
    report_ready_at = -10000
    for minute in range(0, 1440, 2):
        for kind, interval in [('campaign_list', 60), ('settings_bids', 60),
                               ('settings_minus', 60), ('budgets', 15), ('balance', 30),
                               ('stocks_fbs_attempts', 30), ('demand_products', 360)]:
            if minute - last.get(kind, -10000) >= interval:
                counts[kind] += math.ceil(pairs / 100) if kind.startswith('settings_') else 1
                last[kind] = minute
        if report_stage == 0 and minute - report_ready_at < 30:
            continue
        kind = ['fbw_create', 'fbw_status', 'fbw_download'][report_stage]
        if minute - last.get(kind, -10000) < 15:
            continue
        counts[kind] += 1
        last[kind] = minute
        report_stage = (report_stage + 1) % 3
        if report_stage == 0:
            report_ready_at = minute
    return counts


def budget(stores, campaigns=40, products=100, pairs=80, pages=1):
    batches = math.ceil(campaigns / 50)
    queued = dict(fullstats=3 * batches, expenses=4, funnel_history=2,
                  funnel_month=12 * pages, product_orders=8 * pages,
                  clusters=2 * math.ceil(pairs / 100), jam=2 * math.ceil(products / 50))
    operational = operational_day(campaigns, pairs)
    # Demand and the other products callers share the same slot in the real WbRateLimits code.
    funnel_products = queued['funnel_month'] + queued['product_orders'] + operational['demand_products'] * pages
    history_pairs = math.ceil((365 - 30) / 2) * pages
    spare = max(0, 48 - funnel_products)
    queued_count = sum(queued.values())
    return dict(stores=stores, per_store_queued_requests=queued, per_store_operational_attempts=operational,
                per_store_total_attempts=queued_count + sum(operational.values()),
                total_attempts_per_day=stores * (queued_count + sum(operational.values())),
                queued_request_steps_per_day=stores * queued_count,
                queue_pause_hours_lower_bound=stores * queued_count * 15 / 3600,
                queue_pause_only_utilization=stores * queued_count * 15 / 86400,
                products_slot=dict(regular_requests=funnel_products, daily_limit=48, spare_requests=spare,
                    nominal_older_history_requests=history_pairs,
                    optimistic_history_days=math.ceil(history_pairs / spare) if spare else None),
                warnings=[
                    'Operational calls run outside WbSyncWorker; do not multiply them by its 15-second pause.',
                    'The 15-second total is a lower bound: HTTP, SQL, idle/check steps, retries and history add time.',
                    'FBS attempts are counted as attempts; ordinary basic-token access may be unavailable.',
                    'Extra cluster repairs, stock pagination, report polling, manual sync and 429 retries are excluded.',
                    'Base case assumes separate WB seller profiles; multiple local stores of one seller do not create new WB quota.',
                    'Different methods use existing application slot mapping; possible WB shared groups need separate verification.',
                    'History days are a quota-volume lower bound, not a completion-time SLA.'
                ])


class Slots:
    """Small virtual-time quota test model. Keyed by WB seller identity and endpoint/group."""
    def __init__(self):
        self.next = {}

    def reserve(self, seller, method, now, interval):
        key = seller, method
        if now < self.next.get(key, 0):
            return False
        self.next[key] = now + interval
        return True

    def retry_after(self, seller, method, now, seconds):
        key = seller, method
        self.next[key] = max(self.next.get(key, 0), now + seconds)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=Path('artifacts/capacity/request-budget.json'))
    args = parser.parse_args()
    result = dict(kind='planning model, not live measurement', assumptions=dict(campaigns=40, catalog_products=100,
        campaign_product_pairs=80, basic_token=True, refreshes_per_day=2, results_enabled=True,
        worker_cadence_minutes=2, fbs_attempts_included=True, pages=1),
        scenarios=[budget(count) for count in (10, 50, 100)],
        pagination_sensitivity=[budget(100, pages=pages) for pages in (2, 3)])
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    for row in result['scenarios']:
        print(f"{row['stores']} stores: {row['total_attempts_per_day']} attempts/day, "
              f"queue pause floor {row['queue_pause_hours_lower_bound']:.2f}h/day, "
              f"products slots {row['products_slot']['regular_requests']}/48")
