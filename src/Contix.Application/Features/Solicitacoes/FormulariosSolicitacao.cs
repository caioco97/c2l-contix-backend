using System.Text.Json;
using System.Text.Json.Serialization;
using Contix.Application.Common.Models;
using Contix.Application.Common.Validation;
using Contix.Domain.Entities;
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

/* Os formulários de férias, rescisão, afastamento e rubricas referenciam o empregado pelo cadastro da
   empresa (EmpregadoId). O nome (Empregado) é preenchido pela API a partir do cadastro e fica gravado
   para o histórico, mesmo que o cadastro mude depois. */

public sealed record DadosFerias(
    Guid EmpregadoId, string? Empregado, DateOnly PeriodoAquisitivoInicio, DateOnly PeriodoAquisitivoFim,
    DateOnly DataInicio, int Dias, bool AbonoPecuniario, bool Adiantamento13);

public enum TipoDesligamento { SemJustaCausa = 1, ComJustaCausa, PedidoDemissao, AcordoMutuo, TerminoContrato }
public enum TipoAvisoPrevio { Trabalhado = 1, Indenizado, Dispensado }

public sealed record DadosRescisao(
    Guid EmpregadoId, string? Empregado, TipoDesligamento TipoDesligamento, DateOnly DataDesligamento,
    TipoAvisoPrevio AvisoPrevio, string? Observacoes);

public enum MotivoAfastamento { Doenca = 1, AcidenteTrabalho, LicencaMaternidade, LicencaPaternidade, ServicoMilitar, Outros }

public sealed record DadosAfastamento(
    Guid EmpregadoId, string? Empregado, MotivoAfastamento Motivo, DateOnly DataInicio, DateOnly? PrevisaoRetorno, string? Observacoes);

