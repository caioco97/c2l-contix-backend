using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Contix.Infrastructure.Pdf;

public sealed record DanfeParte(string Nome, string? Documento, string? Ie, string? Endereco, string? Municipio, string? Uf, string? Cep, string? Fone);

public sealed record DanfeItem(
    string Codigo, string Descricao, string? Ncm, string? Cst, string? Cfop, string? Unidade,
    decimal Quantidade, decimal ValorUnitario, decimal ValorTotal, decimal BaseIcms, decimal ValorIcms, decimal AliquotaIcms, decimal ValorIpi);

public sealed record DanfeDuplicata(string Numero, DateOnly? Vencimento, decimal Valor);

/// <summary>Dados do DANFE lidos de um XML nfeProc (NF-e modelo 55, layout 4.00).</summary>
public sealed record DanfeModelo(
    string Chave, string NaturezaOperacao, int TipoOperacao, string Serie, string Numero,
    DateTime? Emissao, DateTime? SaidaEntrada, string? Protocolo, DateTime? DataProtocolo, bool Homologacao,
    DanfeParte Emitente, DanfeParte Destinatario,
    decimal BaseIcms, decimal ValorIcms, decimal BaseIcmsSt, decimal ValorIcmsSt, decimal ValorProdutos, decimal Frete,
    decimal Seguro, decimal Desconto, decimal Outras, decimal Ipi, decimal ValorTotal,
    string ModalidadeFrete, string? Transportador, string? TransportadorDocumento,
    IReadOnlyList<DanfeItem> Itens, IReadOnlyList<DanfeDuplicata> Duplicatas, string? InformacoesComplementares)
{
    private static readonly XNamespace N = "http://www.portalfiscal.inf.br/nfe";

    public static DanfeModelo Ler(string xml)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 };
        using var reader = XmlReader.Create(new StringReader(xml.TrimStart('﻿')), settings);
        var doc = XDocument.Load(reader);
        var raiz = doc.Root ?? throw new FormatException("XML vazio.");
        var nfe = raiz.Name.LocalName == "NFe" ? raiz : raiz.Element(N + "NFe") ?? throw new FormatException("O XML não contém a NF-e completa.");
        var inf = nfe.Element(N + "infNFe") ?? throw new FormatException("O XML não contém infNFe.");
        var ide = inf.Element(N + "ide")!;
        var emit = inf.Element(N + "emit")!;
        var dest = inf.Element(N + "dest");
        var tot = inf.Element(N + "total")?.Element(N + "ICMSTot");
        var transp = inf.Element(N + "transp");
        var prot = raiz.Element(N + "protNFe")?.Element(N + "infProt");

        return new DanfeModelo(
            Chave: ((string?)inf.Attribute("Id") ?? "").Replace("NFe", ""),
            NaturezaOperacao: T(ide, "natOp") ?? "",
            TipoOperacao: int.TryParse(T(ide, "tpNF"), out var tp) ? tp : 1,
            Serie: T(ide, "serie") ?? "",
            Numero: T(ide, "nNF") ?? "",
            Emissao: Dt(T(ide, "dhEmi")),
            SaidaEntrada: Dt(T(ide, "dhSaiEnt")),
            Protocolo: T(prot, "nProt"),
            DataProtocolo: Dt(T(prot, "dhRecbto")),
            Homologacao: T(ide, "tpAmb") == "2",
            Emitente: Parte(emit, "enderEmit"),
            Destinatario: dest is null ? new DanfeParte("", null, null, null, null, null, null, null) : Parte(dest, "enderDest"),
            BaseIcms: D(tot, "vBC"), ValorIcms: D(tot, "vICMS"), BaseIcmsSt: D(tot, "vBCST"), ValorIcmsSt: D(tot, "vST"),
            ValorProdutos: D(tot, "vProd"), Frete: D(tot, "vFrete"), Seguro: D(tot, "vSeg"), Desconto: D(tot, "vDesc"),
            Outras: D(tot, "vOutro"), Ipi: D(tot, "vIPI"), ValorTotal: D(tot, "vNF"),
            ModalidadeFrete: DescreverFrete(T(transp, "modFrete")),
            Transportador: T(transp?.Element(N + "transporta"), "xNome"),
            TransportadorDocumento: T(transp?.Element(N + "transporta"), "CNPJ") ?? T(transp?.Element(N + "transporta"), "CPF"),
            Itens: inf.Elements(N + "det").Select(Item).ToList(),
            Duplicatas: inf.Element(N + "cobr")?.Elements(N + "dup")
                .Select(d => new DanfeDuplicata(T(d, "nDup") ?? "", DateOnly.TryParse(T(d, "dVenc"), CultureInfo.InvariantCulture, out var v) ? v : null, D(d, "vDup")))
                .ToList() ?? [],
            InformacoesComplementares: T(inf.Element(N + "infAdic"), "infCpl"));
    }

    private static DanfeParte Parte(XElement e, string endereco)
    {
        var end = e.Element(N + endereco);
        var logradouro = string.Join(", ", new[] { T(end, "xLgr"), T(end, "nro"), T(end, "xCpl"), T(end, "xBairro") }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new DanfeParte(T(e, "xNome") ?? "", T(e, "CNPJ") ?? T(e, "CPF"), T(e, "IE"), logradouro, T(end, "xMun"), T(end, "UF"), T(end, "CEP"), T(end, "fone"));
    }

    private static DanfeItem Item(XElement det)
    {
        var prod = det.Element(N + "prod");
        var imposto = det.Element(N + "imposto");
        var icms = imposto?.Element(N + "ICMS")?.Elements().FirstOrDefault();
        var ipi = imposto?.Element(N + "IPI")?.Element(N + "IPITrib");
        var cst = T(icms, "orig") + (T(icms, "CST") ?? T(icms, "CSOSN"));
        return new DanfeItem(T(prod, "cProd") ?? "", T(prod, "xProd") ?? "", T(prod, "NCM"), cst, T(prod, "CFOP"), T(prod, "uCom"),
            D(prod, "qCom"), D(prod, "vUnCom"), D(prod, "vProd"), D(icms, "vBC"), D(icms, "vICMS"), D(icms, "pICMS"), D(ipi, "vIPI"));
    }

    private static string DescreverFrete(string? codigo) => codigo switch
    {
        "0" => "0 - Por conta do emitente",
        "1" => "1 - Por conta do destinatário",
        "2" => "2 - Por conta de terceiros",
        "3" => "3 - Próprio por conta do remetente",
        "4" => "4 - Próprio por conta do destinatário",
        "9" => "9 - Sem frete",
        _ => codigo ?? ""
    };

    private static string? T(XElement? e, string nome) => e?.Element(N + nome)?.Value.Trim() is { Length: > 0 } v ? v : null;

    private static decimal D(XElement? e, string nome) =>
        decimal.TryParse(T(e, nome), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static DateTime? Dt(string? valor) =>
        DateTimeOffset.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.DateTime : null;
}
