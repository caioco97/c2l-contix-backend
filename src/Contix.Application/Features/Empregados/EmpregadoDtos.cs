using Contix.Application.Common.Validation;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Contix.Domain.ValueObjects;
using FluentValidation;

namespace Contix.Application.Features.Empregados;

public sealed record EmpregadoDto(
    Guid Id, Guid EmpresaId, string Nome, string? Cpf, string? Matricula, string? Cargo,
    VinculoEmpregado Vinculo, SituacaoEmpregado Situacao, DateOnly? DataAdmissao, DateOnly? DataDesligamento,
    string? Email, string? Telefone, Guid? SolicitacaoOrigemId, DateTime CreatedAt, DateTime? UpdatedAt)
{
    public static EmpregadoDto From(Empregado e) => new(
        e.Id, e.EmpresaId, e.Nome, e.Cpf, e.Matricula, e.Cargo, e.Vinculo, e.Situacao, e.DataAdmissao,
        e.DataDesligamento, e.Email, e.Telefone, e.SolicitacaoOrigemId, e.CreatedAt, e.UpdatedAt);
}

public sealed record EmpregadoFiltro
{
    /// <summary>Busca por nome, CPF ou matrícula.</summary>
    public string? Busca { get; init; }
    public SituacaoEmpregado? Situacao { get; init; }
    public VinculoEmpregado? Vinculo { get; init; }
    /// <summary>true: somente quem não está desligado (o que os formulários de solicitação oferecem).</summary>
    public bool? Ativos { get; init; }
}

public sealed record SalvarEmpregadoRequest(
    string Nome, string? Cpf, string? Matricula, string? Cargo, VinculoEmpregado Vinculo,
    SituacaoEmpregado Situacao, DateOnly? DataAdmissao, DateOnly? DataDesligamento,
    string? Email, string? Telefone);

public sealed class SalvarEmpregadoRequestValidator : AbstractValidator<SalvarEmpregadoRequest>
{
    public SalvarEmpregadoRequestValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().WithName("Nome").MaximumLength(150);
        RuleFor(x => x.Cpf).Must(DocumentoFiscal.CpfValido).When(x => !string.IsNullOrWhiteSpace(x.Cpf))
            .WithMessage("CPF inválido.");
        RuleFor(x => x.Matricula).MaximumLength(30);
        RuleFor(x => x.Cargo).MaximumLength(100);
        RuleFor(x => x.Vinculo).IsInEnum();
        RuleFor(x => x.Situacao).IsInEnum();
        RuleFor(x => x.DataDesligamento).NotNull().When(x => x.Situacao == SituacaoEmpregado.Desligado)
            .WithName("Data do desligamento");
        RuleFor(x => x.DataDesligamento).GreaterThanOrEqualTo(x => x.DataAdmissao)
            .When(x => x.DataAdmissao is not null && x.DataDesligamento is not null)
            .WithMessage("O desligamento deve ser igual ou posterior à admissão.");
        RuleFor(x => x.Email).EmailAddress().MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Telefone).MaximumLength(30).Telefone();
    }
}