public sealed record LancamentoRubrica(Guid EmpregadoId, string? Empregado, string Rubrica, decimal? Quantidade, decimal? Valor);

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

    /// <param name="EmpregadoId">Empregado a que a solicitação se refere (tipos de um único empregado).</param>
    public sealed record Resultado(string? DadosJson, string AssuntoSugerido, Guid? EmpregadoId = null);

    /// <summary>Tipos cujo formulário referencia um único empregado do cadastro.</summary>
    public static bool ReferenciaEmpregado(TipoSolicitacao tipo) => tipo is
        TipoSolicitacao.AvisoFerias or TipoSolicitacao.CalculoFerias or TipoSolicitacao.AvisoRescisao or
        TipoSolicitacao.CalculoRescisao or TipoSolicitacao.Afastamento;

    public static bool EhCadastroDePessoa(TipoSolicitacao tipo) => tipo is
        TipoSolicitacao.CadastroEmpregado or TipoSolicitacao.CadastroContribuinte or TipoSolicitacao.CadastroEstagiario;

    /// <summary>
    /// Ids de empregado citados nos dados, para o serviço carregar o cadastro antes de <see cref="Processar"/>.
    /// Tolerante a JSON inválido (a validação completa acontece em <see cref="Processar"/>).
    /// </summary>
    public static IReadOnlyCollection<Guid> EmpregadosReferenciados(TipoSolicitacao tipo, string? dadosJson)
    {
        if (string.IsNullOrWhiteSpace(dadosJson) || (!ReferenciaEmpregado(tipo) && tipo != TipoSolicitacao.LancamentoRubricas))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(dadosJson);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return [];
            var ids = new HashSet<Guid>();
            void Ler(JsonElement e)
            {
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("empregadoId", out var v)
                    && v.ValueKind == JsonValueKind.String && v.TryGetGuid(out var id)) ids.Add(id);
            }
            if (tipo == TipoSolicitacao.LancamentoRubricas)
            {
                if (raiz.TryGetProperty("lancamentos", out var l) && l.ValueKind == JsonValueKind.Array)
                    foreach (var item in l.EnumerateArray().Take(500)) Ler(item);
            }
            else Ler(raiz);
            return ids;
        }
        catch (JsonException) { return []; }
    }

    /// <param name="empregados">Cadastro dos empregados citados (de <see cref="EmpregadosReferenciados"/>), da empresa da solicitação.</param>
    public static Resultado Processar(TipoSolicitacao tipo, string? dadosJson, IReadOnlyDictionary<Guid, Empregado>? empregados = null)
    {
        var nome = CatalogoSolicitacoes.Obter(tipo).Nome;
        var cadastro = empregados ?? new Dictionary<Guid, Empregado>();
        string NomeDe(Guid id) => cadastro[id].Nome;

        return tipo switch
        {
            TipoSolicitacao.Geral => new Resultado(null, nome),
            TipoSolicitacao.CadastroEmpregado or TipoSolicitacao.CadastroContribuinte or TipoSolicitacao.CadastroEstagiario =>
                Validar(dadosJson, ValidadorCadastro(tipo), d => d, d => $"{nome} - {d.Nome.Trim()}"),
            TipoSolicitacao.AvisoFerias or TipoSolicitacao.CalculoFerias =>
                Validar(dadosJson, ValidadorFerias(cadastro), d => d with { Empregado = NomeDe(d.EmpregadoId) },
                    d => $"{nome} - {d.Empregado}", d => d.EmpregadoId),
            TipoSolicitacao.AvisoRescisao or TipoSolicitacao.CalculoRescisao =>
                Validar(dadosJson, ValidadorRescisao(cadastro), d => d with { Empregado = NomeDe(d.EmpregadoId) },
                    d => $"{nome} - {d.Empregado}", d => d.EmpregadoId),
            TipoSolicitacao.Afastamento =>
                Validar(dadosJson, ValidadorAfastamento(cadastro), d => d with { Empregado = NomeDe(d.EmpregadoId) },
                    d => $"{nome} - {d.Empregado}", d => d.EmpregadoId),
            TipoSolicitacao.LancamentoRubricas =>
                Validar(dadosJson, ValidadorRubricas(cadastro),
                    d => d with { Lancamentos = d.Lancamentos.Select(l => l with { Empregado = NomeDe(l.EmpregadoId) }).ToList() },
                    d => $"{nome} - {d.Competencia:MM/yyyy}"),
            _ => throw new ValidationException([new ValidationFailure("tipo", "Tipo de solicitação inválido.")])
        };
    }

    /// <summary>Lê os dados gravados de uma solicitação (já validados na abertura).</summary>
    public static T? Ler<T>(string? dadosJson) where T : class
    {
        if (string.IsNullOrWhiteSpace(dadosJson)) return null;
        try { return JsonSerializer.Deserialize<T>(dadosJson, Json); }
        catch (JsonException) { return null; }
    }

    private static Resultado Validar<T>(
        string? json, IValidator<T> validador, Func<T, T> normalizar, Func<T, string> assunto,
        Func<T, Guid>? empregado = null) where T : class
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

        dados = normalizar(dados);
        return new Resultado(JsonSerializer.Serialize(dados, Json), Limitar(assunto(dados), 200), empregado?.Invoke(dados));
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

    /// <summary>O empregado precisa estar no cadastro da empresa e não pode estar desligado.</summary>
    private static void RegraEmpregado<T>(AbstractValidator<T> v, System.Linq.Expressions.Expression<Func<T, Guid>> campo,
        IReadOnlyDictionary<Guid, Empregado> cadastro)
    {
        v.RuleFor(campo).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Selecione o empregado.")
            .Must(cadastro.ContainsKey).WithMessage("Selecione um empregado cadastrado nesta empresa.")
            .Must(id => !cadastro[id].EstaDesligado).WithMessage("Este empregado está desligado.");
    }

    private static InlineValidator<DadosFerias> ValidadorFerias(IReadOnlyDictionary<Guid, Empregado> cadastro)
    {
        var v = new InlineValidator<DadosFerias>();
        RegraEmpregado(v, x => x.EmpregadoId, cadastro);
        v.RuleFor(x => x.PeriodoAquisitivoInicio).NotEmpty().WithName("Início do período aquisitivo");
        v.RuleFor(x => x.PeriodoAquisitivoFim).NotEmpty().WithName("Fim do período aquisitivo")
            .GreaterThan(x => x.PeriodoAquisitivoInicio).WithMessage("O fim do período aquisitivo deve ser após o início.");
        v.RuleFor(x => x.DataInicio).NotEmpty().WithName("Início das férias");
        v.RuleFor(x => x.Dias).InclusiveBetween(5, 30).WithMessage("As férias devem ter entre 5 e 30 dias.");
        v.RuleFor(x => x.Dias).LessThanOrEqualTo(20).When(x => x.AbonoPecuniario)
            .WithMessage("Com abono pecuniário, o período de gozo é de no máximo 20 dias.");
        return v;
    }

    private static InlineValidator<DadosRescisao> ValidadorRescisao(IReadOnlyDictionary<Guid, Empregado> cadastro)
    {
        var v = new InlineValidator<DadosRescisao>();
        RegraEmpregado(v, x => x.EmpregadoId, cadastro);
        v.RuleFor(x => x.TipoDesligamento).IsInEnum().NotEmpty().WithName("Tipo de desligamento");
        v.RuleFor(x => x.DataDesligamento).NotEmpty().WithName("Data do desligamento");
        v.RuleFor(x => x.AvisoPrevio).IsInEnum().NotEmpty().WithName("Aviso prévio");
        v.RuleFor(x => x.Observacoes).MaximumLength(2000);
        return v;
    }

    private static InlineValidator<DadosAfastamento> ValidadorAfastamento(IReadOnlyDictionary<Guid, Empregado> cadastro)
    {
        var v = new InlineValidator<DadosAfastamento>();
        RegraEmpregado(v, x => x.EmpregadoId, cadastro);
        v.RuleFor(x => x.Motivo).IsInEnum().NotEmpty().WithName("Motivo");
        v.RuleFor(x => x.DataInicio).NotEmpty().WithName("Início do afastamento");
        v.RuleFor(x => x.PrevisaoRetorno).GreaterThanOrEqualTo(x => x.DataInicio).When(x => x.PrevisaoRetorno is not null)
            .WithMessage("A previsão de retorno deve ser igual ou posterior ao início.");
        v.RuleFor(x => x.Observacoes).MaximumLength(2000);
        return v;
    }

    private static InlineValidator<DadosRubricas> ValidadorRubricas(IReadOnlyDictionary<Guid, Empregado> cadastro)
    {
        var v = new InlineValidator<DadosRubricas>();
        v.RuleFor(x => x.Competencia).NotEmpty().WithName("Competência");
        v.RuleFor(x => x.Lancamentos).NotEmpty().WithMessage("Informe ao menos um lançamento.")
            .Must(l => l.Count <= 500).WithMessage("Informe no máximo 500 lançamentos por solicitação.");
        v.RuleForEach(x => x.Lancamentos).ChildRules(l =>
        {
            l.RuleFor(x => x.EmpregadoId).Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("Selecione o empregado.")
                .Must(cadastro.ContainsKey).WithMessage("Selecione um empregado cadastrado nesta empresa.")
                .Must(id => !cadastro[id].EstaDesligado).WithMessage("Este empregado está desligado.");
            l.RuleFor(x => x.Rubrica).NotEmpty().WithName("Rubrica").MaximumLength(100);
            l.RuleFor(x => x).Must(x => x.Quantidade is > 0 || x.Valor is > 0).OverridePropertyName("valor")
                .WithMessage("Informe a quantidade ou o valor.");
        });
        return v;
    }
}
