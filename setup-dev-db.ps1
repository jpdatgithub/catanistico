$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$apiProject = Join-Path $root "src\MeuCatan.Api\MeuCatan.Api.csproj"
$composeFile = Join-Path $root "docker-compose.yml"
$connectionString = "Host=localhost;Port=55432;Database=meucatan_dev;Username=meucatan_dev;Password=meucatan_dev"

if (-not (Test-Path $composeFile)) {
    Write-Host "Arquivo docker-compose.yml nao encontrado em $root"
    exit 1
}

Write-Host "Subindo PostgreSQL local..."
docker compose -f $composeFile up -d
if ($LASTEXITCODE -ne 0) {
    Write-Host "Falha ao subir PostgreSQL. Verifique se o Docker Desktop esta ativo."
    exit 1
}

Write-Host "Aplicando migration no banco..."
dotnet ef database update --project $apiProject --startup-project $apiProject --connection "$connectionString"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Falha ao aplicar migration."
    exit 1
}

Write-Host "Banco de desenvolvimento pronto."
