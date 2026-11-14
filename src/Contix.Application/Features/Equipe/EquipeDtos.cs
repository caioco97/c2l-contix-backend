using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;

namespace Contix.Application.Features.Equipe;

/// <param name="Colaboradores">Usuários ativos do escritório que atuam no departamento.</param>
public sealed record DepartamentoDto(Departamento Codigo, string Nome, bool Ativo, int Colaboradores);

public sealed record AtualizarDepartamentoRequest(string Nome, bool Ativo);

/// <summary>Responsável pela empresa em um departamento (sem responsável: <c>UsuarioId</c> nulo).</summary>
public sealed record ResponsavelEmpresaDto(Departamento Departamento, string DepartamentoNome, bool DepartamentoAtivo, Guid? UsuarioId, string? UsuarioNome);

public sealed record ResponsavelItem(Departamento Departamento, Guid? UsuarioId);

/// <summary>Define os responsáveis da empresa. Departamentos não informados ficam como estão; <c>UsuarioId</c> nulo remove.</summary>
public sealed record DefinirResponsaveisRequest(IReadOnlyList<ResponsavelItem> Responsaveis);

public sealed class AtualizarDepartamentoRequestValidator : AbstractValidator<AtualizarDepartamentoRequest>
{
    public AtualizarDepartamentoRequestValidator() =>
        RuleFor(x => x.Nome).NotEmpty().MaximumLength(80).WithName("Nome");
}

public sealed class DefinirResponsaveisRequestValidator : AbstractValidator<DefinirResponsaveisRequest>
{
    public DefinirResponsaveisRequestValidator()
    {
        RuleFor(x => x.Responsaveis).NotNull();
        RuleForEach(x => x.Responsaveis).ChildRules(r => r.RuleFor(i => i.Departamento).IsInEnum());
        RuleFor(x => x.Responsaveis).Must(r => r.Select(i => i.Departamento).Distinct().Count() == r.Count)
            .WithMessage("Informe cada departamento uma única vez.");
    }
}

/// <summary>Nomes dos departamentos do escritório, já com os personalizados.</summary>
public sealed class NomesDepartamentos(IReadOnlyDictionary<Departamento, DepartamentoEscritorio> personalizados)
{
    public string Nome(Departamento d) =>
        personalizados.TryGetValue(d, out var p) ? p.Nome : DepartamentoEscritorio.NomePadrao(d);

    public bool Ativo(Departamento d) => !personalizados.TryGetValue(d, out var p) || p.Ativo;
}
