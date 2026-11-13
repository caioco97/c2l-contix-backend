#!/usr/bin/env bash
# Zera o banco do Contix no docker compose LOCAL, mantendo só o usuário "Admin C2L Tech".
# DESTRUTIVO. Uso:
#   scripts/db/zerar-banco.sh                 # zera o banco (pede para digitar ZERAR)
#   scripts/db/zerar-banco.sh --com-arquivos  # também apaga os arquivos do storage (volume do compose)
# Variáveis opcionais: ADMIN_NOME="Outro Nome" para manter outro usuário.
set -euo pipefail

cd "$(dirname "$0")/../.."

COM_ARQUIVOS=false
for arg in "$@"; do
  case "$arg" in
    --com-arquivos) COM_ARQUIVOS=true ;;
    -h|--help) sed -n '2,6p' "$0"; exit 0 ;;
    *) echo "Opção desconhecida: $arg" >&2; exit 2 ;;
  esac
done

# Lê só as variáveis necessárias do .env (sem executar o arquivo).
ler_env() {
  local valor=""
  [[ -f .env ]] && valor="$(grep -E "^$1=" .env | tail -n1 | cut -d= -f2- | tr -d '\r' | sed -e 's/^"//' -e 's/"$//')"
  echo "${valor:-$2}"
}
AMBIENTE="$(ler_env ASPNETCORE_ENVIRONMENT Development)"
PG_USER="$(ler_env POSTGRES_USER contix)"
PG_DB="$(ler_env POSTGRES_DB contix)"
ADMIN_NOME="${ADMIN_NOME:-Admin C2L Tech}"

if [[ "$AMBIENTE" != "Development" ]]; then
  echo "Abortado: ASPNETCORE_ENVIRONMENT=$AMBIENTE no .env. Este script só roda em Development." >&2
  exit 1
fi

if ! docker compose ps --status running --services 2>/dev/null | grep -qx db; then
  echo "Abortado: o serviço 'db' do docker compose não está rodando (docker compose up -d db)." >&2
  exit 1
fi

cat <<EOF

  ATENÇÃO: operação DESTRUTIVA e sem volta.
  Banco: $PG_DB (container 'db' do docker compose local)
  Todos os dados serão apagados, exceto o usuário "$ADMIN_NOME" e o escritório dele.
EOF
$COM_ARQUIVOS && echo "  Os arquivos do storage (documentos, anexos, logos, XMLs) também serão apagados."
echo
read -r -p "Digite ZERAR para continuar: " resposta
if [[ "$resposta" != "ZERAR" ]]; then
  echo "Cancelado. Nada foi alterado."
  exit 1
fi

docker compose exec -T db psql -U "$PG_USER" -d "$PG_DB" \
  -v confirmar=ZERAR -v admin_nome="$ADMIN_NOME" -f - < scripts/db/zerar-banco.sql

if $COM_ARQUIVOS; then
  echo "Apagando arquivos do storage..."
  docker compose run --rm --no-deps -T --entrypoint sh api -c 'find /app/storage -mindepth 1 -delete'
  echo "Storage limpo."
else
  echo "Lembrete: os arquivos do storage ficaram órfãos. Rode com --com-arquivos para apagá-los."
fi
