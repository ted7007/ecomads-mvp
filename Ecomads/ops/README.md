# Ecomads production deployment

The production stack runs Caddy, the ASP.NET/React application, and PostgreSQL.
PostgreSQL is available only inside the private Docker network. Secrets stay in
`/opt/ecomads/.env` on the VPS and are not uploaded by deployments.

**WB schema cutover:** this release needs a separate empty PostgreSQL database.
Do not run the standard deployment over the existing Excel-era database. The
deployment script rejects an existing non-WB schema; use an isolated acceptance
stack first and choose the production database deliberately after validation.

## First deployment

From PowerShell in the repository root:

```powershell
.\ops\bootstrap-vps.ps1
.\ops\deploy.ps1
```

Open `https://ecomads.ru`. The direct IP address redirects to the HTTPS domain.
On 2026-09-30, the domain resolved to `31.76.53.166` and HTTPS passed certificate
verification. Deployment and rollback health checks use HTTPS locally.

## Later deployments

```powershell
.\ops\deploy.ps1
```

Show application and proxy logs after deployment:

```powershell
.\ops\deploy.ps1 -Logs
```

## Rollback

```powershell
.\ops\rollback.ps1
```

Rollback switches to the previously healthy image when both images use the WB
schema. The Excel-era image cannot run against the new database; its rollback
pointer was removed during the 2026-09-30 cutover. Deployments create a custom
format PostgreSQL dump in `/opt/ecomads/backups` before replacing an existing
application version. The deployment keeps the five newest source releases and
the ten newest database backups. The one-time Excel-era dump is
`/opt/ecomads/backups/ecomads-old-schema-20260929T231538Z.dump`.

Container logs are archived on the VPS under `/opt/ecomads/logs` before each
deployment or rollback and daily by `ecomads-log-archive.timer`. Daily archives
cover the previous 48 hours and overlap. Archives are compressed, readable only
by root, and retained for up to 14 days within a 1 GB total cap. Docker rotates each live container log at
20 MB across five files. These archives survive container replacement, but they
remain on the same VPS and are not an offsite backup.

```powershell
ssh my-vps "sudo systemctl status ecomads-log-archive.timer --no-pager"
ssh my-vps "sudo ls -lh /opt/ecomads/logs"
ssh my-vps "sudo gzip -cd /opt/ecomads/logs/NAME.log.gz | tail -100"
```

## Operations

```powershell
ssh my-vps "docker compose --env-file /opt/ecomads/.env -f /opt/ecomads/compose.production.yml -p ecomads ps"
ssh my-vps "docker compose --env-file /opt/ecomads/.env -f /opt/ecomads/compose.production.yml -p ecomads logs --tail=200 web caddy db"
```

For Telegram, set `TELEGRAM_BOT_TOKEN` and `TELEGRAM_BOT_USERNAME` in the
protected `/opt/ecomads/.env`. Without them, WB pages work and bot polling stays
idle; chat linking and message delivery remain unavailable.
