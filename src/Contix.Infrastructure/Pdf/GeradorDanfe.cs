using System.Globalization;
using Contix.Application.Features.NotasFiscais;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Contix.Infrastructure.Pdf;

/// <summary>
/// DANFE (retrato) gerado a partir do XML autorizado da NF-e. Segue os blocos do Manual de Orientação do Contribuinte
/// (emitente, chave com código de barras, destinatário, faturas, impostos, transporte, produtos e dados adicionais).
/// </summary>
public sealed class GeradorDanfe : IGeradorDanfe
{
    private static readonly CultureInfo Br = new("pt-BR");
    static GeradorDanfe() => QuestPdfConfig.Configurar();

    public byte[] Gerar(string xml)
    {
        var d = DanfeModelo.Ler(xml);
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(14);
            page.DefaultTextStyle(x => x.FontSize(7));

            page.Header().Column(c =>
            {
                c.Item().Row(r =>
                {
                    r.RelativeItem(4).Border(0.5f).Padding(4).Column(e =>
                    {
                        e.Item().Text(d.Emitente.Nome).Bold().FontSize(10);
                        e.Item().Text(d.Emitente.Endereco ?? "");
                        e.Item().Text($"{d.Emitente.Municipio} - {d.Emitente.Uf}  CEP {FormatarCep(d.Emitente.Cep)}");
                        if (d.Emitente.Fone is not null) e.Item().Text($"Fone: {d.Emitente.Fone}");
                    });
                    r.RelativeItem(2).Border(0.5f).Padding(4).AlignCenter().Column(e =>
                    {
                        e.Item().AlignCenter().Text("DANFE").Bold().FontSize(12);
                        e.Item().AlignCenter().Text("Documento Auxiliar da Nota Fiscal Eletrônica").FontSize(6);
                        e.Item().AlignCenter().Text($"{d.TipoOperacao} - {(d.TipoOperacao == 0 ? "Entrada" : "Saída")}").Bold();
                        e.Item().AlignCenter().Text($"Nº {FormatarNumero(d.Numero)}").Bold();
                        e.Item().AlignCenter().Text($"Série {d.Serie}");
                        e.Item().AlignCenter().Text(x => { x.Span("Folha "); x.CurrentPageNumber(); x.Span("/"); x.TotalPages(); });
                    });
                    r.RelativeItem(4).Border(0.5f).Padding(4).Column(e =>
                    {
                        if (d.Chave.Length == 44) e.Item().Height(34).Element(c => CodigoDeBarras(c, d.Chave));
                        e.Item().PaddingTop(2).Text("CHAVE DE ACESSO").FontSize(6);
                        e.Item().Text(FormatarChave(d.Chave)).Bold().FontSize(8);
                        e.Item().PaddingTop(2).Text("Consulta de autenticidade no portal nacional da NF-e www.nfe.fazenda.gov.br/portal ou no site da Sefaz autorizadora").FontSize(6);
                    });
                });
                c.Item().Row(r =>
                {
                    Campo(r.RelativeItem(6), "NATUREZA DA OPERAÇÃO", d.NaturezaOperacao);
                    Campo(r.RelativeItem(4), "PROTOCOLO DE AUTORIZAÇÃO DE USO",
                        d.Protocolo is null ? "—" : $"{d.Protocolo} {d.DataProtocolo:dd/MM/yyyy HH:mm:ss}");
                });
                c.Item().Row(r =>
                {
                    Campo(r.RelativeItem(), "INSCRIÇÃO ESTADUAL", d.Emitente.Ie ?? "");
                    Campo(r.RelativeItem(), "CNPJ / CPF", FormatarDocumento(d.Emitente.Documento));
                });
                if (d.Homologacao)
                    c.Item().Background(Colors.Red.Lighten4).Padding(2).AlignCenter()
                        .Text("EMITIDA EM AMBIENTE DE HOMOLOGAÇÃO - SEM VALOR FISCAL").Bold().FontColor(Colors.Red.Darken3);
            });

            page.Content().Column(c =>
            {
                Titulo(c, "DESTINATÁRIO / REMETENTE");
                c.Item().Row(r =>
                {
                    Campo(r.RelativeItem(6), "NOME / RAZÃO SOCIAL", d.Destinatario.Nome);
                    Campo(r.RelativeItem(3), "CNPJ / CPF", FormatarDocumento(d.Destinatario.Documento));
                    Campo(r.RelativeItem(2), "DATA DA EMISSÃO", $"{d.Emissao:dd/MM/yyyy}");
                });
                c.Item().Row(r =>
                {
                    Campo(r.RelativeItem(6), "ENDEREÇO", d.Destinatario.Endereco ?? "");
                    Campo(r.RelativeItem(3), "CEP", FormatarCep(d.Destinatario.Cep));
                    Campo(r.RelativeItem(2), "DATA DA SAÍDA/ENTRADA", $"{d.SaidaEntrada:dd/MM/yyyy}");
                });
                c.Item().Row(r =>
                {
                    Campo(r.RelativeItem(4), "MUNICÍPIO", d.Destinatario.Municipio ?? "");
                    Campo(r.RelativeItem(1), "UF", d.Destinatario.Uf ?? "");
                    Campo(r.RelativeItem(2), "FONE / FAX", d.Destinatario.Fone ?? "");
                    Campo(r.RelativeItem(2), "INSCRIÇÃO ESTADUAL", d.Destinatario.Ie ?? "");
                    Campo(r.RelativeItem(2), "HORA DA SAÍDA/ENTRADA", $"{d.SaidaEntrada:HH:mm:ss}");
                });

                if (d.Duplicatas.Count > 0)
                {
                    Titulo(c, "FATURA / DUPLICATAS");
                    c.Item().Border(0.5f).Padding(3).Text(string.Join("     ",
                        d.Duplicatas.Select(p => $"{p.Numero}  {p.Vencimento:dd/MM/yyyy}  {Moeda(p.Valor)}")));
                }

                Titulo(c, "CÁLCULO DO IMPOSTO");
                c.Item().Row(r =>
                {
                    Campo(r.RelativeItem(), "BASE DE CÁLC. DO ICMS", Moeda(d.BaseIcms));
                    Campo(r.RelativeItem(), "VALOR DO ICMS", Moeda(d.ValorIcms));
                    Campo(r.RelativeItem(), "BASE DE CÁLC. ICMS S.T.", Moeda(d.BaseIcmsSt));
                    Campo(r.RelativeItem(), "VALOR DO ICMS SUBST.", Moeda(d.ValorIcmsSt));
                    Campo(r.RelativeItem(), "V. TOTAL PRODUTOS", Moeda(d.ValorProdutos));
                });
                c.Item().Row(r =>
                {
                    Campo(r.RelativeItem(), "VALOR DO FRETE", Moeda(d.Frete));
                    Campo(r.RelativeItem(), "VALOR DO SEGURO", Moeda(d.Seguro));
                    Campo(r.RelativeItem(), "DESCONTO", Moeda(d.Desconto));
                    Campo(r.RelativeItem(), "OUTRAS DESPESAS", Moeda(d.Outras));
                    Campo(r.RelativeItem(), "VALOR TOTAL IPI", Moeda(d.Ipi));
                    Campo(r.RelativeItem(), "V. TOTAL DA NOTA", Moeda(d.ValorTotal), negrito: true);
                });

                Titulo(c, "TRANSPORTADOR / VOLUMES TRANSPORTADOS");
                c.Item().Row(r =>
                {
                    Campo(r.RelativeItem(5), "NOME / RAZÃO SOCIAL", d.Transportador ?? "");
                    Campo(r.RelativeItem(3), "FRETE", d.ModalidadeFrete);
                    Campo(r.RelativeItem(3), "CNPJ / CPF", FormatarDocumento(d.TransportadorDocumento));
                });

                Titulo(c, "DADOS DOS PRODUTOS / SERVIÇOS");
                c.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(1.2f); cols.RelativeColumn(4); cols.RelativeColumn(1.1f); cols.RelativeColumn(0.7f);
                        cols.RelativeColumn(0.7f); cols.RelativeColumn(0.6f); cols.RelativeColumn(1); cols.RelativeColumn(1.1f);
                        cols.RelativeColumn(1.1f); cols.RelativeColumn(1); cols.RelativeColumn(1); cols.RelativeColumn(1); cols.RelativeColumn(0.7f);
                    });
                    t.Header(h =>
                    {
                        foreach (var col in new[] { "CÓDIGO", "DESCRIÇÃO", "NCM/SH", "CST", "CFOP", "UN", "QUANT.", "V. UNIT.", "V. TOTAL", "BC ICMS", "V. ICMS", "V. IPI", "ALÍQ. ICMS" })
                            h.Cell().Border(0.5f).Background(Colors.Grey.Lighten3).Padding(2).Text(col).FontSize(6).Bold();
                    });
                    foreach (var i in d.Itens)
                    {
                        Celula(t, i.Codigo); Celula(t, i.Descricao); Celula(t, i.Ncm ?? ""); Celula(t, i.Cst ?? "");
                        Celula(t, i.Cfop ?? ""); Celula(t, i.Unidade ?? "");
                        Celula(t, i.Quantidade.ToString("#,##0.####", Br), true); Celula(t, i.ValorUnitario.ToString("#,##0.00##", Br), true);
                        Celula(t, Moeda(i.ValorTotal), true); Celula(t, Moeda(i.BaseIcms), true); Celula(t, Moeda(i.ValorIcms), true);
                        Celula(t, Moeda(i.ValorIpi), true); Celula(t, i.AliquotaIcms.ToString("0.##", Br), true);
                    }
                });

                Titulo(c, "DADOS ADICIONAIS");
                c.Item().Border(0.5f).MinHeight(50).Padding(3).Column(e =>
                {
                    e.Item().Text("INFORMAÇÕES COMPLEMENTARES").FontSize(5);
                    e.Item().Text(d.InformacoesComplementares ?? "");
                });
            });

            page.Footer().AlignRight().Text("Gerado pelo Contix a partir do XML autorizado da NF-e").FontSize(6).FontColor(Colors.Grey.Darken1);
        })).GeneratePdf();
    }

    /// <summary>Barras desenhadas como faixas de largura proporcional aos módulos do Code 128C.</summary>
    private static void CodigoDeBarras(IContainer container, string chave) =>
        container.Row(r =>
        {
            var larguras = Code128.Larguras(chave);
            var total = (float)larguras.Sum();
            r.RelativeItem(10 / total);
            for (var i = 0; i < larguras.Count; i++)
            {
                var item = r.RelativeItem(larguras[i] / total);
                if (i % 2 == 0) item.Background(Colors.Black);
            }
            r.RelativeItem(10 / total);
        });

    private static void Titulo(ColumnDescriptor c, string texto) =>
        c.Item().PaddingTop(5).PaddingBottom(1).Text(texto).FontSize(7).Bold();

    private static void Campo(IContainer container, string rotulo, string valor, bool negrito = false) =>
        container.Border(0.5f).Padding(2).Column(e =>
        {
            e.Item().Text(rotulo).FontSize(5);
            var texto = e.Item().Text(valor).FontSize(8);
            if (negrito) texto.Bold();
        });

    private static void Celula(TableDescriptor t, string valor, bool direita = false)
    {
        var cell = t.Cell().BorderLeft(0.5f).BorderRight(0.5f).BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten1).Padding(2);
        (direita ? cell.AlignRight() : cell).Text(valor).FontSize(6.5f);
    }

    private static string Moeda(decimal v) => v.ToString("#,##0.00", Br);

    private static string FormatarChave(string chave) =>
        string.Join(' ', Enumerable.Range(0, (chave.Length + 3) / 4).Select(i => chave.Substring(i * 4, Math.Min(4, chave.Length - i * 4))));

    private static string FormatarNumero(string numero) =>
        long.TryParse(numero, out var n) ? n.ToString("000,000,000", Br).Replace(',', '.') : numero;

    private static string FormatarCep(string? cep) => cep is { Length: 8 } ? $"{cep[..5]}-{cep[5..]}" : cep ?? "";

    private static string FormatarDocumento(string? doc) => doc switch
    {
        { Length: 14 } c => $"{c[..2]}.{c[2..5]}.{c[5..8]}/{c[8..12]}-{c[12..]}",
        { Length: 11 } c => $"{c[..3]}.{c[3..6]}.{c[6..9]}-{c[9..]}",
        _ => doc ?? ""
    };
}
