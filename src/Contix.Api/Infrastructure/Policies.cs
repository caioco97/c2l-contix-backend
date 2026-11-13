using Contix.Domain.Enums;

namespace Contix.Api.Infrastructure;

public static class Policies
{
    /// <summary>Administradores e colaboradores do escritório.</summary>
    public const string Escritorio = nameof(Escritorio);
    /// <summary>Somente administradores do escritório.</summary>
    public const string Administrador = nameof(Administrador);
    /// <summary>Somente usuários do cliente (portal). O escritório não abre solicitações em nome da empresa.</summary>
    public const string Cliente = nameof(Cliente);

    public static readonly string[] RolesEscritorio =
        [nameof(PerfilUsuario.Administrador), nameof(PerfilUsuario.Colaborador)];
}
