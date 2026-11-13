using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmpresasPortalAvisos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "convite_aceito_em",
                table: "usuarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "convite_enviado_em",
                table: "usuarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "modulos",
                table: "usuario_empresas",
                type: "text[]",
                nullable: false,
                // Vínculos existentes mantêm acesso a todos os módulos.
                defaultValueSql: "ARRAY['Documentos','Solicitacoes','NFe']::text[]");

            migrationBuilder.AddColumn<List<Guid>>(
                name: "pasta_ids",
                table: "usuario_empresas",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<bool>(
                name: "todas_as_pastas",
                table: "usuario_empresas",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "notificada_em",
                table: "noticias",
                type: "timestamp with time zone",
                nullable: true);

            // Notícias já publicadas não geram aviso retroativo; as agendadas serão avisadas na data.
            migrationBuilder.Sql("UPDATE noticias SET notificada_em = publicar_em WHERE publicar_em <= now();");

            migrationBuilder.AddColumn<DateTime>(
                name: "aviso_vencida_em",
                table: "certidoes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "aviso_vencimento_15_em",
                table: "certidoes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "aviso_vencimento_30_em",
                table: "certidoes",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "convite_aceito_em",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "convite_enviado_em",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "modulos",
                table: "usuario_empresas");

            migrationBuilder.DropColumn(
                name: "pasta_ids",
                table: "usuario_empresas");

            migrationBuilder.DropColumn(
                name: "todas_as_pastas",
                table: "usuario_empresas");

            migrationBuilder.DropColumn(
                name: "notificada_em",
                table: "noticias");

            migrationBuilder.DropColumn(
                name: "aviso_vencida_em",
                table: "certidoes");

            migrationBuilder.DropColumn(
                name: "aviso_vencimento_15_em",
                table: "certidoes");

            migrationBuilder.DropColumn(
                name: "aviso_vencimento_30_em",
                table: "certidoes");
        }
    }
}
