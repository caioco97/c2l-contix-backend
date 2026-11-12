using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NoticiasCertidoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "certidoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    situacao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    numero = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    emissao = table.Column<DateOnly>(type: "date", nullable: false),
                    validade = table.Column<DateOnly>(type: "date", nullable: false),
                    observacao = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    nome_arquivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    tamanho_bytes = table.Column<long>(type: "bigint", nullable: true),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cadastrado_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certidoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_certidoes_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_certidoes_usuarios_cadastrado_por_id",
                        column: x => x.cadastrado_por_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "noticias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    resumo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    conteudo = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    publicar_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    publico = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    regimes = table.Column<string[]>(type: "text[]", nullable: false),
                    imagem_storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    imagem_content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    autor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_noticias", x => x.id);
                    table.ForeignKey(
                        name: "fk_noticias_escritorios_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "escritorios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_noticias_usuarios_autor_id",
                        column: x => x.autor_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "noticia_anexos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    noticia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome_arquivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tamanho_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_noticia_anexos", x => x.id);
                    table.ForeignKey(
                        name: "fk_noticia_anexos_noticias_noticia_id",
                        column: x => x.noticia_id,
                        principalTable: "noticias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "noticia_empresas",
                columns: table => new
                {
                    noticia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_noticia_empresas", x => new { x.noticia_id, x.empresa_id });
                    table.ForeignKey(
                        name: "fk_noticia_empresas_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_noticia_empresas_noticias_noticia_id",
                        column: x => x.noticia_id,
                        principalTable: "noticias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "noticia_leituras",
                columns: table => new
                {
                    noticia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_noticia_leituras", x => new { x.noticia_id, x.empresa_id, x.usuario_id });
                    table.ForeignKey(
                        name: "fk_noticia_leituras_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_noticia_leituras_noticias_noticia_id",
                        column: x => x.noticia_id,
                        principalTable: "noticias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_noticia_leituras_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_certidoes_cadastrado_por_id",
                table: "certidoes",
                column: "cadastrado_por_id");

            migrationBuilder.CreateIndex(
                name: "ix_certidoes_empresa_id",
                table: "certidoes",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_certidoes_tenant_id_empresa_id_tipo_validade",
                table: "certidoes",
                columns: new[] { "tenant_id", "empresa_id", "tipo", "validade" });

            migrationBuilder.CreateIndex(
                name: "ix_certidoes_tenant_id_validade",
                table: "certidoes",
                columns: new[] { "tenant_id", "validade" });

            migrationBuilder.CreateIndex(
                name: "ix_noticia_anexos_noticia_id",
                table: "noticia_anexos",
                column: "noticia_id");

            migrationBuilder.CreateIndex(
                name: "ix_noticia_empresas_empresa_id",
                table: "noticia_empresas",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_noticia_leituras_empresa_id",
                table: "noticia_leituras",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_noticia_leituras_usuario_id",
                table: "noticia_leituras",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_noticias_autor_id",
                table: "noticias",
                column: "autor_id");

            migrationBuilder.CreateIndex(
                name: "ix_noticias_tenant_id_publicar_em",
                table: "noticias",
                columns: new[] { "tenant_id", "publicar_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "certidoes");

            migrationBuilder.DropTable(
                name: "noticia_anexos");

            migrationBuilder.DropTable(
                name: "noticia_empresas");

            migrationBuilder.DropTable(
                name: "noticia_leituras");

            migrationBuilder.DropTable(
                name: "noticias");
        }
    }
}
