using System.Text.Json;
using Contix.Application.Features.Solicitacoes;
using Contix.Domain.Enums;
using FluentValidation;

namespace Contix.UnitTests.Application;

public class FormulariosSolicitacaoTests
{
    private static string[] Erros(Action acao) =>
        Assert.Throws<ValidationException>(acao).Errors.Select(e => e.PropertyName).Distinct().ToArray();

    [Fact]
    public void Geral_nao_tem_dados()
    {
        var r = FormulariosSolicitacao.Processar(TipoSolicitacao.Geral, "{\"x\":1}");
        Assert.Null(r.DadosJson);
    }

    [Fact]
    public void Ferias_valida_campos_e_sugere_assunto()
    {
        const string json = """{"empregado":"Maria","periodoAquisitivoInicio":"2025-01-01","periodoAquisitivoFim":"2025-12-31","dataInicio":"2026-12-01","dias":20,"abonoPecuniario":true,"adiantamento13":false}""";
        var r = FormulariosSolicitacao.Processar(TipoSolicitacao.CalculoFerias, json);

        Assert.Equal("Cálculo de Férias - Maria", r.AssuntoSugerido);
        Assert.Equal(20, JsonDocument.Parse(r.DadosJson!).RootElement.GetProperty("dias").GetInt32());
    }

    [Fact]
    public void Ferias_com_abono_limita_a_20_dias()
    {
        const string json = """{"empregado":"Maria","periodoAquisitivoInicio":"2025-01-01","periodoAquisitivoFim":"2025-12-31","dataInicio":"2026-12-01","dias":30,"abonoPecuniario":true}""";
        Assert.Equal(["dados.dias"], Erros(() => FormulariosSolicitacao.Processar(TipoSolicitacao.AvisoFerias, json)));
    }

    [Fact]
    public void Cadastro_de_empregado_valida_cpf_e_obrigatorios()
    {
        const string json = """{"nome":"João","cpf":"111.111.111-11","dataNascimento":"1990-01-01"}""";
        var erros = Erros(() => FormulariosSolicitacao.Processar(TipoSolicitacao.CadastroEmpregado, json));
        Assert.Contains("dados.cpf", erros);
        Assert.Contains("dados.cargo", erros);
        Assert.Contains("dados.salario", erros);
        Assert.Contains("dados.jornada", erros);
        Assert.Contains("dados.dataAdmissao", erros);
        Assert.DoesNotContain("dados.curso", erros);
    }

    [Fact]
    public void Cadastro_de_estagiario_exige_dados_do_curso()
    {
        const string json = """{"nome":"Ana","cpf":"529.982.247-25","dataNascimento":"2004-01-01","cargo":"Estagiária","salario":1200,"jornada":"6h","dataAdmissao":"2026-12-01"}""";
        var erros = Erros(() => FormulariosSolicitacao.Processar(TipoSolicitacao.CadastroEstagiario, json));
        Assert.Equal(["dados.instituicaoEnsino", "dados.curso", "dados.dataTerminoEstagio"], erros);
    }

    [Fact]
    public void Rubricas_exigem_quantidade_ou_valor()
    {
        const string json = """{"competencia":"2026-11-01","lancamentos":[{"empregado":"João","rubrica":"Horas extras 50%"}]}""";
        var erros = Erros(() => FormulariosSolicitacao.Processar(TipoSolicitacao.LancamentoRubricas, json));
        Assert.Equal(["dados.lancamentos[0].valor"], erros);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("não é json")]
    public void Dados_ausentes_ou_invalidos(string? json)
    {
        Assert.Equal(["dados"], Erros(() => FormulariosSolicitacao.Processar(TipoSolicitacao.Afastamento, json)));
    }

    [Fact]
    public void Afastamento_com_enum_invalido_retorna_erro_de_formato()
    {
        const string json = """{"empregado":"João","motivo":"Ferias","dataInicio":"2026-11-10"}""";
        Assert.Equal(["dados"], Erros(() => FormulariosSolicitacao.Processar(TipoSolicitacao.Afastamento, json)));
    }
}
