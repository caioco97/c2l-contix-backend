using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecuperacaoSenhaNotificacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "dias_lembrete_vencimento",
                table: "escritorios",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<DateTime>(
                name: "lembrete_vencimento_enviado_em",
                table: "documentos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "emails_pendentes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    para = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    nome_para = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    assunto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    corpo_html = table.Column<string>(type: "text", nullable: false),
                    corpo_texto = table.Column<string>(type: "text", nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    proxima_tentativa_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    enviado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ultimo_erro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emails_pendentes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notificacoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    titulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    mensagem = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    referencia_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lida_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notificacoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_notificacoes_escritorios_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "escritorios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notificacoes_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "preferencias_notificacao",
                columns: table => new
                {
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    receber_email = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preferencias_notificacao", x => new { x.usuario_id, x.tipo });
                    table.ForeignKey(
                        name: "fk_preferencias_notificacao_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tokens_redefinicao_senha",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    usado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    invalidado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    criado_por_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tokens_redefinicao_senha", x => x.id);
                    table.ForeignKey(
                        name: "fk_tokens_redefinicao_senha_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_emails_pendentes_proxima_tentativa_em",
                table: "emails_pendentes",
                column: "proxima_tentativa_em",
                filter: "enviado_em IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_tenant_id",
                table: "notificacoes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_usuario_id_lida_em_created_at",
                table: "notificacoes",
                columns: new[] { "usuario_id", "lida_em", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tokens_redefinicao_senha_token_hash",
                table: "tokens_redefinicao_senha",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tokens_redefinicao_senha_usuario_id",
                table: "tokens_redefinicao_senha",
                column: "usuario_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "emails_pendentes");

            migrationBuilder.DropTable(
                name: "notificacoes");

            migrationBuilder.DropTable(
                name: "preferencias_notificacao");

            migrationBuilder.DropTable(
                name: "tokens_redefinicao_senha");

            migrationBuilder.DropColumn(
                name: "dias_lembrete_vencimento",
                table: "escritorios");

            migrationBuilder.DropColumn(
                name: "lembrete_vencimento_enviado_em",
                table: "documentos");
        }
    }
}
