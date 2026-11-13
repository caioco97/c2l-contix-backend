using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizarMascaras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dados já gravados com máscara passam a ficar só com dígitos, como a API grava agora.
            foreach (var tabela in new[] { "empresas", "escritorios" })
            {
                migrationBuilder.Sql($"""
                    UPDATE {tabela} SET
                        endereco_cep = NULLIF(regexp_replace(endereco_cep, '\D', '', 'g'), ''),
                        endereco_uf = upper(trim(endereco_uf)),
                        telefone = NULLIF(
                            CASE WHEN length(regexp_replace(telefone, '\D', '', 'g')) IN (12, 13)
                                      AND regexp_replace(telefone, '\D', '', 'g') LIKE '55%'
                                 THEN substr(regexp_replace(telefone, '\D', '', 'g'), 3)
                                 ELSE regexp_replace(telefone, '\D', '', 'g') END, '');
                    """);
            }

            migrationBuilder.Sql("""
                UPDATE empregados SET telefone = NULLIF(
                    CASE WHEN length(regexp_replace(telefone, '\D', '', 'g')) IN (12, 13)
                              AND regexp_replace(telefone, '\D', '', 'g') LIKE '55%'
                         THEN substr(regexp_replace(telefone, '\D', '', 'g'), 3)
                         ELSE regexp_replace(telefone, '\D', '', 'g') END, '');
                """);

            migrationBuilder.Sql("""
                UPDATE empresas SET
                    inscricao_estadual = NULLIF(CASE
                        WHEN upper(regexp_replace(inscricao_estadual, '[^A-Za-z0-9]', '', 'g')) = 'ISENTO' THEN 'ISENTO'
                        WHEN upper(trim(inscricao_estadual)) LIKE 'P%'
                            THEN 'P' || regexp_replace(inscricao_estadual, '\D', '', 'g')
                        ELSE regexp_replace(inscricao_estadual, '\D', '', 'g') END, ''),
                    inscricao_municipal = NULLIF(regexp_replace(inscricao_municipal, '\D', '', 'g'), '');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Normalização de dados: não há como restaurar as máscaras originais.
        }
    }
}
