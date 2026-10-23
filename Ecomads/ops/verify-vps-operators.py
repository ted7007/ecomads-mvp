#!/usr/bin/env python3
"""Verify operator permissions and password authentication without changing passwords."""
import json
import os
from pathlib import Path
import pty
import select
import signal
import subprocess
import tempfile
import time


credentials = json.loads(Path('/root/ecomads-operator-credentials.json').read_text())
fd, known_path = tempfile.mkstemp(prefix='ecomads-ssh-check-', dir='/root')
key = Path('/etc/ssh/ssh_host_ed25519_key.pub').read_text().split()
os.write(fd, ('127.0.0.1 ' + key[0] + ' ' + key[1] + '\n').encode())
os.close(fd)
try:
    for user in ('ecomadsops1', 'ecomadsops2'):
        subprocess.run(['chage', '-d', time.strftime('%Y-%m-%d'), user], check=True)
        try:
            permission_result = subprocess.run(['runuser', '-u', user, '--', 'sh', '-c', '''
set -eu
id
test -r /opt/ecomads/.env
test ! -w /opt/ecomads/.env
test ! -w /opt/ecomads/compose.production.yml
test ! -w /opt/ecomads/deploy-vps.sh
test ! -w /opt/ecomads
test ! -w /var/run/docker.sock
probe=$(mktemp /opt/ecomads/releases/.permission-check.XXXXXX)
rm -- "$probe"
sudo -n ecomadsctl status >/dev/null
if sudo -n /usr/bin/id >/dev/null 2>&1; then exit 1; fi
if sudo -n docker ps >/dev/null 2>&1; then exit 1; fi
if sudo -n ecomadsctl restart db >/dev/null 2>&1; then exit 1; fi
'''], check=False, stdout=subprocess.DEVNULL)
        finally:
            subprocess.run(['chage', '-d', '0', user], check=True)
        permission_result.check_returncode()
        command = ['ssh', '-tt', '-o', 'StrictHostKeyChecking=yes', '-o', 'UserKnownHostsFile=' + known_path,
                   '-o', 'PreferredAuthentications=password', '-o', 'PubkeyAuthentication=no',
                   '-o', 'NumberOfPasswordPrompts=1', user + '@127.0.0.1', 'true']
        pid, terminal = pty.fork()
        if pid == 0:
            os.execvp(command[0], command)
        data = b''
        sent = False
        authenticated = False
        try:
            deadline = time.monotonic() + 20
            while time.monotonic() < deadline:
                readable, _, _ = select.select([terminal], [], [], 0.25)
                if readable:
                    try:
                        chunk = os.read(terminal, 8192)
                    except OSError:
                        break
                    if not chunk:
                        break
                    data += chunk
                    if b'password:' in data.lower() and not sent:
                        os.write(terminal, (credentials[user]['password'] + '\n').encode())
                        sent = True
                    if b'required to change your password' in data.lower():
                        authenticated = True
                        break
        finally:
            try:
                os.kill(pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
            os.waitpid(pid, 0)
            os.close(terminal)
        if not authenticated:
            raise RuntimeError(user + ': password authentication/expiry flow failed (output suppressed)')
        print(json.dumps({'user': user, 'permissions': 'ok', 'password_authentication': 'ok',
                          'first_login_password_change': 'required'}))
finally:
    os.unlink(known_path)
