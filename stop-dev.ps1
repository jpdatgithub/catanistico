param(
    [switch]$StopDatabase
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$pidFile = Join-Path $root ".dev-processes.json"
$composeFile = Join-Path $root "docker-compose.yml"

if (-not (Test-Path $pidFile)) {
    Write-Host "Nenhum processo de desenvolvimento registrado."
    exit 0
}

$state = Get-Content -Path $pidFile -Raw | ConvertFrom-Json
$pids = @($state.ApiPid, $state.ClientPid) | Where-Object { $_ }

foreach ($processId in $pids) {
    try {
        Stop-Process -Id $processId -Force -ErrorAction Stop
        Write-Host "Processo finalizado: $processId"
    }
    catch {
        Write-Host "Processo ja nao estava ativo: $processId"
    }
}

Remove-Item -Path $pidFile -Force

if ($StopDatabase -and (Test-Path $composeFile)) {
    docker compose -f $composeFile down
}

Write-Host "Ambiente de desenvolvimento parado."
