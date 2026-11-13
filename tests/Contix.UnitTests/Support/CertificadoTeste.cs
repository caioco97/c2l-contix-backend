using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Contix.Tests.Support;

/// <summary>Gera certificados A1 de teste (autoassinados) no formato e-CNPJ da ICP-Brasil.</summary>
public static class CertificadoTeste
{
    /// <param name="cnpjNoSan">CNPJ no SubjectAltName (OID 2.16.76.1.3.3), como nos certificados reais.</param>
    /// <param name="cnpjNoCn">CNPJ no CN ("NOME:CNPJ"), como alguns emissores fazem.</param>
    public static byte[] Gerar(string senha, string? cnpjNoSan = null, string? cnpjNoCn = null,
        DateTimeOffset? inicio = null, DateTimeOffset? fim = null)
    {
        using var rsa = RSA.Create(2048);
        var cn = cnpjNoCn is null ? "EMPRESA TESTE LTDA" : $"EMPRESA TESTE LTDA:{cnpjNoCn}";
        var req = new CertificateRequest($"CN={cn}, O=ICP-Brasil, C=BR", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (cnpjNoSan is not null)
        {
            var w = new AsnWriter(AsnEncodingRules.DER);
            using (w.PushSequence())
            {
                using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
                {
                    w.WriteObjectIdentifier("2.16.76.1.3.3");
                    using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
                        w.WriteOctetString(Encoding.ASCII.GetBytes(cnpjNoSan));
                }
                w.WriteCharacterString(UniversalTagNumber.IA5String, "contato@empresa.com.br", new Asn1Tag(TagClass.ContextSpecific, 1));
            }
            req.CertificateExtensions.Add(new X509Extension("2.5.29.17", w.Encode(), false));
        }
        using var cert = req.CreateSelfSigned(inicio ?? DateTimeOffset.UtcNow.AddDays(-1), fim ?? DateTimeOffset.UtcNow.AddYears(1));
        return cert.Export(X509ContentType.Pkcs12, senha);
    }

    /// <summary>XML nfeProc mínimo e válido para o leitor.</summary>
    public static string NfeProc(string chave, string emitente, string destinatario, decimal valor = 150.75m,
        string dhEmi = "2026-10-05T10:30:00-03:00", string emitenteNome = "Fornecedor de Teste Ltda") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
          <NFe><infNFe versao="4.00" Id="NFe{chave}">
            <ide><cUF>35</cUF><mod>55</mod><serie>1</serie><nNF>{long.Parse(chave.Substring(25, 9))}</nNF><dhEmi>{dhEmi}</dhEmi><tpNF>1</tpNF></ide>
            <emit><CNPJ>{emitente}</CNPJ><xNome>{emitenteNome}</xNome><IE>111222333444</IE></emit>
            <dest><CNPJ>{destinatario}</CNPJ><xNome>Destinatario</xNome></dest>
            <total><ICMSTot><vNF>{valor.ToString(System.Globalization.CultureInfo.InvariantCulture)}</vNF></ICMSTot></total>
          </infNFe></NFe>
          <protNFe versao="4.00"><infProt><chNFe>{chave}</chNFe><nProt>135260000012345</nProt><cStat>100</cStat></infProt></protNFe>
        </nfeProc>
        """;

    /// <summary>Chave de acesso com DV válido.</summary>
    public static string Chave(string cnpjEmitente, int numero, string aamm = "2610")
    {
        var semDv = $"35{aamm}{cnpjEmitente}55001{numero:D9}1{Random.Shared.Next(10_000_000, 99_999_999):D8}";
        int soma = 0, peso = 2;
        for (var i = semDv.Length - 1; i >= 0; i--) { soma += (semDv[i] - '0') * peso; peso = peso == 9 ? 2 : peso + 1; }
        var resto = soma % 11;
        return semDv + (resto < 2 ? 0 : 11 - resto);
    }

    public static string NovoCnpj()
    {
        var d = Enumerable.Range(0, 12).Select(_ => Random.Shared.Next(10)).ToList();
        int Dv(IReadOnlyList<int> n, int[] pesos) { var r = n.Select((x, i) => x * pesos[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        d.Add(Dv(d, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        d.Add(Dv(d, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        return string.Concat(d);
    }
}
