"""Extract current official WB endpoint quota tables; retain no seller data."""
import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import re
import urllib.request

SOURCES = {
    '08-promotion.yaml': [
        '/adv/v3/fullstats', '/adv/v1/upd', '/adv/v1/normquery/stats',
        '/api/advert/v2/adverts', '/adv/v0/normquery/get-bids', '/adv/v0/normquery/get-minus',
        '/api/advert/v2/budget', '/adv/v1/balance'],
    '11-analytics.yaml': [
        '/api/analytics/v3/sales-funnel/products', '/api/analytics/v3/sales-funnel/grouped/history',
        '/api/v2/search-report/product/search-texts', '/api/analytics/v1/stocks-report/seller-warehouses'],
    '12-reports.yaml': [
        '/api/v1/warehouse_remains',
        '/api/v1/warehouse_remains/tasks/{task_id}/status', '/api/v1/warehouse_remains/tasks/{task_id}/download']
}


def extract(document, endpoint):
    match = re.search(r'^  ' + re.escape(endpoint) + r':\s*\n(.*?)(?=^  /|^components:|\Z)', document, re.M | re.S)
    if not match:
        return {'endpoint': endpoint, 'found': False}
    block = match.group(1)
    tables = re.findall(r'^\s*\|.*\|\s*$', block, re.M)
    limits = [line.strip() for line in tables if re.search(r'Персональный|Сервисный|Базовый', line)]
    if not limits:
        limits = [line.strip() for line in tables if re.search(r'\d', line)]
    scope = next((line.strip() for line in block.splitlines() if 'на один профиль' in line or 'на один аккаунт' in line), None)
    return {'endpoint': endpoint, 'found': True, 'limits': limits, 'scope': scope}


def fetch(output):
    result = {'retrieved_utc': dt.datetime.now(dt.timezone.utc).isoformat(), 'sources': []}
    for name, endpoints in SOURCES.items():
        url = 'https://dev.wildberries.ru/api/swagger/yaml/ru/' + name
        request = urllib.request.Request(url, headers={'User-Agent': 'Ecomads-capacity-audit/1.0'})
        content = urllib.request.urlopen(request, timeout=30).read()
        document = content.decode('utf-8-sig')
        result['sources'].append({'url': url, 'sha256': hashlib.sha256(content).hexdigest(),
                                  'endpoints': [extract(document, endpoint) for endpoint in endpoints]})
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=Path('artifacts/capacity/wb-api-limits.json'))
    fetch(parser.parse_args().output)
