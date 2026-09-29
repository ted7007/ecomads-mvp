param(
    [string]$SshHost = "my-vps"
)

$ErrorActionPreference = "Stop"
$secureKey = Read-Host "Вставьте новый Bothub/OpenAI API-ключ" -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)

try {
    $plainKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    if ([string]::IsNullOrWhiteSpace($plainKey)) {
        throw "Ключ не может быть пустым."
    }

    $plainKey | & ssh $SshHost "python3 /opt/ecomads/set-openai-key-vps.py && docker compose --env-file /opt/ecomads/.env -f /opt/ecomads/compose.production.yml -p ecomads up -d --no-deps --force-recreate web"
    if ($LASTEXITCODE -ne 0) { throw "Не удалось обновить API-ключ." }

    & ssh $SshHost "for i in `$(seq 1 30); do if curl -fsS -H 'Host: 31.76.53.166' http://127.0.0.1/health >/dev/null; then exit 0; fi; sleep 2; done; exit 1"
    if ($LASTEXITCODE -ne 0) { throw "Контейнер обновлён, но health check не прошёл." }

    Write-Host "API-ключ обновлён, приложение работает."
}
finally {
    if ($pointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
    $plainKey = $null
}
