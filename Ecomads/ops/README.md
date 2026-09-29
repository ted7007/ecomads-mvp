# Ecomads production deployment

The production stack runs Caddy, the ASP.NET/React application, and PostgreSQL.
PostgreSQL is available only inside the private Docker network. Secrets stay in
`/opt/ecomads/.env` on the VPS and are not uploaded by deployments.

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

To enable or replace the LLM API key, use the hidden-input helper:

```powershell
.\ops\set-openai-key.ps1
```

The key is sent over SSH through standard input, stored only in the protected
`/opt/ecomads/.env`, and never committed or placed in command-line arguments.
