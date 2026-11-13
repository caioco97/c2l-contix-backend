# Inicializa/repara o banco do Contix no Postgres LOCAL do docker compose:
# reconstrói o histórico de migrations do EF a partir do schema existente. Não apaga dados.
# Uso (PowerShell, na raiz do backend):
#   powershell -ExecutionPolicy Bypass -File scripts\db\inicializar-banco.ps1
Set-Location (Join-Path $PSScriptRoot "..\..")

$projeto = if ($env:COMPOSE_PROJECT_NAME) { $env:COMPOSE_PROJECT_NAME } else { "contix" }
$db = docker ps -q --filter "label=com.docker.compose.project=$projeto" --filter "label=com.docker.compose.service=db" | Select-Object -First 1
if (-not $db) {
    Write-Host "Abortado: não achei o container 'db' do projeto '$projeto' rodando (docker compose up -d db)." -ForegroundColor Red
    exit 1
}
$pgUser = (docker exec $db printenv POSTGRES_USER | Out-String).Trim()
$pgDb = (docker exec $db printenv POSTGRES_DB | Out-String).Trim()
if (-not $pgUser) { $pgUser = "contix" }
if (-not $pgDb) { $pgDb = $pgUser }

docker cp scripts/db/inicializar-banco.sql "${db}:/tmp/inicializar-banco.sql"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
docker exec $db psql -U $pgUser -d $pgDb -f /tmp/inicializar-banco.sql
$codigo = $LASTEXITCODE
docker exec $db rm -f /tmp/inicializar-banco.sql | Out-Null
exit $codigo
