using Contix.Application.Common.Models;
using Contix.Domain.Entities;
using Contix.Domain.Enums;

namespace Contix.Application.Features.Auth;

public sealed record LoginRequest(string Email, string Senha);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record EsqueciSenhaRequest(string Email);

public sealed record RedefinirSenhaRequest(string Token, string NovaSenha);

/// <summary>Troca de senha pelo próprio usuário logado (tela Meu perfil).</summary>
public sealed record TrocarSenhaRequest(string SenhaAtual, string NovaSenha);

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

/// <param name="FotoVersao">Muda a cada troca da foto de perfil; nulo quando o usuário não tem foto.</param>
public sealed record UsuarioAutenticadoDto(
    Guid Id, string Nome, string Email, PerfilUsuario Perfil, EscritorioResumoDto Escritorio,
    string? Telefone = null, string? FotoVersao = null)
{
    /// <remarks>Requer <see cref="Usuario.Escritorio"/> carregado.</remarks>
    public static UsuarioAutenticadoDto From(Usuario u)
    {
        var e = u.Escritorio ?? throw new InvalidOperationException("Escritório do usuário não carregado.");
        return new(u.Id, u.Nome, u.Email, u.Perfil,
            new EscritorioResumoDto(e.Id, e.NomeFantasia ?? e.RazaoSocial, e.LogoUrl),
            u.Telefone,
            u.FotoStorageKey is null || u.FotoAtualizadaEm is null ? null : u.FotoAtualizadaEm.Value.Ticks.ToString());
    }
}

public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiraEm,
    string RefreshToken,
    DateTime RefreshTokenExpiraEm,
    UsuarioAutenticadoDto Usuario);
