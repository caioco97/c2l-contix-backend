using Contix.Application.Common.Models;
using Contix.Application.Common.Validation;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;

namespace Contix.Application.Features.Usuarios;

public sealed record UsuarioDto(
    Guid Id, string Nome, string Email, PerfilUsuario Perfil, bool Ativo,
    DateTime? UltimoLoginEm, DateTime CreatedAt, IReadOnlyList<Guid> EmpresaIds,
    bool ConvitePendente, DateTime? ConviteEnviadoEm)
{
    public static UsuarioDto From(Usuario u) => new(
        u.Id, u.Nome, u.Email, u.Perfil, u.Ativo, u.UltimoLoginEm, u.CreatedAt,
        u.Empresas.Select(e => e.EmpresaId).ToList(), u.ConvitePendente, u.ConviteEnviadoEm);
}

/// <summary>Usuário do portal do cliente no contexto de uma empresa, com as permissões nela.</summary>
public sealed record UsuarioPortalDto(
    Guid Id, string Nome, string Email, bool Ativo, bool ConvitePendente, DateTime? ConviteEnviadoEm,
    DateTime? UltimoLoginEm, DateTime VinculadoEm,
    IReadOnlyList<ModuloPortal> Modulos, bool TodasAsPastas, IReadOnlyList<Guid> PastaIds,
    // Quantas outras empresas o usuário acessa.
    int OutrasEmpresas)
{
    public static UsuarioPortalDto From(Usuario u, UsuarioEmpresa v) => new(
        u.Id, u.Nome, u.Email, u.Ativo, u.ConvitePendente, u.ConviteEnviadoEm, u.UltimoLoginEm, v.VinculadoEm,
        v.Modulos, v.TodasAsPastas, v.PastaIds, u.Empresas.Count(e => e.EmpresaId != v.EmpresaId));
}

/// <summary>Permissões de um usuário do cliente em uma empresa.</summary>
public record PermissoesPortalRequest(IReadOnlyList<ModuloPortal> Modulos, bool TodasAsPastas, IReadOnlyList<Guid>? PastaIds);

/// <summary>
/// Convida um usuário para o portal da empresa. Se já existir um usuário do cliente com o e-mail
/// neste escritório, ele é vinculado à empresa (sem novo convite).
/// </summary>
public sealed record ConvidarUsuarioPortalRequest(
    string Nome, string Email, IReadOnlyList<ModuloPortal> Modulos, bool TodasAsPastas, IReadOnlyList<Guid>? PastaIds)
    : PermissoesPortalRequest(Modulos, TodasAsPastas, PastaIds);

public sealed record AlterarSituacaoUsuarioRequest(bool Ativo);

public sealed record UsuarioFiltro : PageQuery
{
    public string? Busca { get; init; }
    public PerfilUsuario? Perfil { get; init; }
    public bool? Ativo { get; init; }
    public Guid? EmpresaId { get; init; }
}

/// <param name="Senha">Opcional: sem senha, o usuário recebe um convite por e-mail para criar a própria.</param>
public sealed record CriarUsuarioRequest(
    string Nome, string Email, PerfilUsuario Perfil, string? Senha, IReadOnlyList<Guid>? EmpresaIds);

public sealed record AtualizarUsuarioRequest(
    string Nome, PerfilUsuario Perfil, bool Ativo, IReadOnlyList<Guid>? EmpresaIds);

public sealed class CriarUsuarioRequestValidator : AbstractValidator<CriarUsuarioRequest>
{
    public CriarUsuarioRequestValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Perfil).IsInEnum();
        RuleFor(x => x.Senha!).Senha().When(x => !string.IsNullOrEmpty(x.Senha));
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

internal static class PermissoesPortalRules
{
    public static void Aplicar<T>(AbstractValidator<T> v) where T : PermissoesPortalRequest
    {
        v.RuleFor(x => x.Modulos).NotNull();
        v.RuleForEach(x => x.Modulos).IsInEnum();
        v.RuleFor(x => x.PastaIds).NotEmpty().When(x => !x.TodasAsPastas && x.Modulos?.Contains(ModuloPortal.Documentos) == true)
            .WithMessage("Selecione ao menos uma pasta ou libere todas as pastas.");
    }
}

public sealed class PermissoesPortalRequestValidator : AbstractValidator<PermissoesPortalRequest>
{
    public PermissoesPortalRequestValidator() => PermissoesPortalRules.Aplicar(this);
}

public sealed class ConvidarUsuarioPortalRequestValidator : AbstractValidator<ConvidarUsuarioPortalRequest>
{
    public ConvidarUsuarioPortalRequestValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        PermissoesPortalRules.Aplicar(this);
    }
}
