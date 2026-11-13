-- =============================================================================
-- ZERAR O BANCO DO CONTIX (SOMENTE DESENVOLVIMENTO)
--
-- ATENÇÃO: operação DESTRUTIVA e sem volta. Apaga TODOS os dados de todas as
-- tabelas, mantendo apenas:
--   * o usuário "Admin C2L Tech" (ou o nome passado em -v admin_nome=...);
--   * o escritório (tenant) desse usuário, necessário para ele fazer login;
--   * a tabela de histórico de migrations do EF ("__ef_migrations_history"),
--     para o schema continuar migrado.
--
-- Proteções:
--   * exige -v confirmar=ZERAR;
--   * só aceita conexão local (socket ou 127.0.0.1/::1), ou seja, via
--     `docker compose exec db psql ...`; bancos remotos são recusados;
--   * exige que exista exatamente UM usuário ativo com o nome informado;
--   * roda numa única transação: qualquer erro desfaz tudo.
--
-- Uso recomendado: scripts/db/zerar-banco.sh (faz as checagens e pede
-- confirmação). Uso direto:
--   docker compose exec -T db psql -U contix -d contix -v confirmar=ZERAR \
--     -f - < scripts/db/zerar-banco.sql
--
-- Os arquivos guardados no storage (documentos, anexos, certidões, XMLs de
-- NF-e, logos das empresas) NÃO são apagados por este script e ficam órfãos.
-- Veja scripts/db/README.md para limpá-los.
-- =============================================================================

\set ON_ERROR_STOP on
\set QUIET on

\if :{?admin_nome}
\else
  \set admin_nome 'Admin C2L Tech'
\endif

\if :{?confirmar}
\else
  \set confirmar ''
\endif

\if :{?permitir_conexao_remota}
\else
  \set permitir_conexao_remota 'nao'
\endif

BEGIN;

SELECT set_config('contix.confirmar', :'confirmar', true),
       set_config('contix.admin_nome', :'admin_nome', true),
       set_config('contix.permitir_remoto', :'permitir_conexao_remota', true)
\g /dev/null

DO $zerar$
DECLARE
    v_admin_nome  text := current_setting('contix.admin_nome');
    v_admin_id    uuid;
    v_tenant_id   uuid;
    v_qtd         int;
    v_tabelas     text;
    v_usuarios    int;
    v_escritorios int;
BEGIN
    IF current_setting('contix.confirmar') IS DISTINCT FROM 'ZERAR' THEN
        RAISE EXCEPTION 'Abortado: operação destrutiva. Rode com -v confirmar=ZERAR para confirmar.';
    END IF;

    IF inet_server_addr() IS NOT NULL
       AND NOT host(inet_server_addr()) IN ('127.0.0.1', '::1')
       AND current_setting('contix.permitir_remoto') IS DISTINCT FROM 'sim' THEN
        RAISE EXCEPTION 'Abortado: conexão remota (%). Este script só roda no Postgres local do docker compose (docker compose exec db psql ...).',
            host(inet_server_addr());
    END IF;

    SELECT count(*) INTO v_qtd FROM usuarios WHERE nome = v_admin_nome AND NOT is_deleted;
    IF v_qtd <> 1 THEN
        RAISE EXCEPTION 'Abortado: esperava 1 usuário ativo chamado "%", encontrei %. Nada foi apagado.', v_admin_nome, v_qtd;
    END IF;

    SELECT id, tenant_id INTO v_admin_id, v_tenant_id
      FROM usuarios WHERE nome = v_admin_nome AND NOT is_deleted;

    -- Todas as tabelas do schema, menos as que guardam o admin e o histórico de migrations
    -- ("__ef_migrations_history"; o filtro pega qualquer nome de tabela de histórico).
    -- Sem CASCADE de propósito: se alguma FK nova apontar para usuarios/escritorios, o TRUNCATE falha e nada é apagado.
    SELECT string_agg(format('%I.%I', schemaname, tablename), ', ' ORDER BY tablename)
      INTO v_tabelas
      FROM pg_tables
     WHERE schemaname = current_schema()
       AND tablename NOT IN ('usuarios', 'escritorios')
       AND tablename NOT ILIKE '%migrations%history%';

    IF v_tabelas IS NOT NULL THEN
        EXECUTE 'TRUNCATE TABLE ' || v_tabelas || ' RESTART IDENTITY';
    END IF;

    DELETE FROM usuarios WHERE id <> v_admin_id;
    GET DIAGNOSTICS v_usuarios = ROW_COUNT;

    DELETE FROM escritorios WHERE id <> v_tenant_id;
    GET DIAGNOSTICS v_escritorios = ROW_COUNT;

    -- Recomeça a numeração de solicitações/tarefas e libera eventual bloqueio de login do admin.
    UPDATE escritorios SET ultimo_numero_solicitacao = 0, ultimo_numero_tarefa = 0 WHERE id = v_tenant_id;
    UPDATE usuarios SET tentativas_login_falhas = 0, bloqueado_ate = NULL WHERE id = v_admin_id;

    RAISE NOTICE 'Tabelas esvaziadas: %', v_tabelas;
    RAISE NOTICE 'Usuários removidos: % | Escritórios removidos: %', v_usuarios, v_escritorios;
    RAISE NOTICE 'Mantidos: usuário "%" (%) e o escritório %.', v_admin_nome, v_admin_id, v_tenant_id;
END
$zerar$;

COMMIT;

\echo 'Banco zerado. Faça login novamente com o usuário mantido (as sessões foram encerradas).'
