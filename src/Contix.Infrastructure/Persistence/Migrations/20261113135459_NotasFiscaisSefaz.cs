using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotasFiscaisSefaz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "certificados_digitais",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome_arquivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    titular = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    documento = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    emissor = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    numero_serie = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    thumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    valido_de = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valido_ate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    arquivo_criptografado = table.Column<byte[]>(type: "bytea", nullable: false),
                    senha_criptografada = table.Column<byte[]>(type: "bytea", nullable: false),
                    enviado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificados_digitais", x => x.id);
                    table.ForeignKey(
                        name: "fk_certificados_digitais_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_certificados_digitais_usuarios_enviado_por_id",
                        column: x => x.enviado_por_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notas_fiscais",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chave_acesso = table.Column<string>(type: "character(44)", fixedLength: true, maxLength: 44, nullable: false),
                    modelo = table.Column<int>(type: "integer", nullable: false),
                    serie = table.Column<int>(type: "integer", nullable: false),
                    numero = table.Column<long>(type: "bigint", nullable: false),
                    emitente_documento = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    emitente_nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    emitente_ie = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    destinatario_documento = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    valor_total = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    data_emissao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tipo_operacao = table.Column<int>(type: "integer", nullable: true),
                    protocolo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    situacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xml_completo = table.Column<bool>(type: "boolean", nullable: false),
                    xml_storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    nsu = table.Column<long>(type: "bigint", nullable: true),
                    manifestacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    manifestada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    manifestada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    justificativa_manifestacao = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    protocolo_manifestacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notas_fiscais", x => x.id);
                    table.ForeignKey(
                        name: "fk_notas_fiscais_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notas_fiscais_usuarios_manifestada_por_id",
                        column: x => x.manifestada_por_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "sincronizacoes_nfe",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    ciencia_automatica = table.Column<bool>(type: "boolean", nullable: false),
                    ultimo_nsu = table.Column<long>(type: "bigint", nullable: false),
                    max_nsu = table.Column<long>(type: "bigint", nullable: false),
                    proxima_consulta_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ultima_consulta_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ultimo_status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ultima_mensagem = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ultima_consulta_falhou = table.Column<bool>(type: "boolean", nullable: false),
                    documentos_recebidos = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sincronizacoes_nfe", x => x.id);
                    table.ForeignKey(
                        name: "fk_sincronizacoes_nfe_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_certificados_digitais_empresa_id",
                table: "certificados_digitais",
                column: "empresa_id",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_certificados_digitais_enviado_por_id",
                table: "certificados_digitais",
                column: "enviado_por_id");

            migrationBuilder.CreateIndex(
                name: "ix_notas_fiscais_empresa_id_chave_acesso",
                table: "notas_fiscais",
                columns: new[] { "empresa_id", "chave_acesso" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_notas_fiscais_manifestada_por_id",
                table: "notas_fiscais",
                column: "manifestada_por_id");

            migrationBuilder.CreateIndex(
                name: "ix_notas_fiscais_tenant_id_empresa_id_data_emissao",
                table: "notas_fiscais",
                columns: new[] { "tenant_id", "empresa_id", "data_emissao" });

            migrationBuilder.CreateIndex(
                name: "ix_sincronizacoes_nfe_ativa_proxima_consulta_em",
                table: "sincronizacoes_nfe",
                columns: new[] { "ativa", "proxima_consulta_em" });

            migrationBuilder.CreateIndex(
                name: "ix_sincronizacoes_nfe_empresa_id",
                table: "sincronizacoes_nfe",
                column: "empresa_id",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "certificados_digitais");

            migrationBuilder.DropTable(
                name: "notas_fiscais");

            migrationBuilder.DropTable(
                name: "sincronizacoes_nfe");
        }
    }
}
