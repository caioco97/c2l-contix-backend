using Microsoft.AspNetCore.Identity;

namespace Contix.Infrastructure.Security;

/// <summary>Hash de senha com PBKDF2 (implementação do ASP.NET Core Identity).</summary>
public sealed class PasswordHasher : Application.Common.Abstractions.IPasswordHasher
{
    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly object Usuario = new();

    public string Hash(string senha) => Hasher.HashPassword(Usuario, senha);

    public bool Verify(string hash, string senha) =>
        Hasher.VerifyHashedPassword(Usuario, hash, senha) != PasswordVerificationResult.Failed;
}
