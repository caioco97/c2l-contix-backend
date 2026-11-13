using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmpregadosPorEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "empregado_id",
                table: "solicitacoes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "empregados",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    cpf = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    matricula = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    cargo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    vinculo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    situacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    data_admissao = table.Column<DateOnly>(type: "date", nullable: true),
                    data_desligamento = table.Column<DateOnly>(type: "date", nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    telefone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    solicitacao_origem_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_empregados", x => x.id);
                    table.ForeignKey(
                        name: "fk_empregados_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_empregados_escritorios_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "escritorios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_empregado_id",
                table: "solicitacoes",
                column: "empregado_id");

            migrationBuilder.CreateIndex(
                name: "ix_empregados_empresa_id_cpf",
                table: "empregados",
                columns: new[] { "empresa_id", "cpf" },
                unique: true,
                filter: "cpf IS NOT NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_empregados_tenant_id_empresa_id_nome",
                table: "empregados",
                columns: new[] { "tenant_id", "empresa_id", "nome" });

            migrationBuilder.AddForeignKey(
                name: "fk_solicitacoes_empregados_empregado_id",
                table: "solicitacoes",
                column: "empregado_id",
                principalTable: "empregados",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // ---------- Dados existentes: do texto livre para o cadastro ----------
            migrationBuilder.Sql(BackfillSql);
        }

        /// <summary>
        /// 1) Cadastros de pessoa concluídos viram empregados (por CPF, o mais recente vence).
        /// 2) Nomes digitados em férias, rescisão, afastamento e rubricas que não casam com um empregado
        ///    da empresa (nome sem diferenciar maiúsculas) viram empregados sem CPF, para o escritório completar.
        /// 3) As solicitações passam a apontar para o empregado (coluna e "empregadoId" nos dados).
        /// 4) Rescisões concluídas marcam o empregado como desligado.
        /// </summary>
        private const string BackfillSql = """
            INSERT INTO empregados (id, tenant_id, empresa_id, nome, cpf, cargo, vinculo, situacao, data_admissao,
                                    email, telefone, solicitacao_origem_id, created_at, is_deleted)
            SELECT DISTINCT ON (s.empresa_id, regexp_replace(s.dados->>'cpf', '\D', '', 'g'))
                   gen_random_uuid(), s.tenant_id, s.empresa_id, left(trim(s.dados->>'nome'), 150),
                   regexp_replace(s.dados->>'cpf', '\D', '', 'g'),
                   left(nullif(trim(s.dados->>'cargo'), ''), 100),
                   CASE s.tipo WHEN 'CadastroContribuinte' THEN 'Contribuinte'
                               WHEN 'CadastroEstagiario' THEN 'Estagiario' ELSE 'Empregado' END,
                   'Ativo', (s.dados->>'dataAdmissao')::date,
                   left(lower(nullif(trim(s.dados->>'email'), '')), 200), left(nullif(trim(s.dados->>'telefone'), ''), 30),
                   s.id, now(), false
            FROM solicitacoes s
            WHERE NOT s.is_deleted AND s.status = 'Concluida'
              AND s.tipo IN ('CadastroEmpregado', 'CadastroContribuinte', 'CadastroEstagiario')
              AND coalesce(trim(s.dados->>'nome'), '') <> ''
              AND length(regexp_replace(coalesce(s.dados->>'cpf', ''), '\D', '', 'g')) = 11
            ORDER BY s.empresa_id, regexp_replace(s.dados->>'cpf', '\D', '', 'g'), s.created_at DESC;

            UPDATE solicitacoes s SET empregado_id = e.id
            FROM empregados e
            WHERE s.empregado_id IS NULL AND NOT s.is_deleted AND s.status = 'Concluida'
              AND s.tipo IN ('CadastroEmpregado', 'CadastroContribuinte', 'CadastroEstagiario')
              AND e.empresa_id = s.empresa_id AND e.cpf = regexp_replace(s.dados->>'cpf', '\D', '', 'g');

            WITH nomes AS (
                SELECT s.tenant_id, s.empresa_id, trim(s.dados->>'empregado') AS nome, s.created_at
                FROM solicitacoes s
                WHERE NOT s.is_deleted
                  AND s.tipo IN ('AvisoFerias', 'CalculoFerias', 'AvisoRescisao', 'CalculoRescisao', 'Afastamento')
                  AND coalesce(trim(s.dados->>'empregado'), '') <> ''
                UNION ALL
                SELECT s.tenant_id, s.empresa_id, trim(l->>'empregado'), s.created_at
                FROM solicitacoes s
                CROSS JOIN LATERAL jsonb_array_elements(
                    CASE WHEN jsonb_typeof(s.dados->'lancamentos') = 'array' THEN s.dados->'lancamentos' ELSE '[]'::jsonb END) l
                WHERE NOT s.is_deleted AND s.tipo = 'LancamentoRubricas' AND coalesce(trim(l->>'empregado'), '') <> ''
            )
            INSERT INTO empregados (id, tenant_id, empresa_id, nome, vinculo, situacao, created_at, is_deleted)
            SELECT gen_random_uuid(), n.tenant_id, n.empresa_id, left(n.nome, 150), 'Empregado', 'Ativo', now(), false
            FROM (SELECT DISTINCT ON (empresa_id, lower(nome)) tenant_id, empresa_id, nome
                  FROM nomes ORDER BY empresa_id, lower(nome), created_at) n
            WHERE NOT EXISTS (SELECT 1 FROM empregados e
                              WHERE e.empresa_id = n.empresa_id AND NOT e.is_deleted AND lower(e.nome) = lower(n.nome));

            WITH alvo AS (
                SELECT DISTINCT ON (empresa_id, lower(nome)) id, empresa_id, lower(nome) AS chave, nome
                FROM empregados WHERE NOT is_deleted ORDER BY empresa_id, lower(nome), created_at, id
            )
            UPDATE solicitacoes s
            SET empregado_id = a.id,
                dados = jsonb_set(s.dados, '{empregadoId}', to_jsonb(a.id::text))
            FROM alvo a
            WHERE NOT s.is_deleted
              AND s.tipo IN ('AvisoFerias', 'CalculoFerias', 'AvisoRescisao', 'CalculoRescisao', 'Afastamento')
              AND a.empresa_id = s.empresa_id AND a.chave = lower(trim(s.dados->>'empregado'));

            WITH alvo AS (
                SELECT DISTINCT ON (empresa_id, lower(nome)) id, empresa_id, lower(nome) AS chave
                FROM empregados WHERE NOT is_deleted ORDER BY empresa_id, lower(nome), created_at, id
            )
            UPDATE solicitacoes s
            SET dados = jsonb_set(s.dados, '{lancamentos}', (
                SELECT jsonb_agg(CASE WHEN a.id IS NULL THEN x.l ELSE x.l || jsonb_build_object('empregadoId', a.id::text) END
                                 ORDER BY x.ord)
                FROM jsonb_array_elements(s.dados->'lancamentos') WITH ORDINALITY AS x(l, ord)
                LEFT JOIN alvo a ON a.empresa_id = s.empresa_id AND a.chave = lower(trim(x.l->>'empregado'))))
            WHERE NOT s.is_deleted AND s.tipo = 'LancamentoRubricas'
              AND jsonb_typeof(s.dados->'lancamentos') = 'array' AND jsonb_array_length(s.dados->'lancamentos') > 0;

            UPDATE empregados e
            SET situacao = 'Desligado', data_desligamento = (s.dados->>'dataDesligamento')::date
            FROM solicitacoes s
            WHERE s.empregado_id = e.id AND NOT s.is_deleted AND s.status = 'Concluida'
              AND s.tipo IN ('AvisoRescisao', 'CalculoRescisao') AND s.dados->>'dataDesligamento' IS NOT NULL;
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_solicitacoes_empregados_empregado_id",
                table: "solicitacoes");

            migrationBuilder.DropTable(
                name: "empregados");

            migrationBuilder.DropIndex(
                name: "ix_solicitacoes_empregado_id",
                table: "solicitacoes");

            migrationBuilder.DropColumn(
                name: "empregado_id",
                table: "solicitacoes");
        }
    }
}
