using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentidadeVisualEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cor_primaria",
                table: "empresas",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cor_secundaria",
                table: "empresas",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "logo_atualizado_em",
                table: "empresas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_content_type",
                table: "empresas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_storage_key",
                table: "empresas",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cor_primaria",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "cor_secundaria",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "logo_atualizado_em",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "logo_content_type",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "logo_storage_key",
                table: "empresas");
        }
    }
}
