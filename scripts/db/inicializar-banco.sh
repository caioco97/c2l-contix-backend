#!/usr/bin/env bash
# Inicializa/repara o banco do Contix no Postgres LOCAL do docker compose:
# reconstrói o histórico de migrations do EF a partir do schema existente. Não apaga dados.
# Uso: scripts/db/inicializar-banco.sh
set -euo pipefail
cd "$(dirname "$0")/../.."

PROJETO="${COMPOSE_PROJECT_NAME:-contix}"
DB="$(docker ps -q --filter "label=com.docker.compose.project=$PROJETO" --filter "label=com.docker.compose.service=db" | head -n1)"
if [[ -z "$DB" ]]; then
  echo "Abortado: não achei o container 'db' do projeto '$PROJETO' rodando (docker compose up -d db)." >&2
  exit 1
fi
PG_USER="$(docker exec "$DB" printenv POSTGRES_USER || true)"; PG_USER="${PG_USER:-contix}"
PG_DB="$(docker exec "$DB" printenv POSTGRES_DB || true)"; PG_DB="${PG_DB:-$PG_USER}"

docker exec -i "$DB" psql -U "$PG_USER" -d "$PG_DB" -f - < scripts/db/inicializar-banco.sql
