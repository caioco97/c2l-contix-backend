using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;

namespace Contix.Application.Features.Obrigacoes;

public sealed record ObrigacaoDto(
    Guid Id, string Titulo, string? Descricao, Departamento Departamento, PeriodicidadeObrigacao Periodicidade,
    int DiaVencimento, int MesesAposCompetencia, int MesReferencia, int DiasAntecedencia,
    IReadOnlyList<RegimeTributario> Regimes, Guid? ResponsavelPadraoId, string? ResponsavelPadraoNome, bool Ativa,
    DateOnly? ProximaCompetencia, DateOnly? ProximoVencimento, int TarefasGeradas, DateTime CreatedAt);

/// <param name="Regimes">Regimes tributários a que se aplica; vazio ou nulo: todas as empresas ativas.</param>
/// <param name="ResponsavelPadraoId">Usado quando a empresa não tem responsável no departamento.</param>
public sealed record SalvarObrigacaoRequest(
    string Titulo, string? Descricao, Departamento Departamento, PeriodicidadeObrigacao Periodicidade,
    int DiaVencimento, int MesesAposCompetencia, int MesReferencia, int DiasAntecedencia,
    IReadOnlyList<RegimeTributario>? Regimes, Guid? ResponsavelPadraoId, bool Ativa = true);

/// <param name="Competencia">Qualquer dia do mês de competência.</param>
public sealed record GerarTarefasRequest(DateOnly Competencia);

/// <param name="SemResponsavel">Empresas aplicáveis sem responsável no departamento e sem responsável padrão (nenhuma tarefa criada).</param>
public sealed record GeracaoTarefasDto(DateOnly Competencia, DateOnly Vencimento, int EmpresasAplicaveis, int Geradas, int JaExistentes, int SemResponsavel);

/// <summary>Dados mínimos de uma empresa para a geração das tarefas.</summary>
public sealed record EmpresaParaObrigacao(Guid Id, string Nome, RegimeTributario Regime);

public sealed class SalvarObrigacaoRequestValidator : AbstractValidator<SalvarObrigacaoRequest>
{
    public SalvarObrigacaoRequestValidator()
    {
        RuleFor(x => x.Titulo).NotEmpty().MaximumLength(200).WithName("Título");
        RuleFor(x => x.Descricao).MaximumLength(5000).WithName("Descrição");
        RuleFor(x => x.Departamento).IsInEnum();
        RuleFor(x => x.Periodicidade).IsInEnum();
        RuleFor(x => x.DiaVencimento).InclusiveBetween(1, 31).WithName("Dia de vencimento");
        RuleFor(x => x.MesesAposCompetencia).InclusiveBetween(0, 12).WithName("Meses após a competência");
        RuleFor(x => x.MesReferencia).InclusiveBetween(1, 12).WithName("Mês de referência");
        RuleFor(x => x.DiasAntecedencia).InclusiveBetween(0, ObrigacaoRecorrente.MaxDiasAntecedencia).WithName("Antecedência");
        RuleForEach(x => x.Regimes).IsInEnum();
    }
}
