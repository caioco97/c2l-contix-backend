namespace Contix.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string Secao = "Jwt";
    public string Issuer { get; set; } = "contix-api";
    public string Audience { get; set; } = "contix-web";
    /// <summary>Chave simétrica (mínimo 32 caracteres). Em produção, defina via variável de ambiente Jwt__SigningKey.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutos { get; set; } = 15;
    public int RefreshTokenDias { get; set; } = 7;
}
