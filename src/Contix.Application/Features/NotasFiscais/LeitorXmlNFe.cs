using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Contix.Domain.Enums;
using Contix.Domain.ValueObjects;

namespace Contix.Application.Features.NotasFiscais;

/// <summary>Dados de uma NF-e lidos do XML (nota completa ou resumo da SEFAZ).</summary>
/// <param name="DestinatarioDocumento">CNPJ/CPF do destinatário. Nulo no resumo (resNFe), que já é do CNPJ consultado.</param>
public sealed record DadosNotaFiscal(
    string ChaveAcesso, string EmitenteDocumento, string EmitenteNome, string? EmitenteIe, string? DestinatarioDocumento,
    decimal ValorTotal, DateTime DataEmissaoUtc, int? TipoOperacao, string? Protocolo, SituacaoNFe Situacao);

/// <summary>Documento fiscal reconhecido em um XML.</summary>
public abstract record DocumentoNFe;

/// <summary>NF-e: completa (nfeProc/NFe) ou resumo (resNFe).</summary>
public sealed record NotaLida(DadosNotaFiscal Dados, bool Completa) : DocumentoNFe;

/// <summary>Evento de uma NF-e (resEvento/procEventoNFe), ex.: cancelamento (110111).</summary>
public sealed record EventoLido(string ChaveAcesso, string TipoEvento) : DocumentoNFe;

/// <summary>Lê os XMLs da NF-e (layout 4.00) e os documentos da Distribuição DF-e.</summary>
public static class LeitorXmlNFe
{
    public const string Ns = "http://www.portalfiscal.inf.br/nfe";
    public const string EventoCancelamento = "110111";
    private static readonly XNamespace N = Ns;

    /// <summary>Lê o XML. Lança <see cref="FormatException"/> com mensagem amigável quando não é uma NF-e reconhecida.</summary>
    public static DocumentoNFe Ler(string xml)
    {
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 };
            using var reader = XmlReader.Create(new StringReader(xml.TrimStart('﻿')), settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            throw new FormatException("O arquivo não é um XML válido.");
        }

        var raiz = doc.Root ?? throw new FormatException("O arquivo não é um XML válido.");
        return raiz.Name.LocalName switch
        {
            "nfeProc" => LerNotaCompleta(raiz.Element(N + "NFe") ?? throw Invalido(), raiz.Element(N + "protNFe")),
            "NFe" => throw SemProtocolo(),
            "resNFe" => LerResumo(raiz),
            "resEvento" => new EventoLido(Obrigatorio(raiz, "chNFe"), Obrigatorio(raiz, "tpEvento")),
            "procEventoNFe" => LerProcEvento(raiz),
            _ => throw new FormatException("O XML não é de uma NF-e (esperado nfeProc, NFe ou resNFe).")
        };
    }

    private static NotaLida LerNotaCompleta(XElement nfe, XElement? prot)
    {
        var inf = nfe.Element(N + "infNFe") ?? throw Invalido();
        var id = (string?)inf.Attribute("Id") ?? "";
        var chave = id.StartsWith("NFe", StringComparison.Ordinal) ? id[3..] : id;
        var ide = inf.Element(N + "ide") ?? throw Invalido();
        var emit = inf.Element(N + "emit") ?? throw Invalido();
        var dest = inf.Element(N + "dest");
        var total = inf.Element(N + "total")?.Element(N + "ICMSTot");

        var modelo = (string?)ide.Element(N + "mod");
        if (modelo is not null && modelo != "55")
            throw new FormatException("Só NF-e modelo 55 é aceita (NFC-e e outros modelos não são notas de entrada).");

        var infProt = prot?.Element(N + "infProt");
        var cStat = (string?)infProt?.Element(N + "cStat");
        // Só vale nota com protocolo da SEFAZ: 100/150 autorizada, 110/301/302/303 denegada.
        var situacao = cStat switch
        {
            "100" or "150" => SituacaoNFe.Autorizada,
            "110" or "301" or "302" or "303" => SituacaoNFe.Denegada,
            _ => throw SemProtocolo()
        };

        var dados = new DadosNotaFiscal(
            ValidarChave(chave),
            Documento(emit) ?? throw Invalido(),
            Texto(emit, "xNome") ?? "(sem nome)",
            Texto(emit, "IE"),
            dest is null ? null : Documento(dest),
            Decimal(total?.Element(N + "vNF")),
            Data((string?)ide.Element(N + "dhEmi") ?? (string?)ide.Element(N + "dEmi")),
            Inteiro(ide.Element(N + "tpNF")),
            (string?)infProt?.Element(N + "nProt"),
            situacao);
        return new NotaLida(dados, Completa: true);
    }

    private static NotaLida LerResumo(XElement res)
    {
        var situacao = (string?)res.Element(N + "cSitNFe") switch
        {
            "2" => SituacaoNFe.Denegada,
            "3" => SituacaoNFe.Cancelada,
            _ => SituacaoNFe.Autorizada
        };
        var dados = new DadosNotaFiscal(
            ValidarChave(Obrigatorio(res, "chNFe")),
            Documento(res) ?? throw Invalido(),
            Texto(res, "xNome") ?? "(sem nome)",
            Texto(res, "IE"),
            null,
            Decimal(res.Element(N + "vNF")),
            Data((string?)res.Element(N + "dhEmi")),
            Inteiro(res.Element(N + "tpNF")),
            Texto(res, "nProt"),
            situacao);
        return new NotaLida(dados, Completa: false);
    }

    private static EventoLido LerProcEvento(XElement proc)
    {
        var inf = proc.Element(N + "evento")?.Element(N + "infEvento") ?? throw Invalido();
        return new EventoLido(Obrigatorio(inf, "chNFe"), Obrigatorio(inf, "tpEvento"));
    }

    private static FormatException SemProtocolo() =>
        new("A NF-e não tem protocolo de autorização da SEFAZ. Envie o XML autorizado (nfeProc, com protNFe).");

    private static string ValidarChave(string chave) =>
        chave.Length == 44 && chave.All(char.IsAsciiDigit) ? chave : throw new FormatException("Chave de acesso da NF-e inválida.");

    private static string? Documento(XElement e)
    {
        var d = DocumentoFiscal.SomenteDigitos((string?)e.Element(N + "CNPJ") ?? (string?)e.Element(N + "CPF"));
        return d.Length is 11 or 14 ? d : null;
    }

    private static string Obrigatorio(XElement e, string nome) => Texto(e, nome) ?? throw Invalido();

    private static string? Texto(XElement e, string nome) =>
        (string?)e.Element(N + nome) is { } v && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static decimal Decimal(XElement? e) =>
        e is not null && decimal.TryParse(e.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static int? Inteiro(XElement? e) => int.TryParse(e?.Value, out var v) ? v : null;

    private static DateTime Data(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) throw new FormatException("A NF-e não tem data de emissão.");
        if (DateTimeOffset.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto) && valor.Length > 10)
            return dto.UtcDateTime;
        if (DateOnly.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            // Layout antigo (dEmi): só a data. Meio-dia de Brasília para não virar o dia em nenhum fuso.
            return d.ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc);
        throw new FormatException("Data de emissão da NF-e inválida.");
    }

    private static FormatException Invalido() => new("O XML não segue o layout da NF-e.");
}
