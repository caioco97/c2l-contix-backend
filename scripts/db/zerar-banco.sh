#!/usr/bin/env bash
# Zera o banco do Contix no Postgres LOCAL do docker compose, mantendo só o usuário "Admin C2L Tech".
# DESTRUTIVO. Uso:
#   scripts/db/zerar-banco.sh                 # zera o banco (pede para digitar ZERAR)
#   scripts/db/zerar-banco.sh --com-arquivos  # também apaga os arquivos do storage
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

# Lê só a variável necessária do .env (sem executar o arquivo).
ler_env() {
  local valor=""
  [[ -f .env ]] && valor="$(grep -E "^$1=" .env | tail -n1 | cut -d= -f2- | tr -d '\r' | sed -e 's/^"//' -e 's/"$//')" || true
  echo "${valor:-$2}"
}
AMBIENTE="$(ler_env ASPNETCORE_ENVIRONMENT Development)"
ADMIN_NOME="${ADMIN_NOME:-Admin C2L Tech}"

if [[ "$AMBIENTE" != "Development" ]]; then
  echo "Abortado: ASPNETCORE_ENVIRONMENT=$AMBIENTE no .env. Este script só roda em Development." >&2
  exit 1
fi

# Acha o container 'db' do projeto pelos labels do compose (não depende do .env estar completo).
PROJETO="${COMPOSE_PROJECT_NAME:-contix}"
DB="$(docker ps -q --filter "label=com.docker.compose.project=$PROJETO" --filter "label=com.docker.compose.service=db" | head -n1)"
if [[ -z "$DB" ]]; then
  echo "Abortado: não achei o container 'db' do projeto '$PROJETO' rodando (docker compose up -d db)." >&2
  exit 1
fi
PG_USER="$(docker exec "$DB" printenv POSTGRES_USER || true)"; PG_USER="${PG_USER:-contix}"
PG_DB="$(docker exec "$DB" printenv POSTGRES_DB || true)"; PG_DB="${PG_DB:-$PG_USER}"

cat <<MSG

  ATENÇÃO: operação DESTRUTIVA e sem volta.
  Banco: $PG_DB no container $DB (docker compose local)
  Todos os dados serão apagados, exceto o usuário "$ADMIN_NOME" e o escritório dele.
MSG
$COM_ARQUIVOS && echo "  Os arquivos do storage (documentos, anexos, logos, XMLs) também serão apagados."
echo
read -r -p "Digite ZERAR para continuar: " resposta
if [[ "$resposta" != "ZERAR" ]]; then
  echo "Cancelado. Nada foi alterado."
  exit 1
fi

docker exec -i "$DB" psql -U "$PG_USER" -d "$PG_DB" \
  -v confirmar=ZERAR -v admin_nome="$ADMIN_NOME" -f - < scripts/db/zerar-banco.sql

if ! $COM_ARQUIVOS; then
  echo "Lembrete: os arquivos do storage ficaram órfãos. Rode com --com-arquivos para apagá-los."
  exit 0
fi

echo "Apagando arquivos do storage..."
# API rodando com 'dotnet run': pasta storage relativa ao diretório de onde a API foi iniciada.
for pasta in storage src/Contix.Api/storage; do
  if [[ -d "$pasta" ]]; then
    find "$pasta" -mindepth 1 -delete
    echo "  Limpo: $pasta"
  fi
done
# API rodando no docker compose: volume '<projeto>_storage' (usa a imagem do Postgres, que já está baixada).
VOLUME="${PROJETO}_storage"
if [[ -n "$(docker volume ls -q --filter "name=^${VOLUME}\$")" ]]; then
  IMAGEM="$(docker inspect -f '{{.Config.Image}}' "$DB")"
  docker run --rm -v "$VOLUME:/s" --entrypoint sh "$IMAGEM" -c 'find /s -mindepth 1 -delete'
  echo "  Limpo: volume $VOLUME"
fi
echo "Storage limpo."
