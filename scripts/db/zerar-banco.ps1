# Zera o banco do Contix no Postgres LOCAL do docker compose, mantendo só o usuário "Admin C2L Tech".
# DESTRUTIVO. Uso (PowerShell, na raiz do backend):
#   powershell -ExecutionPolicy Bypass -File scripts\db\zerar-banco.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\db\zerar-banco.ps1 -ComArquivos
#   ... -AdminNome "Outro Nome"   para manter outro usuário
param(
    [switch]$ComArquivos,
    [string]$AdminNome = "Admin C2L Tech"
)
Set-Location (Join-Path $PSScriptRoot "..\..")

function Abortar([string]$msg) {
    Write-Host $msg -ForegroundColor Red
    exit 1
}

# Lê só a variável necessária do .env (sem executar o arquivo).
function Ler-Env([string]$nome, [string]$padrao) {
    if (Test-Path .env) {
        $linha = Get-Content .env | Where-Object { $_ -match "^$nome=" } | Select-Object -Last 1
        if ($linha) {
            $valor = ($linha -replace "^$nome=", "").Trim().Trim('"')
            if ($valor) { return $valor }
        }
    }
    return $padrao
}

$ambiente = Ler-Env "ASPNETCORE_ENVIRONMENT" "Development"
if ($ambiente -ne "Development") {
    Abortar "Abortado: ASPNETCORE_ENVIRONMENT=$ambiente no .env. Este script só roda em Development."
}

# Acha o container 'db' do projeto 'contix' pelos labels do compose (não depende do .env estar completo).
$projeto = if ($env:COMPOSE_PROJECT_NAME) { $env:COMPOSE_PROJECT_NAME } else { "contix" }
$db = docker ps -q --filter "label=com.docker.compose.project=$projeto" --filter "label=com.docker.compose.service=db" | Select-Object -First 1
if (-not $db) {
    Abortar "Abortado: não achei o container 'db' do projeto '$projeto' rodando (docker compose up -d db)."
}
$pgUser = (docker exec $db printenv POSTGRES_USER | Out-String).Trim()
$pgDb = (docker exec $db printenv POSTGRES_DB | Out-String).Trim()
if (-not $pgUser) { $pgUser = "contix" }
if (-not $pgDb) { $pgDb = $pgUser }

Write-Host ""
Write-Host "  ATENÇÃO: operação DESTRUTIVA e sem volta." -ForegroundColor Red
Write-Host "  Banco: $pgDb no container $db (docker compose local)"
Write-Host "  Todos os dados serão apagados, exceto o usuário `"$AdminNome`" e o escritório dele."
if ($ComArquivos) { Write-Host "  Os arquivos do storage (documentos, anexos, logos, XMLs) também serão apagados." }
Write-Host ""
$resposta = Read-Host "Digite ZERAR para continuar"
if ($resposta -cne "ZERAR") {
    Write-Host "Cancelado. Nada foi alterado."
    exit 1
}

# Copia o SQL para o container em vez de usar pipe: o PowerShell 5.1 estraga acentos ao passar texto para programas externos.
docker cp scripts/db/zerar-banco.sql "${db}:/tmp/zerar-banco.sql"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
docker exec $db psql -U $pgUser -d $pgDb -v confirmar=ZERAR -v "admin_nome=$AdminNome" -f /tmp/zerar-banco.sql
$codigo = $LASTEXITCODE
docker exec $db rm -f /tmp/zerar-banco.sql | Out-Null
if ($codigo -ne 0) {
    Write-Host "O banco NÃO foi alterado (a transação foi desfeita)." -ForegroundColor Yellow
    exit $codigo
}

if (-not $ComArquivos) {
    Write-Host "Lembrete: os arquivos do storage ficaram órfãos. Rode com -ComArquivos para apagá-los."
    exit 0
}

Write-Host "Apagando arquivos do storage..."
# API rodando com 'dotnet run': pasta storage relativa ao diretório de onde a API foi iniciada.
foreach ($pasta in @("storage", "src\Contix.Api\storage")) {
    if (Test-Path $pasta) {
        Get-ChildItem $pasta -Force | Remove-Item -Recurse -Force
        Write-Host "  Limpo: $pasta"
    }
}
# API rodando no docker compose: volume '<projeto>_storage' (usa a imagem do Postgres, que já está baixada).
$volume = "${projeto}_storage"
if (docker volume ls -q --filter "name=^${volume}$") {
    $imagem = (docker inspect -f "{{.Config.Image}}" $db | Out-String).Trim()
    docker run --rm -v "${volume}:/s" --entrypoint sh $imagem -c "find /s -mindepth 1 -delete"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "  Limpo: volume $volume"
}
Write-Host "Storage limpo."
