#!/usr/bin/env python3
"""Provision named operators; repeat runs preserve accounts and passwords.

Run as root beside ecomadsctl and vps-set-secret.py. New credentials are written
only to /root/ecomads-operator-credentials.json, never standard output.
"""
import argparse
import json
import os
import pathlib
import re
import secrets
import shutil
import subprocess


def run(*args, **kwargs):
    return subprocess.run(args, check=True, text=True, **kwargs)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('users', nargs='+')
    args = parser.parse_args()
    if os.geteuid() != 0:
        parser.error('Run as root')
    if any(not re.fullmatch(r'ecomadsops[0-9]*', user) for user in args.users):
        parser.error('Expected ecomadsops or ecomadsops followed by digits')
    source = pathlib.Path(__file__).resolve().parent
    for filename in ('ecomadsctl', 'vps-set-secret.py'):
        if not (source / filename).is_file():
            parser.error(f'Missing {filename}')
    credentials_path = pathlib.Path('/root/ecomads-operator-credentials.json')
    credentials = json.loads(credentials_path.read_text()) if credentials_path.exists() else {}
    run('groupadd', '--force', 'ecomads-access')
    for user in dict.fromkeys(args.users):
        exists = subprocess.run(['id', user], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0
        if not exists:
            run('useradd', '-m', '-s', '/bin/bash', '-c', 'Ecomads application operator', user)
            password = secrets.token_urlsafe(24)
            credentials[user] = {'password': password, 'must_change_password': True}
            fd = os.open(credentials_path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
            with os.fdopen(fd, 'w') as file:
                json.dump(credentials, file, indent=2)
            os.chmod(credentials_path, 0o600)
            run('chpasswd', input=f'{user}:{password}\n')
            run('chage', '-d', '0', user)
        run('usermod', '-aG', 'ecomads-access', user)
        home = pathlib.Path('/home') / user
        project = home / 'project'
        if not project.exists() and not project.is_symlink():
            project.symlink_to('/opt/ecomads', target_is_directory=True)
            run('chown', '-h', f'{user}:{user}', str(project))
        bashrc = home / '.bashrc'
        line = 'if [[ $- == *i* ]]; then cd /opt/ecomads; fi'
        if line not in bashrc.read_text():
            with bashrc.open('a') as file:
                file.write('\n' + line + '\n')
        sudoers = pathlib.Path('/etc/sudoers.d') / user
        sudoers.write_text(f'{user} ALL=(root) NOPASSWD: /usr/local/sbin/ecomadsctl\n')
        sudoers.chmod(0o440)
        run('visudo', '-cf', str(sudoers))
    for filename, destination in (
        ('ecomadsctl', '/usr/local/sbin/ecomadsctl'),
        ('vps-set-secret.py', '/usr/local/lib/ecomads-set-secret.py'),
    ):
        shutil.copyfile(source / filename, destination)
        os.chown(destination, 0, 0)
        os.chmod(destination, 0o755)
    config = pathlib.Path('/etc/ssh/sshd_config.d/00-ecomads-operators.conf')
    previous = config.read_text() if config.exists() else None
    config.write_text('Match Group ecomads-access\n    PasswordAuthentication yes\n    PubkeyAuthentication yes\n    PermitEmptyPasswords no\n    MaxAuthTries 3\nMatch all\n')
    try:
        run('/usr/sbin/sshd', '-t')
    except subprocess.CalledProcessError:
        if previous is None:
            config.unlink()
        else:
            config.write_text(previous)
        raise
    run('systemctl', 'reload', 'ssh')
    run('/usr/local/sbin/ecomadsctl', 'access')
    print('Operators provisioned: ' + ', '.join(args.users))
    print('New credentials: /root/ecomads-operator-credentials.json (root only)')


if __name__ == '__main__':
    main()
