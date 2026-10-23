#!/usr/bin/env python3
"""Restore a checked snapshot on the new VPS only; never starts web.

--final requires a final snapshot and exact row-count agreement. The prior
trial database on this target is deliberately replaced by the final database.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import time


def run(*args, **kwargs):
    return subprocess.run(args, check=True, **kwargs)


def output(*args):
    return subprocess.check_output(args, text=True).strip()


def sql(query):
    return subprocess.check_output([
        'docker', 'exec', '-i', 'ecomads-db', 'sh', '-c',
        'exec psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At',
    ], input=query, text=True).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('snapshot', type=Path)
    parser.add_argument('--final', action='store_true')
    args = parser.parse_args()
    if os.geteuid() != 0 or socket.gethostname() != 'qkoldonqbv':
        parser.error('Restore is restricted to root on new VPS qkoldonqbv')
    web = subprocess.run(['docker', 'inspect', 'ecomads-web', '--format', '{{.State.Running}}'], capture_output=True, text=True)
    if web.returncode == 0 and web.stdout.strip() == 'true':
        parser.error('Stop target web before restoring')
    snapshot = args.snapshot.resolve()
    if not snapshot.is_relative_to(Path('/opt/ecomads-migration')):
        parser.error('Snapshot must be inside /opt/ecomads-migration')
    for line in (snapshot / 'SHA256SUMS').read_text().splitlines():
        expected, name = line.split('  ', 1)
        if name not in ('database.dump', 'application.tar.gz', 'manifest.json'):
            parser.error('Unexpected checksum filename')
        h = hashlib.sha256()
        with (snapshot / name).open('rb') as file:
            for chunk in iter(lambda: file.read(1024 * 1024), b''):
                h.update(chunk)
        if h.hexdigest() != expected:
            raise RuntimeError('Checksum mismatch: ' + name)
    manifest = json.loads((snapshot / 'manifest.json').read_text())
    if bool(manifest['final']) != args.final:
        parser.error('Final/preliminary snapshot mode mismatch')
    for name, image in manifest['images'].items():
        actual = output('docker', 'image', 'inspect', image['tag'], '--format', '{{.Id}}')
        if actual != image['id']:
            raise RuntimeError('Image mismatch: ' + name)
    unpack = snapshot / 'extracted'
    unpack.mkdir(mode=0o700, exist_ok=True)
    run('tar', '--no-same-owner', '-xzf', str(snapshot / 'application.tar.gz'), '-C', str(unpack))
    root = Path('/opt/ecomads')
    shutil.copytree(unpack / 'ecomads', root, dirs_exist_ok=True, symlinks=True)
    # Numeric source account IDs must not grant ownership to new operators.
    run('chown', '-hR', 'root:root', str(root))
    root.chmod(0o755)
    (root / '.env').chmod(0o600)
    caddyfile = root / 'Caddyfile'
    original_caddyfile = root / 'Caddyfile.before-migration-20261024'
    caddy_source = original_caddyfile if args.final and original_caddyfile.is_file() else caddyfile
    caddyfile.write_text(caddy_source.read_text().replace('http://31.76.53.166', 'http://31.207.76.37'))
    # Restore the root-owned operations helpers and timers from production.
    for name in ('usr/local/sbin/ecomadsctl', 'usr/local/lib/ecomads-set-secret.py',
                 'etc/systemd/system/ecomads-log-archive.service', 'etc/systemd/system/ecomads-log-archive.timer'):
        destination = Path('/') / name
        shutil.copy2(unpack / 'system' / name, destination)
        os.chown(destination, 0, 0)
    for name in ('dataprotection_keys', 'wb_response_journal', 'caddy_data', 'caddy_config'):
        volume = 'ecomads_' + name
        run('docker', 'volume', 'create', volume, stdout=subprocess.DEVNULL)
        mount = Path(output('docker', 'volume', 'inspect', volume, '--format', '{{.Mountpoint}}'))
        shutil.copytree(unpack / 'volumes' / name, mount, dirs_exist_ok=True, symlinks=True)
    compose = ['docker', 'compose', '--env-file', str(root / '.env'), '-f', str(root / 'compose.production.yml'), '-p', 'ecomads']
    run(*compose, 'config', '--quiet')
    run(*compose, 'up', '-d', '--no-deps', 'db')
    for _ in range(60):
        if output('docker', 'inspect', 'ecomads-db', '--format', '{{.State.Health.Status}}') == 'healthy':
            break
        time.sleep(1)
    else:
        raise RuntimeError('Database did not become healthy')
    run('docker', 'exec', 'ecomads-db', 'sh', '-c', 'dropdb -U "$POSTGRES_USER" --force "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" -O "$POSTGRES_USER" "$POSTGRES_DB"')
    with (snapshot / 'database.dump').open('rb') as dump:
        run('docker', 'exec', '-i', 'ecomads-db', 'sh', '-c', 'exec pg_restore --exit-on-error --no-owner --no-privileges -U "$POSTGRES_USER" -d "$POSTGRES_DB"', stdin=dump)
    migrations = sql('SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId";').splitlines()
    if migrations != manifest['migrations']:
        raise RuntimeError('Migration history differs')
    differences = {}
    for table, expected in manifest['table_counts'].items():
        identifier = '"' + table.replace('"', '""') + '"'
        actual = sql(f'SELECT count(*) FROM public.{identifier};')
        if actual != expected:
            differences[table] = {'expected': expected, 'actual': actual}
    if args.final and differences:
        raise RuntimeError('Final row counts differ: ' + json.dumps(differences))
    result = {'final': args.final, 'tables': len(manifest['table_counts']), 'migrations': len(migrations),
              'row_count_differences': differences, 'web_started': False}
    (snapshot / 'restore-result.json').write_text(json.dumps(result, indent=2) + '\n')
    run('systemctl', 'daemon-reload')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
