using Contix.Application.Common.Models;
using Contix.Domain.Entities;
using Contix.Domain.Enums;

namespace Contix.Application.Features.Auth;

public sealed record LoginRequest(string Email, string Senha);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record EsqueciSenhaRequest(string Email);

public sealed record RedefinirSenhaRequest(string Token, string NovaSenha);

/// <summary>Onboarding: cria o escritório e seu primeiro administrador.</summary>
public sealed record RegistrarEscritorioRequest(
    string RazaoSocial,
    string? NomeFantasia,
    string Cnpj,
    string? Crc,
    string? Telefone,
    EnderecoDto? Endereco,
    string NomeAdministrador,
    string EmailAdministrador,
    string Senha);

public sealed record EscritorioResumoDto(Guid Id, string Nome, string? LogoUrl);

public sealed record UsuarioAutenticadoDto(
    Guid Id, string Nome, string Email, PerfilUsuario Perfil, EscritorioResumoDto Escritorio)
{
    /// <remarks>Requer <see cref="Usuario.Escritorio"/> carregado.</remarks>
    public static UsuarioAutenticadoDto From(Usuario u)
    {
        var e = u.Escritorio ?? throw new InvalidOperationException("Escritório do usuário não carregado.");
        return new(u.Id, u.Nome, u.Email, u.Perfil,
            new EscritorioResumoDto(e.Id, e.NomeFantasia ?? e.RazaoSocial, e.LogoUrl));
    }
}

public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiraEm,
    string RefreshToken,
    DateTime RefreshTokenExpiraEm,
    UsuarioAutenticadoDto Usuario);
