using System.IdentityModel.Tokens.Jwt;
using Contix.Application.Common.Abstractions;
using Contix.Domain.Enums;
using Contix.Infrastructure.Security;

namespace Contix.Api.Infrastructure;

/// <summary>Lê o usuário autenticado a partir das claims do JWT.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => ParseGuid(JwtRegisteredClaimNames.Sub);

    public Guid? TenantId => ParseGuid(ContixClaims.TenantId);

    public PerfilUsuario? Perfil =>
        Enum.TryParse<PerfilUsuario>(Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, out var p) ? p : null;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private Guid? ParseGuid(string claim) =>
        IsAuthenticated && Guid.TryParse(Principal!.FindFirst(claim)?.Value, out var id) ? id : null;
}
