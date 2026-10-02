param(
    [string]$SshHost = "my-vps"
)

$ErrorActionPreference = "Stop"
$stage = "/tmp/ecomads-bootstrap-$([Guid]::NewGuid().ToString('N'))"
$files = @(
    "compose.production.yml",
    "Caddyfile",
    "bootstrap-vps.sh",
    "deploy-vps.sh",
    "rollback-vps.sh",
    "archive-logs.sh"
)

try {
    & ssh $SshHost "mkdir -p '$stage'"
    if ($LASTEXITCODE -ne 0) { throw "Unable to create bootstrap staging directory." }

    foreach ($file in $files) {
        & scp (Join-Path $PSScriptRoot $file) "${SshHost}:${stage}/${file}"
        if ($LASTEXITCODE -ne 0) { throw "Unable to upload $file." }
    }

    & ssh $SshHost "bash '$stage/bootstrap-vps.sh' '$stage'"
    if ($LASTEXITCODE -ne 0) { throw "VPS bootstrap failed." }
}
finally {
    & ssh $SshHost "rm -rf '$stage'" 2>$null
}
