"""Immediate storage estimate from record size, version frequency and retention.

No WB or production access. All rates are explicit scenario assumptions, not forecasts
of how often business rules will trigger. Protected decision evidence may outlive retention.
"""
import argparse
import json
from pathlib import Path


def estimate(clients, campaigns, versions, retention, low_bytes, high_bytes):
    if min(clients, campaigns, retention, low_bytes, high_bytes) <= 0 or versions < 0 or high_bytes < low_bytes:
        raise ValueError('Invalid capacity inputs')
    daily = campaigns * versions
    return dict(clients=clients, snapshots_per_client_day=daily,
                snapshots_retained=clients * daily * retention,
                low_bytes=clients * daily * retention * low_bytes,
                high_bytes=clients * daily * retention * high_bytes,
                low_decimal_gb=clients * daily * retention * low_bytes / 1e9,
                high_decimal_gb=clients * daily * retention * high_bytes / 1e9)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--campaigns', type=int, default=40)
    parser.add_argument('--pairs', type=int, default=80)
    parser.add_argument('--clusters-per-pair', type=int, default=3)
    parser.add_argument('--versions', type=float, default=70)
    parser.add_argument('--retention', type=int, default=90)
    parser.add_argument('--low-bytes', type=int, default=4096)
    parser.add_argument('--high-bytes', type=int, default=8192)
    parser.add_argument('--output', type=Path, default=Path('artifacts/capacity/recommendation-storage.json'))
    args = parser.parse_args()
    clusters = args.pairs * args.clusters_per_pair
    result = dict(
        assumptions=dict(campaigns=args.campaigns, pairs=args.pairs, clusters_per_pair=args.clusters_per_pair,
                         versions_per_campaign_day=args.versions, retention_days=args.retention,
                         record_low_bytes=args.low_bytes, record_high_bytes=args.high_bytes,
                         record_budget_includes='compressed JSONB, row, TOAST, indexes; planning range, not exact row size'),
        observed=dict(total_rows=65952, total_relation_bytes=294952960, index_bytes=19922944,
                      actual_physical_bytes_per_row=294952960 / 65952,
                      rows_last_24h=19318, campaigns_last_24h=282, versions_per_campaign_day=19318 / 282,
                      latest_sample_rows=200, latest_mean_compressed_jsonb_bytes=4654,
                      latest_mean_serialized_json_bytes=45464, latest_mean_cards_per_snapshot=.04,
                      latest_max_cards_per_snapshot=3,
                      caveat='Existing mixed-size production campaigns, not a representative forecast of new client rule triggers.'),
        rules=dict(cluster_subjects=clusters, article_subjects=args.pairs, campaign_subjects=args.campaigns,
                   rule_checks=2*clusters+args.pairs+args.campaigns,
                   loose_simultaneous_card_bound=clusters+args.pairs+args.campaigns,
                   assumptions='R01 disabled; two cluster rules mutually exclusive; R07 enabled only for applicable unified bidding.'),
        trigger_scenarios=[dict(fraction=fraction, cards_across_client=(clusters+args.pairs+args.campaigns)*fraction)
                           for fraction in (.05, .10, .20)],
        current_archive=[estimate(n,args.campaigns,args.versions,args.retention,args.low_bytes,args.high_bytes) for n in (1,10,50,100)],
        alternative_archive_policy=[estimate(n,args.campaigns,3,args.retention,args.low_bytes,args.high_bytes) for n in (1,10,50,100)],
        cadence_stress=[estimate(1,args.campaigns,720,args.retention,args.low_bytes,args.high_bytes)],
        limitations=['Storage table covers recommendation runs only, not the whole database or VPS disk.',
                     'Trigger fractions are explicit scenarios, not inferred business probabilities.',
                     '70 versions/day is normalized from existing data, not 70 new unique recommendations.',
                     'Three archived versions/day requires a new archive policy; current app does not enforce this limit.',
                     'Decision/exposure references can protect snapshots from 90-day deletion.',
                     '720 is the two-minute worker cadence scenario, not an absolute cap including HTTP calculations.'])
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    for row in result['current_archive']:
        print(f"{row['clients']} clients: {row['low_decimal_gb']:.2f}–{row['high_decimal_gb']:.2f} GB / {args.retention} days")
