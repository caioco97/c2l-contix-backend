using System.Security.Cryptography;
using Contix.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace Contix.Infrastructure.Security;

public sealed class CriptografiaOptions
{
    public const string Secao = "Criptografia";

    /// <summary>
    /// Chave AES-256 em Base64 (32 bytes) usada nos segredos guardados no banco (certificados A1).
    /// Gere com <c>openssl rand -base64 32</c> e guarde fora do repositório. Trocar a chave invalida os certificados já enviados.
    /// </summary>
    public string? Chave { get; set; }
}

/// <summary>AES-256-GCM: [versão 1 byte][nonce 12][tag 16][cifrado]. Detecta adulteração na leitura.</summary>
public sealed class CriptografiaAesGcm : ICriptografiaSegredos
{
    private const byte Versao = 1;
    private const int TamanhoNonce = 12;
    private const int TamanhoTag = 16;
    private readonly Lazy<byte[]> _chaveLazy;

    public CriptografiaAesGcm(IOptions<CriptografiaOptions> options) =>
        // Lida só no primeiro uso: ambientes sem certificados não precisam configurar a chave.
        _chaveLazy = new Lazy<byte[]>(() =>
        {
            var base64 = options.Value.Chave;
            if (string.IsNullOrWhiteSpace(base64))
                throw new InvalidOperationException("Criptografia:Chave não configurada (Base64 de 32 bytes).");
            var chave = Convert.FromBase64String(base64);
            return chave.Length == 32 ? chave : throw new InvalidOperationException("Criptografia:Chave deve ter 32 bytes (AES-256).");
        });

    private byte[] _chave => _chaveLazy.Value;

    public byte[] Proteger(ReadOnlySpan<byte> dados)
    {
        var saida = new byte[1 + TamanhoNonce + TamanhoTag + dados.Length];
        saida[0] = Versao;
        var nonce = saida.AsSpan(1, TamanhoNonce);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_chave, TamanhoTag);
        aes.Encrypt(nonce, dados, saida.AsSpan(1 + TamanhoNonce + TamanhoTag), saida.AsSpan(1 + TamanhoNonce, TamanhoTag));
        return saida;
    }

    public byte[] Desproteger(ReadOnlySpan<byte> protegido)
    {
        if (protegido.Length < 1 + TamanhoNonce + TamanhoTag || protegido[0] != Versao)
            throw new CryptographicException("Conteúdo criptografado em formato desconhecido.");
        var cifrado = protegido[(1 + TamanhoNonce + TamanhoTag)..];
        var claro = new byte[cifrado.Length];
        using var aes = new AesGcm(_chave, TamanhoTag);
        aes.Decrypt(protegido.Slice(1, TamanhoNonce), cifrado, protegido.Slice(1 + TamanhoNonce, TamanhoTag), claro);
        return claro;
    }
}
