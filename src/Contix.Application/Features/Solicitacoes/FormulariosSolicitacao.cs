using System.Text.Json;
using System.Text.Json.Serialization;
using Contix.Application.Common.Models;
using Contix.Application.Common.Validation;
using Contix.Domain.Enums;
using Contix.Domain.ValueObjects;
using FluentValidation;
using FluentValidation.Results;

namespace Contix.Application.Features.Solicitacoes;

/* ---------- Campos específicos por tipo de solicitação (trafegam em "dados") ---------- */

/// <summary>Cadastro de empregado, contribuinte (sócio/autônomo) ou estagiário.</summary>
public sealed record DadosCadastroPessoa(
    string Nome, string Cpf, string? Rg, string? Pis, DateOnly DataNascimento,
    string? Email, string? Telefone, EnderecoDto? Endereco,
    string? Cargo, decimal? Salario, string? Jornada, DateOnly? DataAdmissao,
    string? InstituicaoEnsino, string? Curso, DateOnly? DataTerminoEstagio);

public sealed record DadosFerias(
    string Empregado, DateOnly PeriodoAquisitivoInicio, DateOnly PeriodoAquisitivoFim,
    DateOnly DataInicio, int Dias, bool AbonoPecuniario, bool Adiantamento13);

public enum TipoDesligamento { SemJustaCausa = 1, ComJustaCausa, PedidoDemissao, AcordoMutuo, TerminoContrato }
public enum TipoAvisoPrevio { Trabalhado = 1, Indenizado, Dispensado }

public sealed record DadosRescisao(
    string Empregado, TipoDesligamento TipoDesligamento, DateOnly DataDesligamento,
    TipoAvisoPrevio AvisoPrevio, string? Observacoes);

public enum MotivoAfastamento { Doenca = 1, AcidenteTrabalho, LicencaMaternidade, LicencaPaternidade, ServicoMilitar, Outros }

public sealed record DadosAfastamento(
    string Empregado, MotivoAfastamento Motivo, DateOnly DataInicio, DateOnly? PrevisaoRetorno, string? Observacoes);

public sealed record LancamentoRubrica(string Empregado, string Rubrica, decimal? Quantidade, decimal? Valor);

public sealed record DadosRubricas(DateOnly Competencia, IReadOnlyList<LancamentoRubrica> Lancamentos);

