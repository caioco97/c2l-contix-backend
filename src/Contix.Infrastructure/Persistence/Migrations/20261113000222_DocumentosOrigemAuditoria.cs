using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentosOrigemAuditoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "origem",
                table: "documentos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Escritorio");

            // Documentos já existentes enviados por usuários do cliente.
            migrationBuilder.Sql("""
                UPDATE documentos d SET origem = 'Cliente'
                FROM usuarios u
                WHERE u.id = d.adicionado_por_id AND u.perfil = 'Cliente';
                """);

            migrationBuilder.CreateTable(
                name: "documento_eventos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ocorrido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    detalhe = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documento_eventos", x => x.id);
                    table.ForeignKey(
                        name: "fk_documento_eventos_documentos_documento_id",
                        column: x => x.documento_id,
                        principalTable: "documentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_documento_eventos_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documentos_empresa_id_competencia",
                table: "documentos",
                columns: new[] { "empresa_id", "competencia" });

            migrationBuilder.CreateIndex(
                name: "ix_documento_eventos_documento_id_ocorrido_em",
                table: "documento_eventos",
                columns: new[] { "documento_id", "ocorrido_em" });

            migrationBuilder.CreateIndex(
                name: "ix_documento_eventos_usuario_id",
                table: "documento_eventos",
                column: "usuario_id");

            // O histórico começa com o upload dos documentos já existentes.
            migrationBuilder.Sql("""
                INSERT INTO documento_eventos (id, documento_id, usuario_id, tipo, ocorrido_em, detalhe, created_at)
                SELECT gen_random_uuid(), d.id, d.adicionado_por_id, 'Upload', d.publicado_em, NULL, now()
                FROM documentos d;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documento_eventos");

            migrationBuilder.DropIndex(
                name: "ix_documentos_empresa_id_competencia",
                table: "documentos");

            migrationBuilder.DropColumn(
                name: "origem",
                table: "documentos");
        }
    }
}
