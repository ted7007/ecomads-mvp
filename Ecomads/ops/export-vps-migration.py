#!/usr/bin/env python3
"""Export a protected Ecomads migration snapshot without touching VPN.

Run on the source VPS as root. --final refuses a running web container.
Database files are never copied; export uses pg_dump from PostgreSQL 15.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tarfile


def command(*args):
    return subprocess.check_output(args, text=True).strip()


def sql(query):
    return subprocess.check_output([
        'docker', 'exec', '-i', 'ecomads-db', 'sh', '-c',
        'exec psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At',
    ], input=query, text=True).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('destination', type=Path)
    parser.add_argument('--final', action='store_true')
    args = parser.parse_args()
    if os.geteuid() != 0:
        parser.error('Run as root')
    destination = args.destination.resolve()
    if not destination.is_relative_to(Path('/opt/ecomads-migration')):
        parser.error('Destination must be beneath /opt/ecomads-migration')
    if destination.exists():
        parser.error('Destination already exists; choose a new snapshot name')
    web_running = command('docker', 'inspect', 'ecomads-web', '--format', '{{.State.Running}}')
    if args.final and web_running != 'false':
        parser.error('Stop source web before final export')
    os.umask(0o077)
    destination.mkdir(parents=True, mode=0o700)
    root = Path('/opt/ecomads')
    manifest = {'final': args.final, 'images': {}, 'missing_source_releases': []}
    for name in ('web', 'db', 'caddy'):
        info = json.loads(command('docker', 'inspect', f'ecomads-{name}'))[0]
        manifest['images'][name] = {'tag': info['Config']['Image'], 'id': info['Image']}
    for pointer in ('.current-image', '.previous-image'):
        if (root / pointer).exists():
            image = (root / pointer).read_text().strip()
            manifest[pointer] = image
            if not (root / 'releases' / image.split(':', 1)[1]).is_dir():
                manifest['missing_source_releases'].append(image)
    with (destination / 'database.dump').open('wb') as dump:
        subprocess.run([
            'docker', 'exec', 'ecomads-db', 'sh', '-c',
            'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc',
        ], stdout=dump, check=True)
    tables = json.loads(sql("SELECT coalesce(json_agg(tablename ORDER BY tablename), '[]') FROM pg_tables WHERE schemaname='public';"))
    statements = []
    for table in tables:
        identifier = '"' + table.replace('"', '""') + '"'
        literal = "'" + table.replace("'", "''") + "'"
        statements.append(f'SELECT {literal}, count(*) FROM public.{identifier}')
    manifest['table_counts'] = dict(line.rsplit('|', 1) for line in sql(';\n'.join(statements) + ';').splitlines())
    manifest['migrations'] = sql('SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId";').splitlines()
    with tarfile.open(destination / 'application.tar.gz', 'w:gz', compresslevel=1) as archive:
        archive.add(root, arcname='ecomads')
        for name in ('dataprotection_keys', 'wb_response_journal', 'caddy_data', 'caddy_config'):
            mount = command('docker', 'volume', 'inspect', f'ecomads_{name}', '--format', '{{.Mountpoint}}')
            archive.add(mount, arcname=f'volumes/{name}')
        for filename in (
            '/usr/local/sbin/ecomadsctl', '/usr/local/lib/ecomads-set-secret.py',
            '/etc/systemd/system/ecomads-log-archive.service',
            '/etc/systemd/system/ecomads-log-archive.timer',
        ):
            archive.add(filename, arcname='system/' + filename.lstrip('/'))
    if args.final and command('docker', 'inspect', 'ecomads-web', '--format', '{{.State.Running}}') != 'false':
        raise RuntimeError('Source restarted during final export; snapshot must not be used')
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    checksums = []
    for name in ('database.dump', 'application.tar.gz', 'manifest.json'):
        hasher = hashlib.sha256()
        with (destination / name).open('rb') as file:
            for chunk in iter(lambda: file.read(1024 * 1024), b''):
                hasher.update(chunk)
        checksums.append(f'{hasher.hexdigest()}  {name}\n')
    (destination / 'SHA256SUMS').write_text(''.join(checksums))
    print(json.dumps({'snapshot': str(destination), 'final': args.final,
                      'missing_source_releases': manifest['missing_source_releases'],
                      'files': {file.name: file.stat().st_size for file in destination.iterdir()}}))


if __name__ == '__main__':
    main()
