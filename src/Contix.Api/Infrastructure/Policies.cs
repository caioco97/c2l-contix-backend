using Contix.Domain.Enums;

namespace Contix.Api.Infrastructure;

public static class Policies
{
    /// <summary>Administradores e colaboradores do escritório.</summary>
    public const string Escritorio = nameof(Escritorio);
    /// <summary>Somente administradores do escritório.</summary>
    public const string Administrador = nameof(Administrador);

    public static readonly string[] RolesEscritorio =
        [nameof(PerfilUsuario.Administrador), nameof(PerfilUsuario.Colaborador)];
}
