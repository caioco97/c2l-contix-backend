using Contix.Application.Common.Models;
using Contix.Application.Common.Validation;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;

namespace Contix.Application.Features.Usuarios;

public sealed record UsuarioDto(
    Guid Id, string Nome, string Email, PerfilUsuario Perfil, bool Ativo,
    DateTime? UltimoLoginEm, DateTime CreatedAt, IReadOnlyList<Guid> EmpresaIds)
{
    public static UsuarioDto From(Usuario u) => new(
        u.Id, u.Nome, u.Email, u.Perfil, u.Ativo, u.UltimoLoginEm, u.CreatedAt,
        u.Empresas.Select(e => e.EmpresaId).ToList());
}

public sealed record UsuarioFiltro : PageQuery
{
    public string? Busca { get; init; }
    public PerfilUsuario? Perfil { get; init; }
    public bool? Ativo { get; init; }
    public Guid? EmpresaId { get; init; }
}

public sealed record CriarUsuarioRequest(
    string Nome, string Email, PerfilUsuario Perfil, string Senha, IReadOnlyList<Guid>? EmpresaIds);

public sealed record AtualizarUsuarioRequest(
    string Nome, PerfilUsuario Perfil, bool Ativo, IReadOnlyList<Guid>? EmpresaIds);

public sealed class CriarUsuarioRequestValidator : AbstractValidator<CriarUsuarioRequest>
{
    public CriarUsuarioRequestValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Perfil).IsInEnum();
        RuleFor(x => x.Senha).Senha();
        RuleFor(x => x.EmpresaIds).NotEmpty().When(x => x.Perfil == PerfilUsuario.Cliente)
            .WithMessage("Usuário do cliente deve estar vinculado a ao menos uma empresa.");
    }
}

public sealed class AtualizarUsuarioRequestValidator : AbstractValidator<AtualizarUsuarioRequest>
{
    public AtualizarUsuarioRequestValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Perfil).IsInEnum();
        RuleFor(x => x.EmpresaIds).NotEmpty().When(x => x.Perfil == PerfilUsuario.Cliente)
            .WithMessage("Usuário do cliente deve estar vinculado a ao menos uma empresa.");
    }
}
