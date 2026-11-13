namespace Contix.Application.Common.Abstractions;

/// <summary>Criptografia simétrica de segredos guardados no banco (ex.: certificado A1 e sua senha).</summary>
public interface ICriptografiaSegredos
{
    byte[] Proteger(ReadOnlySpan<byte> dados);
    /// <summary>Lança <see cref="System.Security.Cryptography.CryptographicException"/> se o conteúdo foi adulterado ou a chave mudou.</summary>
    byte[] Desproteger(ReadOnlySpan<byte> protegido);
}
