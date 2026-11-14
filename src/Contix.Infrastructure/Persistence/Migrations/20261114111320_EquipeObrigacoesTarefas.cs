using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EquipeObrigacoesTarefas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "departamentos",
                table: "usuarios",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<DateOnly>(
                name: "competencia",
                table: "tarefas",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "documentos_entregues",
                table: "tarefas",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<Guid>(
                name: "obrigacao_id",
                table: "tarefas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "solicitacao_id",
                table: "tarefas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "departamentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_departamentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_departamentos_escritorios_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "escritorios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "empresa_responsaveis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    departamento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_empresa_responsaveis", x => x.id);
                    table.ForeignKey(
                        name: "fk_empresa_responsaveis_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_empresa_responsaveis_escritorios_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "escritorios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_empresa_responsaveis_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "obrigacoes_recorrentes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descricao = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    departamento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    periodicidade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dia_vencimento = table.Column<int>(type: "integer", nullable: false),
                    meses_apos_competencia = table.Column<int>(type: "integer", nullable: false),
                    mes_referencia = table.Column<int>(type: "integer", nullable: false),
                    dias_antecedencia = table.Column<int>(type: "integer", nullable: false),
                    regimes = table.Column<string[]>(type: "text[]", nullable: false),
                    responsavel_padrao_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    criada_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_obrigacoes_recorrentes", x => x.id);
                    table.ForeignKey(
                        name: "fk_obrigacoes_recorrentes_escritorios_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "escritorios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_obrigacoes_recorrentes_usuarios_responsavel_padrao_id",
                        column: x => x.responsavel_padrao_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tarefas_obrigacao_id_empresa_id_competencia",
                table: "tarefas",
                columns: new[] { "obrigacao_id", "empresa_id", "competencia" },
                unique: true,
                filter: "obrigacao_id IS NOT NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_tarefas_solicitacao_id",
                table: "tarefas",
                column: "solicitacao_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_tenant_id_created_at",
                table: "solicitacoes",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_departamentos_tenant_id_codigo",
                table: "departamentos",
                columns: new[] { "tenant_id", "codigo" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_empresa_responsaveis_empresa_id_departamento",
                table: "empresa_responsaveis",
                columns: new[] { "empresa_id", "departamento" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_empresa_responsaveis_tenant_id_usuario_id",
                table: "empresa_responsaveis",
                columns: new[] { "tenant_id", "usuario_id" });

            migrationBuilder.CreateIndex(
                name: "ix_empresa_responsaveis_usuario_id",
                table: "empresa_responsaveis",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_obrigacoes_recorrentes_responsavel_padrao_id",
                table: "obrigacoes_recorrentes",
                column: "responsavel_padrao_id");

            migrationBuilder.CreateIndex(
                name: "ix_obrigacoes_recorrentes_tenant_id_ativa",
                table: "obrigacoes_recorrentes",
                columns: new[] { "tenant_id", "ativa" });

            migrationBuilder.AddForeignKey(
                name: "fk_tarefas_obrigacoes_recorrentes_obrigacao_id",
                table: "tarefas",
                column: "obrigacao_id",
                principalTable: "obrigacoes_recorrentes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_tarefas_solicitacoes_solicitacao_id",
                table: "tarefas",
                column: "solicitacao_id",
                principalTable: "solicitacoes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tarefas_obrigacoes_recorrentes_obrigacao_id",
                table: "tarefas");

            migrationBuilder.DropForeignKey(
                name: "fk_tarefas_solicitacoes_solicitacao_id",
                table: "tarefas");

            migrationBuilder.DropTable(
                name: "departamentos");

            migrationBuilder.DropTable(
                name: "empresa_responsaveis");

            migrationBuilder.DropTable(
                name: "obrigacoes_recorrentes");

            migrationBuilder.DropIndex(
                name: "ix_tarefas_obrigacao_id_empresa_id_competencia",
                table: "tarefas");

            migrationBuilder.DropIndex(
                name: "ix_tarefas_solicitacao_id",
                table: "tarefas");

            migrationBuilder.DropIndex(
                name: "ix_solicitacoes_tenant_id_created_at",
                table: "solicitacoes");

            migrationBuilder.DropColumn(
                name: "departamentos",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "competencia",
                table: "tarefas");

            migrationBuilder.DropColumn(
                name: "documentos_entregues",
                table: "tarefas");

            migrationBuilder.DropColumn(
                name: "obrigacao_id",
                table: "tarefas");

            migrationBuilder.DropColumn(
                name: "solicitacao_id",
                table: "tarefas");
        }
    }
}
