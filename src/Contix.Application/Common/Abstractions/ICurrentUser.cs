using Contix.Domain.Enums;

namespace Contix.Application.Common.Abstractions;

/// <summary>Usuário autenticado da requisição atual.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? TenantId { get; }
    PerfilUsuario? Perfil { get; }
    string? IpAddress { get; }

    Guid RequireUserId() => UserId ?? throw new Exceptions.UnauthorizedException();
    Guid RequireTenantId() => TenantId ?? throw new Exceptions.UnauthorizedException();
    bool EhCliente => Perfil == PerfilUsuario.Cliente;
    bool EhAdministrador => Perfil == PerfilUsuario.Administrador;
}
