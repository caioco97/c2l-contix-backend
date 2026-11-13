using System.Text.Json;
using Contix.Application.Features.Solicitacoes;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;

namespace Contix.UnitTests.Application;

public class FormulariosSolicitacaoTests
{
    private static readonly Empregado Maria = new() { Nome = "Maria Souza" };
    private static readonly Empregado Joao = new() { Nome = "João Pereira" };
    private static readonly Empregado Desligado = new() { Nome = "Pedro", Situacao = SituacaoEmpregado.Desligado };
    private static readonly Dictionary<Guid, Empregado> Cadastro =
        new[] { Maria, Joao, Desligado }.ToDictionary(e => e.Id);

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
        // O nome digitado pelo cliente é ignorado: vale o do cadastro.
        var json = $$"""{"empregadoId":"{{Maria.Id}}","empregado":"Outro","periodoAquisitivoInicio":"2025-01-01","periodoAquisitivoFim":"2025-12-31","dataInicio":"2026-12-01","dias":20,"abonoPecuniario":true,"adiantamento13":false}""";
        var r = FormulariosSolicitacao.Processar(TipoSolicitacao.CalculoFerias, json, Cadastro);

        Assert.Equal("Cálculo de Férias - Maria Souza", r.AssuntoSugerido);
        Assert.Equal(Maria.Id, r.EmpregadoId);
        var dados = JsonDocument.Parse(r.DadosJson!).RootElement;
        Assert.Equal(20, dados.GetProperty("dias").GetInt32());
        Assert.Equal("Maria Souza", dados.GetProperty("empregado").GetString());
        Assert.Equal(Maria.Id, dados.GetProperty("empregadoId").GetGuid());
    }

    [Fact]
    public void Ferias_com_abono_limita_a_20_dias()
    {
        var json = $$"""{"empregadoId":"{{Maria.Id}}","periodoAquisitivoInicio":"2025-01-01","periodoAquisitivoFim":"2025-12-31","dataInicio":"2026-12-01","dias":30,"abonoPecuniario":true}""";
        Assert.Equal(["dados.dias"], Erros(() => FormulariosSolicitacao.Processar(TipoSolicitacao.AvisoFerias, json, Cadastro)));
    }

    [Fact]
    public void Empregado_precisa_ser_do_cadastro_da_empresa_e_nao_desligado()
    {
        const string semId = """{"empregado":"Maria","motivo":"Doenca","dataInicio":"2026-11-10"}""";
        var outraEmpresa = $$"""{"empregadoId":"{{Guid.NewGuid()}}","motivo":"Doenca","dataInicio":"2026-11-10"}""";
        var desligado = $$"""{"empregadoId":"{{Desligado.Id}}","motivo":"Doenca","dataInicio":"2026-11-10"}""";

        string Mensagem(string json) => Assert.Throws<ValidationException>(() =>
            FormulariosSolicitacao.Processar(TipoSolicitacao.Afastamento, json, Cadastro)).Errors.Single(e => e.PropertyName == "dados.empregadoId").ErrorMessage;

        Assert.Equal("Selecione o empregado.", Mensagem(semId));
        Assert.Equal("Selecione um empregado cadastrado nesta empresa.", Mensagem(outraEmpresa));
        Assert.Equal("Este empregado está desligado.", Mensagem(desligado));
    }

    [Fact]
    public void Empregados_referenciados_lidos_dos_dados()
    {
        var rubricas = $$"""{"lancamentos":[{"empregadoId":"{{Maria.Id}}"},{"empregadoId":"{{Joao.Id}}"},{"empregadoId":"x"},{}]}""";
        var ids = FormulariosSolicitacao.EmpregadosReferenciados(TipoSolicitacao.LancamentoRubricas, rubricas);
        Assert.Equal(2, ids.Count);
        Assert.Contains(Maria.Id, ids);
        Assert.Contains(Joao.Id, ids);
        Assert.Equal([Maria.Id], FormulariosSolicitacao.EmpregadosReferenciados(TipoSolicitacao.AvisoRescisao, $$"""{"empregadoId":"{{Maria.Id}}"}"""));
        Assert.Empty(FormulariosSolicitacao.EmpregadosReferenciados(TipoSolicitacao.Afastamento, "não é json"));
        Assert.Empty(FormulariosSolicitacao.EmpregadosReferenciados(TipoSolicitacao.CadastroEmpregado, $$"""{"empregadoId":"{{Maria.Id}}"}"""));
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
    public void Cadastro_de_empregado_guarda_documentos_e_contatos_sem_mascara()
    {
        const string json = """{"nome":"Ana","cpf":"529.982.247-25","pis":"120.69870.68-7","telefone":"+55 (11) 91234-5678","dataNascimento":"1990-01-01","cargo":"Analista","salario":3000,"jornada":"44h","dataAdmissao":"2026-12-01","endereco":{"cep":"01310-100","uf":"sp","cidade":" São Paulo "}}""";
        var r = FormulariosSolicitacao.Processar(TipoSolicitacao.CadastroEmpregado, json);

        var dados = JsonDocument.Parse(r.DadosJson!).RootElement;
        Assert.Equal("52998224725", dados.GetProperty("cpf").GetString());
        Assert.Equal("12069870687", dados.GetProperty("pis").GetString());
        Assert.Equal("11912345678", dados.GetProperty("telefone").GetString());
        var endereco = dados.GetProperty("endereco");
        Assert.Equal("01310100", endereco.GetProperty("cep").GetString());
        Assert.Equal("SP", endereco.GetProperty("uf").GetString());
        Assert.Equal("São Paulo", endereco.GetProperty("cidade").GetString());
    }

    [Fact]
    public void Cadastro_de_empregado_valida_pis_telefone_cep_e_uf()
    {
        const string json = """{"nome":"Ana","cpf":"529.982.247-25","pis":"120.69870.68-6","telefone":"(11) 81234-5678","dataNascimento":"1990-01-01","cargo":"Analista","salario":3000,"jornada":"44h","dataAdmissao":"2026-12-01","endereco":{"cep":"0131010","uf":"XX"}}""";
        var erros = Erros(() => FormulariosSolicitacao.Processar(TipoSolicitacao.CadastroEmpregado, json));
        Assert.Equal(["dados.pis", "dados.telefone", "dados.endereco.cep", "dados.endereco.uf"], erros);
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
        var json = $$"""{"competencia":"2026-11-01","lancamentos":[{"empregadoId":"{{Joao.Id}}","rubrica":"Horas extras 50%"},{"rubrica":"Faltas","quantidade":1}]}""";
        var erros = Erros(() => FormulariosSolicitacao.Processar(TipoSolicitacao.LancamentoRubricas, json, Cadastro));
        Assert.Equal(["dados.lancamentos[0].valor", "dados.lancamentos[1].empregadoId"], erros);
    }

    [Fact]
    public void Rubricas_gravam_o_nome_do_cadastro_em_cada_lancamento()
    {
        var json = $$"""{"competencia":"2026-11-01","lancamentos":[{"empregadoId":"{{Joao.Id}}","rubrica":"Faltas","quantidade":1},{"empregadoId":"{{Maria.Id}}","rubrica":"Comissões","valor":150}]}""";
        var r = FormulariosSolicitacao.Processar(TipoSolicitacao.LancamentoRubricas, json, Cadastro);
        var nomes = JsonDocument.Parse(r.DadosJson!).RootElement.GetProperty("lancamentos").EnumerateArray()
            .Select(l => l.GetProperty("empregado").GetString()).ToArray();
        Assert.Equal(["João Pereira", "Maria Souza"], nomes);
        Assert.Null(r.EmpregadoId);
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
        var json = $$"""{"empregadoId":"{{Joao.Id}}","motivo":"Ferias","dataInicio":"2026-11-10"}""";
        Assert.Equal(["dados"], Erros(() => FormulariosSolicitacao.Processar(TipoSolicitacao.Afastamento, json)));
    }
}
