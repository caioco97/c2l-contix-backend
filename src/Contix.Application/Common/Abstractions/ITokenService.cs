using Contix.Domain.Entities;

namespace Contix.Application.Common.Abstractions;

public sealed record AccessToken(string Token, DateTime ExpiraEm);
public sealed record GeneratedRefreshToken(string Token, string Hash, DateTime ExpiraEm);

public interface ITokenService
{
    AccessToken GerarAccessToken(Usuario usuario);
    GeneratedRefreshToken GerarRefreshToken();
    string HashRefreshToken(string token);
}
