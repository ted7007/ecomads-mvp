param(
    [string]$SshHost = "my-vps"
)

$ErrorActionPreference = "Stop"
& ssh $SshHost "bash /opt/ecomads/rollback-vps.sh"
if ($LASTEXITCODE -ne 0) { throw "Rollback failed." }
