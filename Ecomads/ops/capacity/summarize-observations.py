"""Summarize a downloaded VPS JSONL observation; mark partial data explicitly."""
import argparse
import json
import math
from pathlib import Path
from datetime import datetime


def distribution(values):
    values = sorted(values)
    if not values:
        return None
    return dict(min=min(values), mean=sum(values)/len(values),
                p95=values[math.ceil(len(values)*.95)-1], max=max(values))


def summarize(path):
    lines = path.read_text(encoding='utf-8').splitlines()
    rows = []
    incomplete_lines = 0
    for line in lines:
        try:
            rows.append(json.loads(line))
        except json.JSONDecodeError:
            incomplete_lines += 1
    if not rows:
        raise ValueError('No complete samples')
    elapsed = (datetime.fromisoformat(rows[-1]['utc'])-datetime.fromisoformat(rows[0]['utc'])).total_seconds()
    databases = [row for row in rows if 'database' in row]
    growth = None
    if len(databases) >= 2:
        first, last = databases[0], databases[-1]
        seconds = (datetime.fromisoformat(last['utc'])-datetime.fromisoformat(first['utc'])).total_seconds()
        delta = last['database']['database_bytes']-first['database']['database_bytes']
        growth = dict(observed_seconds=seconds, delta_bytes=delta,
                      extrapolated_bytes_per_day=delta*86400/seconds if seconds >= 3600 else None)
    containers = {}
    for row in rows:
        for container in row.get('containers', []):
            containers.setdefault(container['Name'], []).append(float(container['CPUPerc'].rstrip('%')))
    return dict(samples=len(rows), incomplete_lines=incomplete_lines, observed_hours=elapsed/3600,
                first_utc=rows[0]['utc'], last_utc=rows[-1]['utc'], full_day=elapsed >= 23.9*3600,
                cpu_busy_percent=distribution([row['cpu_busy_percent'] for row in rows if 'cpu_busy_percent' in row]),
                available_memory_bytes=distribution([row['memory']['MemAvailable_bytes'] for row in rows if 'memory' in row]),
                disk_free_bytes=distribution([row['disk']['free_bytes'] for row in rows if 'disk' in row]),
                sampler_seconds=distribution([row['sampler_seconds'] for row in rows]),
                container_cpu_percent={name: distribution(values) for name, values in containers.items()},
                database_growth=growth, latest_database=databases[-1]['database'] if databases else None,
                errors=[dict(utc=row['utc'], error=row.get('error') or row.get('database_error')) for row in rows if row.get('error') or row.get('database_error')],
                limitations=['CPU host samples are interval averages; docker CPU is a short snapshot.',
                             'Database table row counts are PostgreSQL estimates.',
                             'Growth below one full day is preliminary; no purchasing conclusion from a short slope.',
                             'Captured WB journal excludes some operational and settings requests.'])


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('input', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = summarize(args.input)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(f"{result['samples']} samples / {result['observed_hours']:.2f}h, full day: {result['full_day']}, errors: {len(result['errors'])}")
