using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmissaoAutomaticaCertidoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "cadastrado_por_id",
                table: "certidoes",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<bool>(
                name: "emitida_automaticamente",
                table: "certidoes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "configuracoes_emissao_certidao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    tipos = table.Column<string[]>(type: "text[]", nullable: false),
                    dias_antecedencia = table.Column<int>(type: "integer", nullable: false),
                    responsavel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracoes_emissao_certidao", x => x.id);
                    table.ForeignKey(
                        name: "fk_configuracoes_emissao_certidao_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_configuracoes_emissao_certidao_usuarios_responsavel_id",
                        column: x => x.responsavel_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "emissoes_certidao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    proxima_tentativa_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    concluida_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    provedor = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    mensagem = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    certidao_id = table.Column<Guid>(type: "uuid", nullable: true),
                    solicitado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emissoes_certidao", x => x.id);
                    table.ForeignKey(
                        name: "fk_emissoes_certidao_certidoes_certidao_id",
                        column: x => x.certidao_id,
                        principalTable: "certidoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_emissoes_certidao_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_emissoes_certidao_usuarios_solicitado_por_id",
                        column: x => x.solicitado_por_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_configuracoes_emissao_certidao_empresa_id",
                table: "configuracoes_emissao_certidao",
                column: "empresa_id",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_configuracoes_emissao_certidao_responsavel_id",
                table: "configuracoes_emissao_certidao",
                column: "responsavel_id");

            migrationBuilder.CreateIndex(
                name: "ix_emissoes_certidao_certidao_id",
                table: "emissoes_certidao",
                column: "certidao_id");

            migrationBuilder.CreateIndex(
                name: "ix_emissoes_certidao_empresa_id",
                table: "emissoes_certidao",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_emissoes_certidao_solicitado_por_id",
                table: "emissoes_certidao",
                column: "solicitado_por_id");

            migrationBuilder.CreateIndex(
                name: "ix_emissoes_certidao_status_proxima_tentativa_em",
                table: "emissoes_certidao",
                columns: new[] { "status", "proxima_tentativa_em" });

            migrationBuilder.CreateIndex(
                name: "ix_emissoes_certidao_tenant_id_empresa_id_created_at",
                table: "emissoes_certidao",
                columns: new[] { "tenant_id", "empresa_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configuracoes_emissao_certidao");

            migrationBuilder.DropTable(
                name: "emissoes_certidao");

            migrationBuilder.DropColumn(
                name: "emitida_automaticamente",
                table: "certidoes");

            migrationBuilder.AlterColumn<Guid>(
                name: "cadastrado_por_id",
                table: "certidoes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
