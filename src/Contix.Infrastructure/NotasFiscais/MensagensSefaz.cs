using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Contix.Application.Features.NotasFiscais;
using Contix.Application.Features.NotasFiscais.Sefaz;
using Contix.Domain.Enums;

namespace Contix.Infrastructure.NotasFiscais;

/// <summary>Monta e lê as mensagens XML/SOAP dos web services nacionais da NF-e.</summary>
public static class MensagensSefaz
{
    public const string WsdlDistribuicao = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe";
    public const string WsdlEvento = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4";
    public const string AcaoDistribuicao = WsdlDistribuicao + "/nfeDistDFeInteresse";
    public const string AcaoEvento = WsdlEvento + "/nfeRecepcaoEvento";
    private const string Soap12 = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace Nfe = LeitorXmlNFe.Ns;
    /// <summary>Horário de Brasília (sem horário de verão desde 2019).</summary>
    private static readonly TimeSpan FusoBrasilia = TimeSpan.FromHours(-3);

    public static (string Tipo, string Descricao) Evento(ManifestacaoNFe tipo) => tipo switch
    {
        ManifestacaoNFe.Confirmada => ("210200", "Confirmacao da Operacao"),
        ManifestacaoNFe.Ciencia => ("210210", "Ciencia da Operacao"),
        ManifestacaoNFe.Desconhecida => ("210220", "Desconhecimento da Operacao"),
        ManifestacaoNFe.NaoRealizada => ("210240", "Operacao nao Realizada"),
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Manifestação sem evento na SEFAZ.")
    };

    public static string DistDFeInt(int tpAmb, int cUfAutor, string cnpj, long ultimoNsu) =>
        new XElement(Nfe + "distDFeInt", new XAttribute("versao", "1.01"),
            new XElement(Nfe + "tpAmb", tpAmb),
            new XElement(Nfe + "cUFAutor", cUfAutor),
            new XElement(Nfe + "CNPJ", cnpj),
            new XElement(Nfe + "distNSU", new XElement(Nfe + "ultNSU", ultimoNsu.ToString("D15", CultureInfo.InvariantCulture))))
            .ToString(SaveOptions.DisableFormatting);

    /// <summary>envEvento com o evento de manifestação assinado (XMLDSig enveloped, RSA-SHA1, C14N), como pede o leiaute.</summary>
    public static string EnvEventoAssinado(
        int tpAmb, string cnpj, string chave, ManifestacaoNFe tipo, string? justificativa, DateTimeOffset agora, X509Certificate2 certificado)
    {
        var (tpEvento, descricao) = Evento(tipo);
        const int seq = 1;
        var id = $"ID{tpEvento}{chave}{seq:D2}";
        var det = new XElement(Nfe + "detEvento", new XAttribute("versao", "1.00"), new XElement(Nfe + "descEvento", descricao));
        if (tipo == ManifestacaoNFe.NaoRealizada) det.Add(new XElement(Nfe + "xJust", justificativa));

        var dh = agora.ToOffset(FusoBrasilia).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        var env = new XElement(Nfe + "envEvento", new XAttribute("versao", "1.00"),
            new XElement(Nfe + "idLote", agora.ToUnixTimeMilliseconds().ToString("D15", CultureInfo.InvariantCulture)[..15]),
            new XElement(Nfe + "evento", new XAttribute("versao", "1.00"),
                new XElement(Nfe + "infEvento", new XAttribute("Id", id),
                    new XElement(Nfe + "cOrgao", 91),
                    new XElement(Nfe + "tpAmb", tpAmb),
                    new XElement(Nfe + "CNPJ", cnpj),
                    new XElement(Nfe + "chNFe", chave),
                    new XElement(Nfe + "dhEvento", dh),
                    new XElement(Nfe + "tpEvento", tpEvento),
                    new XElement(Nfe + "nSeqEvento", seq),
                    new XElement(Nfe + "verEvento", "1.00"),
                    det)));

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(env.ToString(SaveOptions.DisableFormatting));
        var evento = (XmlElement)doc.GetElementsByTagName("evento", LeitorXmlNFe.Ns)[0]!;
        evento.AppendChild(Assinar(doc, id, certificado));
        return doc.OuterXml;
    }

