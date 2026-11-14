# Ecomads production deployment

The production stack runs Caddy, the ASP.NET/React application, and PostgreSQL.

## Current target (verified 2026-11-10)

Production runs at `48.102.213.83`. The former server at `152.234.253.9`
temporarily proxies HTTPS to the new server for clients with cached DNS.
Use the verified SSH target:

```powershell
Resolve-DnsName ecomads.ru -Type A -Server 1.1.1.1
ssh -o Hostname=48.102.213.83 ecomads-new "hostname; cat /opt/ecomads/.current-image"
```

Preserve SSH host-key verification. Recheck DNS and the running release before
each deployment. Confirm that the local `ecomads-new` alias points to the
verified target before running the PowerShell helpers. Use `/opt/ecomads/deploy-vps.sh` on the
verified server. For a narrow change, copy the current production release and
apply only the requested changes, avoiding unrelated local work.

The final migration snapshot is stored under
`/opt/ecomads-migration/final-20261110` on both servers. Keep the former server
and its data for at least seven days. A rollback after the new web container has
started requires transferring new database writes and mutable files back before
returning traffic to the former server.

Update this section and the root `AGENTS.md` together after the next move.
The following migration record describes the earlier infrastructure:
See [the migration record](MIGRATION-2026-10-24.md) for checkpoints and rollback.
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
the three newest database backups. The one-time Excel-era dump is excluded
from rotation and is stored at
`/opt/ecomads/backups/ecomads-old-schema-20260929T231538Z.dump`.

### Copy the pre-deploy dump to the operator's PC

For every deployment, copy its completed PostgreSQL dump to
`%LOCALAPPDATA%\Ecomads\Backups`, including when the deployment fails after the
dump was successfully created. Use the exact `ecomads-…-before-<version>.dump`
filename from the deployment output and confirm it matches the release you
attempted. Keep these production data files outside the repository and shared
or synced folders. Use the IP verified before the deployment:

```powershell
$targetIp = '48.102.213.83' # Replace if the verified production IP has changed.
$dumpName = '<exact dump filename from deployment output>'
if ($dumpName -cnotmatch '^ecomads-[0-9]{8}T[0-9]{6}Z-before-[A-Za-z0-9._-]+\.dump$') {
    throw 'Provide the exact pre-deploy dump filename.'
}
$remoteDump = "/opt/ecomads/backups/$dumpName"
$backupDir = Join-Path $env:LOCALAPPDATA 'Ecomads\Backups'
$localDump = Join-Path $backupDir $dumpName
$partialDump = "$localDump.partial"
New-Item -ItemType Directory -Path $backupDir -Force -ErrorAction Stop | Out-Null

$checksumLine = & ssh -o "Hostname=$targetIp" ecomads-new "sha256sum -- '$remoteDump'"
if ($LASTEXITCODE -ne 0 -or $checksumLine -notmatch '^([0-9a-fA-F]{64})\s') {
    throw 'Could not read the server checksum.'
}
$expectedHash = $Matches[1]

& scp -o "Hostname=$targetIp" "ecomads-new:${remoteDump}" $partialDump
if ($LASTEXITCODE -ne 0) { throw 'Could not copy the database dump.' }
if ((Get-FileHash -LiteralPath $partialDump -Algorithm SHA256 -ErrorAction Stop).Hash -ne $expectedHash) {
    throw 'The local dump does not match the server checksum.'
}
Move-Item -LiteralPath $partialDump -Destination $localDump -Force -ErrorAction Stop
```

A `.partial` file is not a verified backup. Report any failed copy or checksum
check separately from the deployment result. A checksum verifies the transfer;
it does not test database restoration.

### Image cleanup and log archives

After a successful deployment, dangling (untagged and unused) Docker images
are pruned to remove intermediate build layers. Tagged images, including the
rollback version, and all data volumes are preserved. A cleanup error is
reported as a warning without failing a healthy deployment.

Container logs are archived on the VPS under `/opt/ecomads/logs` before each
deployment or rollback and daily by `ecomads-log-archive.timer`. Daily archives
cover the previous 48 hours and overlap. Archives are compressed, readable only
by root, and retained for up to 14 days within a 1 GB total cap. Docker rotates each live container log at
20 MB across five files. These archives survive container replacement, but they
remain on the same VPS and are not an offsite backup.

```powershell
ssh ecomads-new "sudo systemctl status ecomads-log-archive.timer --no-pager"
ssh ecomads-new "sudo ls -lh /opt/ecomads/logs"
ssh ecomads-new "sudo gzip -cd /opt/ecomads/logs/NAME.log.gz | tail -100"
```

## Operations

```powershell
ssh ecomads-new "docker compose --env-file /opt/ecomads/.env -f /opt/ecomads/compose.production.yml -p ecomads ps"
ssh ecomads-new "docker compose --env-file /opt/ecomads/.env -f /opt/ecomads/compose.production.yml -p ecomads logs --tail=200 web caddy db"
```

For Telegram, set `TELEGRAM_BOT_TOKEN` and `TELEGRAM_BOT_USERNAME` in the
protected `/opt/ecomads/.env`. Without them, WB pages work and bot polling stays
idle; chat linking and message delivery remain unavailable.

## WB automatic refresh

Automatic collection runs at 06:15 and 18:15 Moscow time, with a daily recheck of the latest
30 completed days and automatic year backfill. The new
`AddWbSparseObservations` migration records sparse advertising responses separately
from confirmed zeroes and is applied during application startup.

Set `WB_AUTO_REFRESH_ENABLED=false` in the protected server `.env` to disable
new scheduled runs. For a staged rollout, set `WB_AUTO_REFRESH_STORE_IDS` to a
comma-separated list of store UUIDs; an empty value permits all connected
stores with their own auto-refresh switch enabled. Changing these environment
values requires recreating the web container. In-flight and already queued
jobs are not canceled by the schedule switch; year jobs have a separate pause.

See [the collection guide](../docs/wb-automatic-refresh.md) for scheduling,
method limits, durability, report readiness, and monitoring.