/// <summary>
/// Valida e normaliza os campos específicos de cada tipo. Os erros voltam com o prefixo
/// "dados." (ex.: "dados.cpf") para o front associar ao campo do formulário.
/// </summary>
public static class FormulariosSolicitacao
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public sealed record Resultado(string? DadosJson, string AssuntoSugerido);

    public static Resultado Processar(TipoSolicitacao tipo, string? dadosJson)
    {
        var nome = CatalogoSolicitacoes.Obter(tipo).Nome;
        return tipo switch
        {
            TipoSolicitacao.Geral => new Resultado(null, nome),
            TipoSolicitacao.CadastroEmpregado or TipoSolicitacao.CadastroContribuinte or TipoSolicitacao.CadastroEstagiario =>
                Validar(dadosJson, ValidadorCadastro(tipo), d => $"{nome} - {d.Nome.Trim()}"),
            TipoSolicitacao.AvisoFerias or TipoSolicitacao.CalculoFerias =>
                Validar(dadosJson, ValidadorFerias(), d => $"{nome} - {d.Empregado.Trim()}"),
            TipoSolicitacao.AvisoRescisao or TipoSolicitacao.CalculoRescisao =>
                Validar(dadosJson, ValidadorRescisao(), d => $"{nome} - {d.Empregado.Trim()}"),
            TipoSolicitacao.Afastamento =>
                Validar(dadosJson, ValidadorAfastamento(), d => $"{nome} - {d.Empregado.Trim()}"),
            TipoSolicitacao.LancamentoRubricas =>
                Validar(dadosJson, ValidadorRubricas(), d => $"{nome} - {d.Competencia:MM/yyyy}"),
            _ => throw new ValidationException([new ValidationFailure("tipo", "Tipo de solicitação inválido.")])
        };
    }

    private static Resultado Validar<T>(string? json, IValidator<T> validador, Func<T, string> assunto) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ValidationException([new ValidationFailure("dados", "Preencha os dados do formulário.")]);

        T? dados;
        try { dados = JsonSerializer.Deserialize<T>(json, Json); }
        catch (JsonException)
        {
            throw new ValidationException([new ValidationFailure("dados", "Os dados do formulário estão em um formato inválido.")]);
        }
        if (dados is null)
            throw new ValidationException([new ValidationFailure("dados", "Preencha os dados do formulário.")]);

        var resultado = validador.Validate(dados);
        if (!resultado.IsValid)
            throw new ValidationException(resultado.Errors.Select(e =>
                new ValidationFailure("dados." + CamelCase(e.PropertyName), e.ErrorMessage)));

        return new Resultado(JsonSerializer.Serialize(dados, Json), Limitar(assunto(dados), 200));
    }

    private static string CamelCase(string propriedade) =>
        string.Join('.', propriedade.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));

    private static string Limitar(string texto, int max) => texto.Length <= max ? texto : texto[..max];

    private static InlineValidator<DadosCadastroPessoa> ValidadorCadastro(TipoSolicitacao tipo)
    {
        var v = new InlineValidator<DadosCadastroPessoa>();
        v.RuleFor(x => x.Nome).NotEmpty().WithName("Nome").MaximumLength(150);
        v.RuleFor(x => x.Cpf).NotEmpty().WithName("CPF").Must(DocumentoFiscal.CpfValido).WithMessage("CPF inválido.");
        v.RuleFor(x => x.Pis).Matches(@"^\d{3}\.?\d{5}\.?\d{2}-?\d$").When(x => !string.IsNullOrWhiteSpace(x.Pis))
            .WithMessage("PIS inválido.");
        v.RuleFor(x => x.DataNascimento).NotEmpty().WithName("Data de nascimento");
        v.RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        v.RuleFor(x => x.Endereco!).SetValidator(new EnderecoValidator()).When(x => x.Endereco is not null);
        v.RuleFor(x => x.Cargo).NotEmpty().WithName(tipo == TipoSolicitacao.CadastroContribuinte ? "Função" : "Cargo").MaximumLength(100);
        v.RuleFor(x => x.Salario).NotNull().GreaterThan(0)
            .WithName(tipo switch
            {
                TipoSolicitacao.CadastroContribuinte => "Valor do pró-labore/serviço",
                TipoSolicitacao.CadastroEstagiario => "Valor da bolsa",
                _ => "Salário"
            });
        v.RuleFor(x => x.DataAdmissao).NotNull()
            .WithName(tipo == TipoSolicitacao.CadastroEmpregado ? "Data de admissão" : "Data de início");

        if (tipo != TipoSolicitacao.CadastroContribuinte)
            v.RuleFor(x => x.Jornada).NotEmpty().WithName("Jornada").MaximumLength(100);

        if (tipo == TipoSolicitacao.CadastroEstagiario)
        {
            v.RuleFor(x => x.InstituicaoEnsino).NotEmpty().WithName("Instituição de ensino").MaximumLength(150);
            v.RuleFor(x => x.Curso).NotEmpty().WithName("Curso").MaximumLength(150);
            v.RuleFor(x => x.DataTerminoEstagio).NotNull().WithName("Término do estágio")
                .GreaterThan(x => x.DataAdmissao).When(x => x.DataAdmissao is not null)
                .WithMessage("O término deve ser posterior ao início.");
        }
        return v;
    }

    private static InlineValidator<DadosFerias> ValidadorFerias()
    {
        var v = new InlineValidator<DadosFerias>();
        v.RuleFor(x => x.Empregado).NotEmpty().WithName("Empregado").MaximumLength(150);
        v.RuleFor(x => x.PeriodoAquisitivoInicio).NotEmpty().WithName("Início do período aquisitivo");
        v.RuleFor(x => x.PeriodoAquisitivoFim).NotEmpty().WithName("Fim do período aquisitivo")
            .GreaterThan(x => x.PeriodoAquisitivoInicio).WithMessage("O fim do período aquisitivo deve ser após o início.");
        v.RuleFor(x => x.DataInicio).NotEmpty().WithName("Início das férias");
        v.RuleFor(x => x.Dias).InclusiveBetween(5, 30).WithMessage("As férias devem ter entre 5 e 30 dias.");
        v.RuleFor(x => x.Dias).LessThanOrEqualTo(20).When(x => x.AbonoPecuniario)
            .WithMessage("Com abono pecuniário, o período de gozo é de no máximo 20 dias.");
        return v;
    }

    private static InlineValidator<DadosRescisao> ValidadorRescisao()
    {
        var v = new InlineValidator<DadosRescisao>();
        v.RuleFor(x => x.Empregado).NotEmpty().WithName("Empregado").MaximumLength(150);
        v.RuleFor(x => x.TipoDesligamento).IsInEnum().NotEmpty().WithName("Tipo de desligamento");
        v.RuleFor(x => x.DataDesligamento).NotEmpty().WithName("Data do desligamento");
        v.RuleFor(x => x.AvisoPrevio).IsInEnum().NotEmpty().WithName("Aviso prévio");
        v.RuleFor(x => x.Observacoes).MaximumLength(2000);
        return v;
    }

    private static InlineValidator<DadosAfastamento> ValidadorAfastamento()
    {
        var v = new InlineValidator<DadosAfastamento>();
        v.RuleFor(x => x.Empregado).NotEmpty().WithName("Empregado").MaximumLength(150);
        v.RuleFor(x => x.Motivo).IsInEnum().NotEmpty().WithName("Motivo");
        v.RuleFor(x => x.DataInicio).NotEmpty().WithName("Início do afastamento");
        v.RuleFor(x => x.PrevisaoRetorno).GreaterThanOrEqualTo(x => x.DataInicio).When(x => x.PrevisaoRetorno is not null)
            .WithMessage("A previsão de retorno deve ser igual ou posterior ao início.");
        v.RuleFor(x => x.Observacoes).MaximumLength(2000);
        return v;
    }

    private static InlineValidator<DadosRubricas> ValidadorRubricas()
    {
        var v = new InlineValidator<DadosRubricas>();
        v.RuleFor(x => x.Competencia).NotEmpty().WithName("Competência");
        v.RuleFor(x => x.Lancamentos).NotEmpty().WithMessage("Informe ao menos um lançamento.")
            .Must(l => l.Count <= 500).WithMessage("Informe no máximo 500 lançamentos por solicitação.");
        v.RuleForEach(x => x.Lancamentos).ChildRules(l =>
        {
            l.RuleFor(x => x.Empregado).NotEmpty().WithName("Empregado").MaximumLength(150);
            l.RuleFor(x => x.Rubrica).NotEmpty().WithName("Rubrica").MaximumLength(100);
            l.RuleFor(x => x).Must(x => x.Quantidade is > 0 || x.Valor is > 0).OverridePropertyName("valor")
                .WithMessage("Informe a quantidade ou o valor.");
        });
        return v;
    }
}
