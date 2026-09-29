param(
    [string]$SshHost = "my-vps",
    [switch]$Logs
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$gitRevision = (& git -C $repoRoot -c safe.directory=C:/Work/ecomads-mvp rev-parse --short HEAD 2>$null)
if (-not $gitRevision) { $gitRevision = "nogit" }
$dirty = & git -C $repoRoot -c safe.directory=C:/Work/ecomads-mvp status --porcelain 2>$null
$suffix = if ($dirty) { "-dirty" } else { "" }
$version = "$timestamp-$gitRevision$suffix"
$archive = Join-Path ([IO.Path]::GetTempPath()) "ecomads-$version.tar.gz"
$remoteArchive = "/tmp/ecomads-$version.tar.gz"
$remoteRelease = "/opt/ecomads/releases/$version"

try {
    & (Join-Path $PSScriptRoot "bootstrap-vps.ps1") -SshHost $SshHost

    Push-Location $repoRoot
    try {
        & tar.exe -czf $archive `
            --exclude=Ecomads.WebApplication/appsettings.json `
            --exclude=Ecomads.WebApplication/appsettings.Development.json `
            --exclude=Ecomads.WebApplication/bin `
            --exclude=Ecomads.WebApplication/obj `
            --exclude=Ecomads.WebApplication/ClientApp/node_modules `
            --exclude=Ecomads.WebApplication/ClientApp/dist `
            Ecomads.WebApplication .dockerignore
        if ($LASTEXITCODE -ne 0) { throw "Unable to create deployment archive." }
    }
    finally {
        Pop-Location
    }

    & scp $archive "${SshHost}:${remoteArchive}"
    if ($LASTEXITCODE -ne 0) { throw "Unable to upload deployment archive." }

    & ssh $SshHost "mkdir -p '$remoteRelease' && tar -xzf '$remoteArchive' -C '$remoteRelease' && rm -f '$remoteArchive' && bash /opt/ecomads/deploy-vps.sh '$version' '$remoteRelease'"
    if ($LASTEXITCODE -ne 0) { throw "Deployment failed." }

    if ($Logs) {
        & ssh $SshHost "docker compose --env-file /opt/ecomads/.env -f /opt/ecomads/compose.production.yml -p ecomads logs --tail=200 web caddy"
    }
}
finally {
    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }
}

Write-Host "Deployment completed: $version"
Write-Host "Direct URL: http://31.76.53.166"
