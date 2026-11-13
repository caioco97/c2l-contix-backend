-- =============================================================================
-- INICIALIZAR / REPARAR O BANCO DO CONTIX (SOMENTE DESENVOLVIMENTO)
--
-- Reconstrói a tabela de histórico de migrations do EF ("__ef_migrations_history")
-- a partir do que já existe no schema. Use quando a API não sobe porque o EF
-- tenta recriar tabelas que já existem (ex.: o histórico foi apagado pela
-- primeira versão do zerar-banco.sql).
--
-- Para cada migration conhecida há um "sentinela" (tabela ou coluna que ela cria).
-- O script marca como aplicadas todas as migrations até a última cujo sentinela
-- existe; as seguintes ficam pendentes e a API aplica no próximo start.
-- Não apaga nem altera dados. Pode ser rodado mais de uma vez.
--
-- Ao criar uma migration nova, acrescente-a na lista abaixo.
-- =============================================================================

\set ON_ERROR_STOP on
\set QUIET on

BEGIN;

CREATE TEMP TABLE _migrations (ordem int, id text, tabela text, coluna text) ON COMMIT DROP;
INSERT INTO _migrations VALUES
    ( 1, '20261112220247_InitialCreate',                'escritorios',       NULL),
    ( 2, '20261112230621_Solicitacoes',                 'escritorios',       'ultimo_numero_solicitacao'),
    ( 3, '20261112234107_NoticiasCertidoes',            'noticias',          NULL),
    ( 4, '20261112235453_RecuperacaoSenhaNotificacoes', 'escritorios',       'dias_lembrete_vencimento'),
    ( 5, '20261113000222_DocumentosOrigemAuditoria',    'documento_eventos', NULL),
    ( 6, '20261113004107_EmpresasPortalAvisos',         'usuarios',          'convite_enviado_em'),
    ( 7, '20261113040732_EmpresaExclusao',              'empresas',          'excluida_por_id'),
    ( 8, '20261113040844_EmpregadosPorEmpresa',         'empregados',        NULL),
    ( 9, '20261113041731_Tarefas',                      'tarefas',           NULL),
    (10, '20261113042817_NormalizarMascaras',           NULL,                NULL), -- só dados: segue a próxima
    (11, '20261113124330_EmissaoAutomaticaCertidoes',   'certidoes',         'emitida_automaticamente'),
    (12, '20261113132407_IdentidadeVisualEmpresa',      'empresas',          'cor_primaria'),
    (13, '20261113135459_NotasFiscaisSefaz',            'notas_fiscais',     NULL),
    (14, '20261113172116_DocumentoEventoIp',            'documento_eventos', 'ip');

DO $init$
DECLARE
    v_ultima    int;
    v_col_id    text;
    v_col_ver   text;
    v_inseridas int;
    v_pendentes text;
BEGIN
    SELECT max(m.ordem) INTO v_ultima
      FROM _migrations m
     WHERE m.tabela IS NOT NULL
       AND EXISTS (SELECT 1 FROM information_schema.columns c
                    WHERE c.table_schema = current_schema()
                      AND c.table_name = m.tabela
                      AND (m.coluna IS NULL OR c.column_name = m.coluna));

    IF v_ultima IS NULL THEN
        RAISE NOTICE 'Banco vazio (nenhuma tabela do Contix). Nada a fazer: suba a API e ela cria tudo (migrations + seed).';
        RETURN;
    END IF;

    IF to_regclass('"__ef_migrations_history"') IS NULL THEN
        CREATE TABLE "__ef_migrations_history" (
            "MigrationId"    varchar(150) NOT NULL PRIMARY KEY,
            "ProductVersion" varchar(32)  NOT NULL
        );
    END IF;

    -- Descobre o nome das colunas (o padrão do EF é MigrationId/ProductVersion).
    SELECT column_name INTO v_col_id FROM information_schema.columns
     WHERE table_schema = current_schema() AND table_name = '__ef_migrations_history'
       AND column_name IN ('MigrationId', 'migration_id');
    SELECT column_name INTO v_col_ver FROM information_schema.columns
     WHERE table_schema = current_schema() AND table_name = '__ef_migrations_history'
       AND column_name IN ('ProductVersion', 'product_version');

    EXECUTE format(
        'INSERT INTO "__ef_migrations_history" (%I, %I)
         SELECT id, %L FROM _migrations WHERE ordem <= %s
         ON CONFLICT DO NOTHING', v_col_id, v_col_ver, '10.0.15', v_ultima);
    GET DIAGNOSTICS v_inseridas = ROW_COUNT;

    SELECT string_agg(id, ', ' ORDER BY ordem) INTO v_pendentes FROM _migrations WHERE ordem > v_ultima;

    RAISE NOTICE 'Histórico de migrations: % registro(s) incluído(s), schema até %.',
        v_inseridas, (SELECT id FROM _migrations WHERE ordem = v_ultima);
    IF v_pendentes IS NOT NULL THEN
        RAISE NOTICE 'Pendentes (a API aplica ao subir): %', v_pendentes;
    END IF;
END
$init$;

COMMIT;

\echo 'Pronto. Suba a API normalmente.'
