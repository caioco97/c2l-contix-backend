using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Contix.Domain.Exceptions;
using Contix.Domain.ValueObjects;

namespace Contix.Application.Features.NotasFiscais.Sefaz;

/// <summary>Certificado A1 (arquivo .pfx/.p12 e senha) já descriptografado, pronto para assinar e autenticar na SEFAZ.</summary>
public sealed record CertificadoA1(byte[] Pfx, string Senha)
{
    public X509Certificate2 Carregar() => LeitorCertificadoA1.Carregar(Pfx, Senha);
}

/// <summary>Metadados de um certificado A1.</summary>
public sealed record InfoCertificado(
    string Titular, string? Cnpj, string Emissor, string NumeroSerie, string Thumbprint, DateTime ValidoDe, DateTime ValidoAte);

/// <summary>Abre arquivos PKCS#12 e extrai o CNPJ do titular (ICP-Brasil).</summary>
public static partial class LeitorCertificadoA1
{
    /// <summary>OID do CNPJ no "otherName" do SubjectAltName dos certificados e-CNPJ da ICP-Brasil.</summary>
    public const string OidCnpj = "2.16.76.1.3.3";

    public static X509Certificate2 Carregar(byte[] pfx, string senha)
    {
        try
        {
            return X509CertificateLoader.LoadPkcs12(pfx, senha, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException)
        {
            throw new DomainException("Não foi possível abrir o certificado: confira a senha e se o arquivo é um .pfx (A1) válido.");
        }
    }

    public static InfoCertificado Ler(byte[] pfx, string senha)
    {
        using var cert = Carregar(pfx, senha);
        if (!cert.HasPrivateKey)
            throw new DomainException("O arquivo não traz a chave privada do certificado. Exporte o A1 com a chave privada (.pfx).");

        var titular = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        var emissor = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: true);
        return new InfoCertificado(
            string.IsNullOrWhiteSpace(titular) ? cert.Subject : titular,
            ExtrairCnpj(cert),
            string.IsNullOrWhiteSpace(emissor) ? cert.Issuer : emissor,
            cert.SerialNumber,
            cert.Thumbprint,
            cert.NotBefore.ToUniversalTime(),
            cert.NotAfter.ToUniversalTime());
    }

    /// <summary>CNPJ do titular: pelo SubjectAltName (OID 2.16.76.1.3.3) ou, na falta, pelo "NOME:CNPJ" do CN.</summary>
    public static string? ExtrairCnpj(X509Certificate2 cert)
    {
        foreach (var ext in cert.Extensions)
        {
            if (ext.Oid?.Value != "2.5.29.17") continue;
            try
            {
                if (CnpjDoSan(ext.RawData) is { } cnpj) return cnpj;
            }
            catch (AsnContentException) { /* SAN fora do padrão: tenta pelo CN. */ }
        }

        var cn = cert.GetNameInfo(X509NameType.SimpleName, false) ?? "";
        var m = CnpjNoCn().Match(cn);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? CnpjDoSan(byte[] raw)
    {
        var nomes = new AsnReader(raw, AsnEncodingRules.BER).ReadSequence();
        var otherName = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
        while (nomes.HasData)
        {
            if (!nomes.PeekTag().HasSameClassAndValue(otherName))
            {
                nomes.ReadEncodedValue();
                continue;
            }
            var other = nomes.ReadSequence(otherName);
            var oid = other.ReadObjectIdentifier();
            var valor = other.ReadSequence(otherName);
            if (oid != OidCnpj || !valor.HasData) continue;

            var tag = valor.PeekTag();
            string texto = tag.TagClass == TagClass.Universal && tag.TagValue == (int)UniversalTagNumber.OctetString
                ? Encoding.ASCII.GetString(valor.ReadOctetString())
                : valor.ReadCharacterString((UniversalTagNumber)tag.TagValue);
            var digitos = DocumentoFiscal.SomenteDigitos(texto);
            if (digitos.Length >= 14) return digitos[..14];
        }
        return null;
    }

    [GeneratedRegex(@":(\d{14})\b")]
    private static partial Regex CnpjNoCn();
}
