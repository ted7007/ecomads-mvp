#!/usr/bin/env python3
"""Root-owned application secret editor, invoked by ecomadsctl."""
import datetime
import fcntl
import getpass
import grp
import os
import pathlib
import re
import subprocess
import sys
import tempfile


def validate(name, value):
    if not re.fullmatch(r'(?:TELEGRAM|OPENAI|WB|JWT|POSTGRES|CADDY)_[A-Z0-9_]+', name):
        raise ValueError('Only application variables TELEGRAM_, OPENAI_, WB_, JWT_, POSTGRES_, CADDY_ are allowed')
    if any(c in value for c in "'\r\n\x00"):
        raise ValueError('Single quotes, line breaks and NUL are not supported in secret values')


def updated(content, name, value):
    validate(name, value)
    lines = [line for line in content.splitlines() if not line.startswith(name + '=')]
    lines.append(name + "='" + value + "'")
    return '\n'.join(lines) + '\n'


def main():
    if os.geteuid() != 0 or len(sys.argv) != 2:
        raise ValueError('Use sudo ecomadsctl secret VARIABLE_NAME')
    name = sys.argv[1]
    validate(name, '')
    value = getpass.getpass('New value (hidden): ') if sys.stdin.isatty() else sys.stdin.read().removesuffix('\n')
    validate(name, value)
    root = pathlib.Path('/opt/ecomads')
    env = root / '.env'
    with open('/run/ecomads-secret.lock', 'w') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        content = env.read_text()
        new_content = updated(content, name, value)
        descriptor, temporary = tempfile.mkstemp(prefix='.env.operator.', dir=root)
        try:
            with os.fdopen(descriptor, 'w') as out:
                out.write(new_content)
                out.flush()
                os.fsync(out.fileno())
            for command in [
                ['bash', '-n', temporary],
                ['docker', 'compose', '--env-file', temporary, '-f', str(root / 'compose.production.yml'), '-p', 'ecomads', 'config', '--quiet'],
            ]:
                if subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode:
                    raise ValueError('Configuration validation failed; original .env was preserved')
            backup_dir = root / 'backups'
            backup_dir.mkdir(mode=0o700, exist_ok=True)
            backup = backup_dir / ('env-' + datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ') + '.backup')
            fd = os.open(backup, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(fd, 'w') as out:
                out.write(content)
            os.chown(temporary, 0, grp.getgrnam('ecomads-access').gr_gid)
            os.chmod(temporary, 0o640)
            os.replace(temporary, env)
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)
    print(name + ' saved. Use sudo ecomadsctl apply to recreate the web container with updated settings.')


if __name__ == '__main__':
    try:
        main()
    except ValueError as error:
        print(str(error), file=sys.stderr)
        sys.exit(2)
