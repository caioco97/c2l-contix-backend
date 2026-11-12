using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Solicitacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ultimo_numero_solicitacao",
                table: "escritorios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "solicitacoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    departamento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    assunto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descricao = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    dados = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    solicitante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    responsavel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prazo = table.Column<DateOnly>(type: "date", nullable: true),
                    finalizada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitacoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_solicitacoes_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_solicitacoes_escritorios_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "escritorios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_solicitacoes_usuarios_responsavel_id",
                        column: x => x.responsavel_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_solicitacoes_usuarios_solicitante_id",
                        column: x => x.solicitante_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "solicitacao_interacoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    autor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    mensagem = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    status_anterior = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    status_novo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitacao_interacoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_solicitacao_interacoes_solicitacoes_solicitacao_id",
                        column: x => x.solicitacao_id,
                        principalTable: "solicitacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_solicitacao_interacoes_usuarios_autor_id",
                        column: x => x.autor_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "solicitacao_anexos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interacao_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nome_arquivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tamanho_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    enviado_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitacao_anexos", x => x.id);
                    table.ForeignKey(
                        name: "fk_solicitacao_anexos_solicitacao_interacoes_interacao_id",
                        column: x => x.interacao_id,
                        principalTable: "solicitacao_interacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_solicitacao_anexos_solicitacoes_solicitacao_id",
                        column: x => x.solicitacao_id,
                        principalTable: "solicitacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_solicitacao_anexos_usuarios_enviado_por_id",
                        column: x => x.enviado_por_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_solicitacao_anexos_enviado_por_id",
                table: "solicitacao_anexos",
                column: "enviado_por_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacao_anexos_interacao_id",
                table: "solicitacao_anexos",
                column: "interacao_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacao_anexos_solicitacao_id",
                table: "solicitacao_anexos",
                column: "solicitacao_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacao_interacoes_autor_id",
                table: "solicitacao_interacoes",
                column: "autor_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacao_interacoes_solicitacao_id_created_at",
                table: "solicitacao_interacoes",
                columns: new[] { "solicitacao_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_empresa_id",
                table: "solicitacoes",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_responsavel_id",
                table: "solicitacoes",
                column: "responsavel_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_solicitante_id",
                table: "solicitacoes",
                column: "solicitante_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_tenant_id_empresa_id_status",
                table: "solicitacoes",
                columns: new[] { "tenant_id", "empresa_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_tenant_id_numero",
                table: "solicitacoes",
                columns: new[] { "tenant_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_tenant_id_responsavel_id",
                table: "solicitacoes",
                columns: new[] { "tenant_id", "responsavel_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "solicitacao_anexos");

            migrationBuilder.DropTable(
                name: "solicitacao_interacoes");

            migrationBuilder.DropTable(
                name: "solicitacoes");

            migrationBuilder.DropColumn(
                name: "ultimo_numero_solicitacao",
                table: "escritorios");
        }
    }
}
