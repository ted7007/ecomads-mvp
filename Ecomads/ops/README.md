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

Before DNS is configured, open `http://31.76.53.166`. After the `ecomads.ru`
and `www.ecomads.ru` A records point to that IP, Caddy obtains certificates and
the application becomes available over HTTPS automatically.

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

Rollback switches to the previously healthy image. Deployments create a custom
format PostgreSQL dump in `/opt/ecomads/backups` before replacing an existing
application version. The deployment keeps the five newest source releases and
the ten newest database backups.

## Operations

```powershell
ssh my-vps "docker compose --env-file /opt/ecomads/.env -f /opt/ecomads/compose.production.yml -p ecomads ps"
ssh my-vps "docker compose --env-file /opt/ecomads/.env -f /opt/ecomads/compose.production.yml -p ecomads logs --tail=200 web caddy db"
```

For Telegram, set `TELEGRAM_BOT_TOKEN` and `TELEGRAM_BOT_USERNAME` in the
protected `/opt/ecomads/.env`. Without them, WB pages work and bot polling stays
idle; chat linking and message delivery remain unavailable.
