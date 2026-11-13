# Zera o banco do Contix no docker compose LOCAL, mantendo só o usuário "Admin C2L Tech".
# DESTRUTIVO. Uso (PowerShell, na raiz do backend):
#   powershell -ExecutionPolicy Bypass -File scripts\db\zerar-banco.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\db\zerar-banco.ps1 -ComArquivos
#   ... -AdminNome "Outro Nome"   para manter outro usuário
param(
    [switch]$ComArquivos,
    [string]$AdminNome = "Admin C2L Tech"
)
Set-Location (Join-Path $PSScriptRoot "..\..")

# Lê só as variáveis necessárias do .env (sem executar o arquivo).
function Abortar([string]$msg) {
    Write-Host $msg -ForegroundColor Red
    exit 1
}

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
$pgUser = Ler-Env "POSTGRES_USER" "contix"
$pgDb = Ler-Env "POSTGRES_DB" "contix"

if ($ambiente -ne "Development") {
    Abortar "Abortado: ASPNETCORE_ENVIRONMENT=$ambiente no .env. Este script só roda em Development."
}

$servicos = docker compose ps --status running --services 2>$null
if (-not ($servicos -contains "db")) {
    Abortar "Abortado: o serviço 'db' do docker compose não está rodando (docker compose up -d db)."
}

Write-Host ""
Write-Host "  ATENÇÃO: operação DESTRUTIVA e sem volta." -ForegroundColor Red
Write-Host "  Banco: $pgDb (container 'db' do docker compose local)"
Write-Host "  Todos os dados serão apagados, exceto o usuário `"$AdminNome`" e o escritório dele."
if ($ComArquivos) { Write-Host "  Os arquivos do storage (documentos, anexos, logos, XMLs) também serão apagados." }
Write-Host ""
$resposta = Read-Host "Digite ZERAR para continuar"
if ($resposta -cne "ZERAR") {
    Write-Host "Cancelado. Nada foi alterado."
    exit 1
}

# Copia o SQL para o container em vez de usar pipe: o PowerShell 5.1 estraga acentos ao passar texto para programas externos.
docker compose cp scripts/db/zerar-banco.sql db:/tmp/zerar-banco.sql
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
docker compose exec -T db psql -U $pgUser -d $pgDb -v confirmar=ZERAR -v "admin_nome=$AdminNome" -f /tmp/zerar-banco.sql
$codigo = $LASTEXITCODE
docker compose exec -T db rm -f /tmp/zerar-banco.sql | Out-Null
if ($codigo -ne 0) {
    Write-Host "O banco NÃO foi alterado (a transação foi desfeita)." -ForegroundColor Yellow
    exit $codigo
}

if ($ComArquivos) {
    Write-Host "Apagando arquivos do storage..."
    docker compose run --rm --no-deps -T --entrypoint sh api -c "find /app/storage -mindepth 1 -delete"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "Storage limpo."
} else {
    Write-Host "Lembrete: os arquivos do storage ficaram órfãos. Rode com -ComArquivos para apagá-los."
}
