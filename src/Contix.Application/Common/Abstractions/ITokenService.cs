using Contix.Domain.Entities;

namespace Contix.Application.Common.Abstractions;

public sealed record AccessToken(string Token, DateTime ExpiraEm);
public sealed record GeneratedRefreshToken(string Token, string Hash, DateTime ExpiraEm);
/// <summary>Token aleatório opaco (ex.: redefinição de senha) e seu hash para armazenamento.</summary>
public sealed record GeneratedOpaqueToken(string Token, string Hash);

public interface ITokenService
{
    AccessToken GerarAccessToken(Usuario usuario);
    GeneratedRefreshToken GerarRefreshToken();
    string HashRefreshToken(string token);
    GeneratedOpaqueToken GerarTokenOpaco();
    /// <summary>SHA-256 em hexadecimal, usado para buscar tokens opacos sem armazená-los em claro.</summary>
    string HashToken(string token);
}