    private static XmlElement Assinar(XmlDocument doc, string id, X509Certificate2 certificado)
    {
        var signed = new SignedXml(doc) { SigningKey = certificado.GetRSAPrivateKey() };
        signed.SignedInfo!.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;
        signed.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
        var referencia = new Reference("#" + id) { DigestMethod = SignedXml.XmlDsigSHA1Url };
        referencia.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        referencia.AddTransform(new XmlDsigC14NTransform());
        signed.AddReference(referencia);
        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificado));
        signed.KeyInfo = keyInfo;
        signed.ComputeSignature();
        return (XmlElement)doc.ImportNode(signed.GetXml(), deep: true);
    }

    public static string Envelope(string operacaoXml, string wsdlNs, bool embrulharOperacao)
    {
        var corpo = embrulharOperacao
            ? $"<nfeDistDFeInteresse xmlns=\"{wsdlNs}\"><nfeDadosMsg>{operacaoXml}</nfeDadosMsg></nfeDistDFeInteresse>"
            : $"<nfeDadosMsg xmlns=\"{wsdlNs}\">{operacaoXml}</nfeDadosMsg>";
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
               + $"<soap12:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap12=\"{Soap12}\">"
               + $"<soap12:Body>{corpo}</soap12:Body></soap12:Envelope>";
    }

    public static RetornoDistribuicao LerRetornoDistribuicao(string soap)
    {
        var ret = Descendente(soap, "retDistDFeInt");
        var docs = new List<DocumentoDistribuido>();
        foreach (var z in ret.Element(Nfe + "loteDistDFeInt")?.Elements(Nfe + "docZip") ?? [])
        {
            var nsu = long.Parse((string?)z.Attribute("NSU") ?? "0", CultureInfo.InvariantCulture);
            docs.Add(new DocumentoDistribuido(nsu, (string?)z.Attribute("schema") ?? "", Descompactar(z.Value)));
        }
        return new RetornoDistribuicao(
            (string?)ret.Element(Nfe + "cStat") ?? "",
            (string?)ret.Element(Nfe + "xMotivo") ?? "",
            Long(ret.Element(Nfe + "ultNSU")),
            Long(ret.Element(Nfe + "maxNSU")),
            docs);
    }

    public static RetornoEvento LerRetornoEvento(string soap)
    {
        var ret = Descendente(soap, "retEnvEvento");
        var cStatLote = (string?)ret.Element(Nfe + "cStat") ?? "";
        var inf = ret.Element(Nfe + "retEvento")?.Element(Nfe + "infEvento");
        if (cStatLote != "128" || inf is null)
            return new RetornoEvento(false, cStatLote, (string?)ret.Element(Nfe + "xMotivo") ?? "", null);
        var cStat = (string?)inf.Element(Nfe + "cStat") ?? "";
        return new RetornoEvento(
            cStat is "135" or "136" or "573",
            cStat,
            (string?)inf.Element(Nfe + "xMotivo") ?? "",
            (string?)inf.Element(Nfe + "nProt"));
    }

    public static string Descompactar(string base64)
    {
        using var entrada = new MemoryStream(Convert.FromBase64String(base64.Trim()));
        using var gzip = new GZipStream(entrada, CompressionMode.Decompress);
        using var leitor = new StreamReader(gzip, Encoding.UTF8);
        return leitor.ReadToEnd();
    }

    public static string Compactar(string xml)
    {
        using var saida = new MemoryStream();
        using (var gzip = new GZipStream(saida, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(xml));
        return Convert.ToBase64String(saida.ToArray());
    }

    private static XElement Descendente(string soap, string nome)
    {
        XDocument doc;
        try
        {
            using var reader = XmlReader.Create(new StringReader(soap), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            doc = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new HttpRequestException("A SEFAZ devolveu uma resposta que não é XML.", ex);
        }
        return doc.Descendants(Nfe + nome).FirstOrDefault()
               ?? throw new HttpRequestException($"Resposta da SEFAZ sem {nome}: {Resumo(doc)}");
    }

    private static string Resumo(XDocument doc)
    {
        var texto = doc.Descendants().FirstOrDefault(e => e.Name.LocalName is "Text" or "faultstring")?.Value ?? doc.Root?.Value ?? "";
        return texto.Length > 300 ? texto[..300] : texto;
    }

    private static long Long(XElement? e) => long.TryParse(e?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
