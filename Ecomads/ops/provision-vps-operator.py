import subprocess as sp, pathlib as P, secrets, string, os, pty, select, time, json
def run(args,**kw): return sp.run(args,check=True,text=True,**kw)
user='ecomadsops'
if sp.run(['id',user],stdout=sp.DEVNULL,stderr=sp.DEVNULL).returncode==0: raise SystemExit('Account exists; refusing overwrite')
helper=P.Path('/usr/local/sbin/ecomadsctl')
if helper.exists(): raise SystemExit('Helper exists; refusing overwrite')
secret_helper=P.Path('/usr/local/lib/ecomads-set-secret.py')
if secret_helper.exists(): raise SystemExit('Secret helper exists; refusing overwrite')
secret_source=P.Path(__file__).with_name('vps-set-secret.py').read_text()
run(['groupadd','--force','ecomads-access'])
run(['useradd','-m','-s','/bin/bash','-c','Ecomads application operator',user])
run(['usermod','-aG','ecomads-access',user])
password=''.join(secrets.choice(string.ascii_letters+string.digits) for _ in range(28))
run(['chpasswd'],input=user+':'+password+'\n')
secret_helper.write_text(secret_source)
os.chmod(secret_helper,0o755)
helper.write_text("""#!/usr/bin/env bash
set -Eeuo pipefail
ROOT=/opt/ecomads
[[ "$(id -u)" == 0 ]] || { echo "Use sudo ecomadsctl"; exit 1; }
access() {
  chgrp -R ecomads-access "$ROOT/releases"
  chmod -R g+rwX "$ROOT/releases"
  find "$ROOT/releases" -type d -exec chmod g+s {} +
  for file in .env .current-image .previous-image; do
    if [[ -f "$ROOT/$file" ]]; then
      chgrp ecomads-access "$ROOT/$file"
      chmod 0640 "$ROOT/$file"
    fi
  done
}
action="${1:-help}"
if (( $# )); then shift; fi
compose() { docker compose --env-file "$ROOT/.env" -f "$ROOT/compose.production.yml" -p ecomads "$@"; }
case "$action" in
  status) [[ $# == 0 ]] || exit 2; compose ps ;;
  logs) [[ $# == 0 ]] || exit 2; compose logs --tail=200 web caddy db ;;
  restart) [[ $# == 0 ]] || exit 2; compose restart web ;;
  apply) [[ $# == 0 ]] || exit 2; "$ROOT/archive-logs.sh" operator-apply; compose up -d --no-deps web ;;
  secret) [[ $# == 1 ]] || exit 2; /usr/bin/python3 /usr/local/lib/ecomads-set-secret.py "$1" ;;
  deploy)
    [[ $# == 1 && "$1" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$ ]] || exit 2
    [[ -d "$ROOT/releases/$1" && ! -L "$ROOT/releases/$1" ]] || exit 2
    trap access EXIT
    bash "$ROOT/deploy-vps.sh" "$1" "$ROOT/releases/$1"
    ;;
  rollback) [[ $# == 0 ]] || exit 2; trap access EXIT; bash "$ROOT/rollback-vps.sh" ;;
  access) [[ $# == 0 ]] || exit 2; access ;;
  env) [[ $# == 0 ]] || exit 2; cat "$ROOT/.env" ;;
  *) echo "Usage: sudo ecomadsctl {status|logs|restart|apply|secret NAME|deploy VERSION|rollback|access|env}"; [[ "$action" == help ]] ;;
esac
""")
os.chmod(helper,0o755)
sudoers=P.Path('/etc/sudoers.d/ecomadsops')
sudoers.write_text('ecomadsops ALL=(root) NOPASSWD: /usr/local/sbin/ecomadsctl\n')
os.chmod(sudoers,0o440)
run(['visudo','-cf',str(sudoers)])
run([str(helper),'access'])
sshconf=P.Path('/etc/ssh/sshd_config.d/90-ecomadsops.conf')
sshconf.write_text('Match User ecomadsops\n    PasswordAuthentication yes\n    PubkeyAuthentication yes\n    PermitEmptyPasswords no\n    MaxAuthTries 3\nMatch all\n')
try: run(['/usr/sbin/sshd','-t'])
except:
 sshconf.unlink()
 raise
run(['systemctl','reload','ssh'])
home=P.Path('/home')/user
(home/'project').symlink_to('/opt/ecomads',target_is_directory=True)
with (home/'.bashrc').open('a') as f: f.write('\n# Start interactive sessions in the application directory.\nif [[ $- == *i* ]]; then cd /opt/ecomads; fi\n')
run(['chown','-h',user+':'+user,str(home/'project')])
known=P.Path('/run/ecomadsops-verify-known-hosts')
key=P.Path('/etc/ssh/ssh_host_ed25519_key.pub').read_text().split()
known.write_text('127.0.0.1 '+key[0]+' '+key[1]+'\n')
cmd=['ssh','-o','StrictHostKeyChecking=yes','-o','UserKnownHostsFile='+str(known),'-o','PreferredAuthentications=password','-o','PubkeyAuthentication=no','-o','NumberOfPasswordPrompts=1',user+'@127.0.0.1','set -e; id; test -r /opt/ecomads/.env; test ! -w /opt/ecomads/.env; test ! -w /opt/ecomads/deploy-vps.sh; test ! -w /opt/ecomads; test ! -w /var/run/docker.sock; probe=$(mktemp /opt/ecomads/releases/.access-check.XXXXXX); rm -- "$probe"; echo PROJECT_ACCESS_OK; sudo -n ecomadsctl status; if sudo -n /usr/bin/id 2>/dev/null; then exit 1; else echo SUDO_SCOPED_OK; fi; if sudo -n ecomadsctl restart db; then exit 1; else echo ARGUMENT_RESTRICTION_OK; fi']
pid,fd=pty.fork()
if pid==0: os.execvp(cmd[0],cmd)
output=b''; sent=False; status=None; deadline=time.time()+30
try:
 while time.time()<deadline:
  ready,_,_=select.select([fd],[],[],0.25)
  if ready:
   try: chunk=os.read(fd,8192)
   except OSError: break
   if not chunk: break
   output+=chunk
   if b'password:' in output.lower() and not sent:
    os.write(fd,(password+'\n').encode()); sent=True
  done,code=os.waitpid(pid,os.WNOHANG)
  if done: status=code; break
 if status is None:
  done,code=os.waitpid(pid,os.WNOHANG)
  if not done:
   os.kill(pid,15); _,code=os.waitpid(pid,0)
  status=code
finally:
 os.close(fd); known.unlink(missing_ok=True)
result=output.decode(errors='replace')
if not sent or os.waitstatus_to_exitcode(status)!=0 or 'ARGUMENT_RESTRICTION_OK' not in result:
 print(result); raise SystemExit('Login verification failed')
run(['chage','-d','0',user])
health=run(['curl','--fail','--silent','--show-error','--max-time','10','--resolve','ecomads.ru:443:127.0.0.1','https://ecomads.ru/health'],capture_output=True)
print(json.dumps({'user':user,'password':password,'verification':result,'health':health.stdout,'must_change_password':True}))
