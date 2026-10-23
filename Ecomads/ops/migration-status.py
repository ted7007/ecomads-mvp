#!/usr/bin/env python3
"""Read-only migration evidence without exposing tokens, personal records or raw logs."""
import collections
import datetime
import json
from pathlib import Path
import re
import shutil
import subprocess


def sql(query):
    return subprocess.check_output(['docker','exec','-i','ecomads-db','sh','-c',
        'exec psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At'],input=query,text=True).strip()


query='''SELECT json_build_object(
'database_bytes',pg_database_size(current_database()),
'jobs_updated_since_cutover',(SELECT count(*) FROM wb_sync_jobs WHERE updated_at_utc >= '2026-10-23T15:27:00Z'),
'new_wb_responses',(SELECT count(*) FROM wb_response_snapshots WHERE "ReceivedAtUtc" >= '2026-10-23T15:27:00Z'),
'last_wb_response_utc',(SELECT max("ReceivedAtUtc") FROM wb_response_snapshots),
'wb_http_statuses',(SELECT json_object_agg(code,n) FROM (SELECT coalesce("HttpStatus"::text,'none') code,count(*) n FROM wb_response_snapshots WHERE "ReceivedAtUtc" >= '2026-10-23T15:27:00Z' GROUP BY "HttpStatus") t),
'latest_requested_slot_utc',(SELECT max(requested_slot_utc) FROM wb_refresh_schedules),
'latest_planned_slot_utc',(SELECT max(planned_slot_utc) FROM wb_refresh_schedules),
'jobs',(SELECT json_agg(t) FROM (SELECT kind,status,count(*) AS count FROM wb_sync_jobs GROUP BY kind,status ORDER BY kind,status) t));'''
report={'checked_at_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(), 'database':json.loads(sql(query))}
containers=json.loads(subprocess.check_output(['docker','inspect','ecomads-web','ecomads-db','ecomads-caddy'],text=True))
report['containers']=[{'name':c['Name'],'running':c['State']['Running'],'restart_count':c['RestartCount'],'image':c['Image']} for c in containers]
logs=subprocess.run(['docker','logs','--since','30m','ecomads-web'],text=True,capture_output=True)
text=logs.stdout+logs.stderr
markers=('CryptographicException','Telegram polling failed','WB automatic refresh pass failed','WB automatic refresh failed','WB sync worker failed before completing a job step','WB spool recovery failed')
report['log_markers']={marker:text.count(marker) for marker in markers}
report['logged_http_response_codes']=dict(collections.Counter(re.findall(r'Received HTTP response headers after [^\n]+ - (\d{3})',text)))
disk=shutil.disk_usage('/')
report['disk']={'free_bytes':disk.free,'total_bytes':disk.total,'free_percent':round(disk.free/disk.total*100,1)}
mem={line.split(':',1)[0]:line.split(':',1)[1].strip() for line in Path('/proc/meminfo').read_text().splitlines()}
report['memory']={k:mem[k] for k in ('MemTotal','MemAvailable')}
print(json.dumps(report))
