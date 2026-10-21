#!/usr/bin/env python3
"""Bounded, read-only VPS sampling. Never reads secrets or response bodies."""
import argparse
import datetime as dt
import json
import os
from pathlib import Path
import shutil
import subprocess
import time


SQL = '''
BEGIN READ ONLY;
SET LOCAL statement_timeout = '3s';
SET LOCAL lock_timeout = '500ms';
SELECT json_build_object(
 'database_bytes', pg_database_size(current_database()),
 'stores', (SELECT count(*) FROM stores),
 'connected_stores', (SELECT count(*) FROM stores WHERE api_key IS NOT NULL),
 'campaigns', (SELECT count(*) FROM campaigns),
 'tables', (SELECT json_agg(t) FROM (
   SELECT relname, n_live_tup, n_dead_tup, seq_scan, idx_scan,
          pg_total_relation_size(relid) AS bytes
   FROM pg_stat_user_tables ORDER BY pg_total_relation_size(relid) DESC LIMIT 20
 ) t),
 'jobs', (SELECT json_agg(j) FROM (
   SELECT kind, status, count(*) AS count,
     count(*) FILTER (WHERE status IN ('pending','running') AND pause_requested_at_utc IS NULL
       AND next_attempt_at_utc <= now()) AS eligible,
     max(extract(epoch FROM now()-created_at_utc)) FILTER
       (WHERE status IN ('pending','running')) AS oldest_active_seconds
   FROM wb_sync_jobs GROUP BY kind, status
 ) j),
 'captured_wb_last_15m', (SELECT json_agg(r) FROM (
   SELECT "Kind" AS kind, "HttpStatus" AS http_status, count(*) AS count,
      sum(octet_length("Body")) AS body_bytes,
      avg(extract(epoch FROM "ReceivedAtUtc"-"RequestedAtUtc"))*1000 AS mean_ms
   FROM wb_response_snapshots WHERE "RequestedAtUtc" >= now()-interval '15 minutes'
   GROUP BY "Kind", "HttpStatus"
 ) r),
 'postgres', (SELECT row_to_json(d) FROM (
   SELECT xact_commit, xact_rollback, blks_read, blks_hit, temp_bytes, deadlocks,
          tup_inserted, tup_updated, tup_deleted, stats_reset
   FROM pg_stat_database WHERE datname=current_database()
 ) d)
);
COMMIT;
'''


def command(args, timeout=10, stdin=None):
    result = subprocess.run(args, input=stdin, capture_output=True, text=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError((result.stderr or result.stdout)[-800:])
    return result.stdout


def proc_sample():
    cpu = [int(x) for x in Path('/proc/stat').read_text().splitlines()[0].split()[1:]]
    memory = {}
    for line in Path('/proc/meminfo').read_text().splitlines():
        key, value = line.split(':', 1)
        if key in ('MemTotal', 'MemAvailable', 'SwapTotal', 'SwapFree'):
            memory[key + '_bytes'] = int(value.split()[0]) * 1024
    disk = shutil.disk_usage('/')
    pressure = {}
    for kind in ('cpu', 'memory', 'io'):
        path = Path('/proc/pressure') / kind
        if path.exists():
            pressure[kind] = path.read_text().strip()
    return dict(cpu_ticks=cpu, memory=memory, load=os.getloadavg(), pressure=pressure,
                disk=dict(total_bytes=disk.total, used_bytes=disk.used, free_bytes=disk.free))


def collect(output, hours, interval):
    output.mkdir(parents=True, exist_ok=True)
    os.chmod(output, 0o700)
    with (output / 'metadata.json').open('w') as handle:
        json.dump(dict(started_utc=dt.datetime.now(dt.timezone.utc).isoformat(),
                       duration_hours=hours, interval_seconds=interval, cpu_count=os.cpu_count(),
                       limitations='WB journal does not capture all operational/settings requests; '
                                    'no authenticated API traffic is generated.'), handle, indent=2)
    deadline = time.monotonic() + hours * 3600
    previous = None
    next_database = 0
    with (output / 'samples.jsonl').open('a', buffering=1) as handle:
        while time.monotonic() < deadline:
            started = time.monotonic()
            sample = dict(utc=dt.datetime.now(dt.timezone.utc).isoformat())
            try:
                sample.update(proc_sample())
                if previous:
                    delta = [a-b for a, b in zip(sample['cpu_ticks'], previous)]
                    total = sum(delta[:8])  # guest time is already included in user/nice.
                    if total > 0:
                        sample['cpu_busy_percent'] = 100 * (total-delta[3]-delta[4]) / total
                        sample['cpu_iowait_percent'] = 100 * delta[4] / total
                        sample['cpu_steal_percent'] = 100 * delta[7] / total
                previous = sample['cpu_ticks']
                stats = command(['docker', 'stats', '--no-stream', '--format', '{{json .}}'], timeout=12)
                sample['containers'] = [json.loads(line) for line in stats.splitlines() if line]
                if started >= next_database:
                    try:
                        raw = command(['docker', 'exec', '-i', 'ecomads-db', 'psql', '-X', '-q', '-t', '-A',
                                       '-v', 'ON_ERROR_STOP=1', '-U', 'ecomads_user', '-d', 'ecomads_wb'],
                                      timeout=15, stdin=SQL)
                        sample['database'] = json.loads(raw)
                    except Exception as error:
                        sample['database_error'] = str(error)
                    next_database = started + 900
            except Exception as error:
                sample['error'] = str(error)
            sample['sampler_seconds'] = time.monotonic() - started
            handle.write(json.dumps(sample) + '\n')
            if handle.tell() > 32 * 1024 * 1024:
                raise RuntimeError('32 MiB observation output cap reached')
            time.sleep(max(0, min(interval - (time.monotonic() - started), deadline-time.monotonic())))
    (output / 'completed.json').write_text(json.dumps(dict(completed_utc=dt.datetime.now(dt.timezone.utc).isoformat())))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--hours', type=float, default=24)
    parser.add_argument('--interval', type=int, default=60)
    args = parser.parse_args()
    if not 0 < args.hours <= 48 or args.interval < 30:
        parser.error('hours must be in (0,48], interval must be >=30s')
    collect(args.output, args.hours, args.interval)
