using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Contix.Infrastructure.Security;

public sealed class JwtTokenService(IOptions<JwtOptions> options, IDateTimeProvider clock) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public AccessToken GerarAccessToken(Usuario usuario)
    {
        var agora = clock.UtcNow;
        var expira = agora.AddMinutes(_options.AccessTokenMinutos);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, usuario.Email),
            new(JwtRegisteredClaimNames.Name, usuario.Nome),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, usuario.Perfil.ToString()),
            new(ContixClaims.TenantId, usuario.TenantId.ToString())
        };

        var credenciais = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, agora, expira, credenciais);
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expira);
    }

    public GeneratedRefreshToken GerarRefreshToken()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        return new GeneratedRefreshToken(token, HashRefreshToken(token), clock.UtcNow.AddDays(_options.RefreshTokenDias));
    }

    public string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
