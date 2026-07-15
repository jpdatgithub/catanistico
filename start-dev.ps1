param(
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$apiProject = Join-Path $root "src\MeuCatan.Api\MeuCatan.Api.csproj"
$clientProject = Join-Path $root "src\MeuCatan.MudblazorWasmClient\MeuCatan.MudblazorWasmClient.csproj"
$composeFile = Join-Path $root "docker-compose.yml"
$pidFile = Join-Path $root ".dev-processes.json"

if (Test-Path $pidFile) {
    Write-Host "Ja existe um arquivo de processos em execucao: $pidFile"
    Write-Host "Se necessario, rode ./stop-dev.ps1 antes de iniciar novamente."
    exit 1
}

if (Test-Path $composeFile) {
    Write-Host "Subindo banco local com Docker Compose..."
    docker compose -f $composeFile up -d
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Falha ao subir PostgreSQL local. Inicie o Docker Desktop e tente novamente."
        exit 1
    }
}

$apiProc = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", $apiProject, "--launch-profile", "http") -PassThru
$clientProc = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", $clientProject, "--launch-profile", "http") -PassThru

$state = [ordered]@{
    ApiPid    = $apiProc.Id
    ClientPid = $clientProc.Id
    ApiUrl    = "http://localhost:5053"
    ClientUrl = "http://localhost:5274"
}

$state | ConvertTo-Json | Set-Content -Path $pidFile -Encoding UTF8

if (-not $NoBrowser) {
    Start-Process "http://localhost:5274"
}

Write-Host "API iniciada em http://localhost:5053 (PID: $($apiProc.Id))"
Write-Host "Client iniciado em http://localhost:5274 (PID: $($clientProc.Id))"
Write-Host "Para parar os dois processos, execute: ./stop-dev.ps1"
