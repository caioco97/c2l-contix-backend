using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Tarefas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ultimo_numero_tarefa",
                table: "escritorios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "tarefas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descricao = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    departamento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    responsavel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prazo_interno = table.Column<DateOnly>(type: "date", nullable: false),
                    prazo_legal = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    concluida_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tarefas", x => x.id);
                    table.ForeignKey(
                        name: "fk_tarefas_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tarefas_escritorios_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "escritorios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tarefas_usuarios_criado_por_id",
                        column: x => x.criado_por_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tarefas_usuarios_responsavel_id",
                        column: x => x.responsavel_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tarefa_interacoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tarefa_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_tarefa_interacoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_tarefa_interacoes_tarefas_tarefa_id",
                        column: x => x.tarefa_id,
                        principalTable: "tarefas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tarefa_interacoes_usuarios_autor_id",
                        column: x => x.autor_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tarefa_anexos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tarefa_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_tarefa_anexos", x => x.id);
                    table.ForeignKey(
                        name: "fk_tarefa_anexos_tarefa_interacoes_interacao_id",
                        column: x => x.interacao_id,
                        principalTable: "tarefa_interacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tarefa_anexos_tarefas_tarefa_id",
                        column: x => x.tarefa_id,
                        principalTable: "tarefas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tarefa_anexos_usuarios_enviado_por_id",
                        column: x => x.enviado_por_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tarefa_anexos_enviado_por_id",
                table: "tarefa_anexos",
                column: "enviado_por_id");

            migrationBuilder.CreateIndex(
                name: "ix_tarefa_anexos_interacao_id",
                table: "tarefa_anexos",
                column: "interacao_id");

            migrationBuilder.CreateIndex(
                name: "ix_tarefa_anexos_tarefa_id",
                table: "tarefa_anexos",
                column: "tarefa_id");

            migrationBuilder.CreateIndex(
                name: "ix_tarefa_interacoes_autor_id",
                table: "tarefa_interacoes",
                column: "autor_id");

            migrationBuilder.CreateIndex(
                name: "ix_tarefa_interacoes_tarefa_id_created_at",
                table: "tarefa_interacoes",
                columns: new[] { "tarefa_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tarefas_criado_por_id",
                table: "tarefas",
                column: "criado_por_id");

            migrationBuilder.CreateIndex(
                name: "ix_tarefas_empresa_id",
                table: "tarefas",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_tarefas_responsavel_id",
                table: "tarefas",
                column: "responsavel_id");

            migrationBuilder.CreateIndex(
                name: "ix_tarefas_tenant_id_empresa_id_status",
                table: "tarefas",
                columns: new[] { "tenant_id", "empresa_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tarefas_tenant_id_numero",
                table: "tarefas",
                columns: new[] { "tenant_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tarefas_tenant_id_prazo_interno",
                table: "tarefas",
                columns: new[] { "tenant_id", "prazo_interno" });

            migrationBuilder.CreateIndex(
                name: "ix_tarefas_tenant_id_responsavel_id_status",
                table: "tarefas",
                columns: new[] { "tenant_id", "responsavel_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tarefa_anexos");

            migrationBuilder.DropTable(
                name: "tarefa_interacoes");

            migrationBuilder.DropTable(
                name: "tarefas");

            migrationBuilder.DropColumn(
                name: "ultimo_numero_tarefa",
                table: "escritorios");
        }
    }
}
