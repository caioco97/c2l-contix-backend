using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AppMobilePush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dispositivos_push",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    plataforma = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ultimo_uso_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispositivos_push", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispositivos_push_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pushes_pendentes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    mensagem = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    dados = table.Column<string>(type: "jsonb", nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    proxima_tentativa_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    enviado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ultimo_erro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pushes_pendentes", x => x.id);
                    table.ForeignKey(
                        name: "fk_pushes_pendentes_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dispositivos_push_token",
                table: "dispositivos_push",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dispositivos_push_usuario_id",
                table: "dispositivos_push",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_pushes_pendentes_proxima_tentativa_em",
                table: "pushes_pendentes",
                column: "proxima_tentativa_em",
                filter: "enviado_em IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pushes_pendentes_usuario_id",
                table: "pushes_pendentes",
                column: "usuario_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dispositivos_push");

            migrationBuilder.DropTable(
                name: "pushes_pendentes");
        }
    }
}
