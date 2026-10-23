#!/usr/bin/env python3
"""Read-only runtime smoke checks. Never print secrets or personal API responses."""
import argparse
import base64
import hashlib
import hmac
import json
from pathlib import Path
import re
import subprocess
import time
import urllib.error
import urllib.request


def sql(query):
    return subprocess.check_output(['docker','exec','-i','ecomads-db','sh','-c',
        'exec psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At'], input=query, text=True).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--address', default='127.0.0.1')
    args = parser.parse_args()
    if not re.fullmatch(r'[0-9.]+', args.address):
        parser.error('IPv4 address required')
    info = json.loads(subprocess.check_output(['docker','inspect','ecomads-web'], text=True))[0]
    environment = dict(item.split('=', 1) for item in info['Config']['Env'])
    subject = sql("SELECT seller_id FROM stores WHERE api_key IS NOT NULL AND api_key <> '' ORDER BY id LIMIT 1;")
    def encode(value):
        return base64.urlsafe_b64encode(value).rstrip(b'=').decode()
    now = int(time.time())
    parts = [encode(json.dumps({'alg':'HS256','typ':'JWT'}).encode()),
             encode(json.dumps({'sub':subject,'iat':now,'exp':now+120,'iss':'ecomads_api','aud':'ecomads_clients'}).encode())]
    unsigned = '.'.join(parts)
    token = unsigned + '.' + encode(hmac.new(environment['JwtSettings__SecretKey'].encode(), unsigned.encode(), hashlib.sha256).digest())
    results = []
    def request(path, expected=200, authenticated=False, data=None):
        config = ['url = ' + json.dumps('https://ecomads.ru' + path)]
        if authenticated:
            config.append('header = ' + json.dumps('Authorization: Bearer ' + token))
        if data is not None:
            config.extend(['header = "Content-Type: application/json"', 'data = ' + json.dumps(json.dumps(data))])
        response = subprocess.run(['curl','--silent','--show-error','--connect-timeout','5','--max-time','30',
            '--resolve',f'ecomads.ru:443:{args.address}','--config','-','--write-out','\n%{http_code}'],
            input='\n'.join(config)+'\n', text=True, capture_output=True)
        if response.returncode:
            raise RuntimeError('TLS/network check failed: ' + path)
        body, code = response.stdout.rsplit('\n', 1)
        if int(code) != expected:
            raise RuntimeError(f'{path}: expected {expected}, got {code}; response body suppressed')
        results.append({'path':path,'status':int(code),'authenticated':authenticated})
        return body
    page = request('/')
    request('/health')
    request('/login')
    request('/api/auth/me', expected=401)
    me = json.loads(request('/api/auth/me', authenticated=True))
    if me['id'] != subject:
        raise RuntimeError('Authenticated subject mismatch')
    json.loads(request('/api/wb/stores', authenticated=True))
    request('/api/auth/login', expected=401, data={'email':'migration-check@example.invalid','password':'migration-check-invalid-password'})
    assets = re.findall(r'(?:src|href)="(/assets/[^" ]+)"', page)
    if not assets:
        raise RuntimeError('No frontend asset found')
    request(assets[0])
    telegram = 'not_configured'
    bot_token = environment.get('Telegram__BotToken')
    if bot_token:
        try:
            with urllib.request.urlopen('https://api.telegram.org/bot' + bot_token + '/getMe', timeout=15) as response:
                bot = json.load(response)
            if not bot.get('ok'):
                raise RuntimeError('Telegram getMe returned unsuccessful response')
            telegram = 'ok'
        except Exception:
            raise RuntimeError('Telegram read-only getMe failed; secret URL suppressed') from None
    report = {'address':args.address,'checks':results,'telegram_getMe':telegram,'image':info['Image']}
    print(json.dumps(report))
    if args.address == '127.0.0.1':
        Path('/opt/ecomads-migration/runtime-check.json').write_text(json.dumps(report, indent=2)+'\n')


if __name__ == '__main__':
    main()
