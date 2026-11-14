using Contix.Application.Features.Relatorios;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Contix.Infrastructure.Pdf;

/// <summary>
/// Relatórios em PDF (QuestPDF). Licença Community: gratuita para empresas com receita anual bruta abaixo de US$ 1 milhão
/// (https://www.questpdf.com/license/); acima disso é preciso a licença Professional.
/// </summary>
public sealed class ExportadorPdf : IExportadorPdf
{
    static ExportadorPdf() => QuestPdfConfig.Configurar();

    public byte[] Relatorio(RelatorioTabela t) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(t.Colunas.Count > 6 ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.Margin(24);
            page.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken4));

            page.Header().PaddingBottom(8).Column(c =>
            {
                c.Item().Row(r =>
                {
                    r.RelativeItem().Text(t.Titulo).FontSize(14).Bold().FontColor("#1E3A8A");
                    r.ConstantItem(160).AlignRight().Text($"Contix · {t.GeradoEm.AddHours(-3):dd/MM/yyyy HH:mm}").FontColor(Colors.Grey.Darken1);
                });
                c.Item().Text(t.Subtitulo).FontColor(Colors.Grey.Darken2);
                if (t.Resumo.Count > 0)
                    c.Item().PaddingTop(6).Row(r =>
                    {
                        foreach (var item in t.Resumo)
                            r.RelativeItem().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Column(k =>
                            {
                                k.Item().Text(item.Rotulo).FontSize(7).FontColor(Colors.Grey.Darken1);
                                k.Item().Text(item.Valor).Bold();
                            });
                    });
                if (t.Truncado)
                    c.Item().PaddingTop(4).Text($"Mostrando as primeiras {t.Linhas.Count} linhas. Refine os filtros para ver o restante.")
                        .FontColor(Colors.Orange.Darken2);
            });

            page.Content().Table(table =>
            {
                table.ColumnsDefinition(cols => { foreach (var _ in t.Colunas) cols.RelativeColumn(); });
                table.Header(h =>
                {
                    foreach (var col in t.Colunas)
                        h.Cell().Background("#EFF6FF").BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3).Text(col).Bold();
                });
                var i = 0;
                foreach (var linha in t.Linhas)
                {
                    string fundo = i++ % 2 == 0 ? "#FFFFFF" : "#F8FAFC";
                    foreach (var valor in linha)
                        table.Cell().Background(fundo).BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(valor);
                }
            });

            page.Footer().AlignCenter().Text(x =>
            {
                x.Span("Página ").FontColor(Colors.Grey.Darken1);
                x.CurrentPageNumber();
                x.Span(" de ").FontColor(Colors.Grey.Darken1);
                x.TotalPages();
            });
        })).GeneratePdf();
}

internal static class QuestPdfConfig
{
    private static int _configurado;

    public static void Configurar()
    {
        if (Interlocked.Exchange(ref _configurado, 1) == 1) return;
        QuestPDF.Settings.License = LicenseType.Community;
        // Servidores sem fontes do sistema: usa as fontes embutidas no QuestPDF.
        QuestPDF.Settings.UseEnvironmentFonts = false;
    }
}
