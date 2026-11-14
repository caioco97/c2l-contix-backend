using Contix.Application.Features.Solicitacoes;
using Contix.Infrastructure.Planilhas;

namespace Contix.UnitTests.Infrastructure;

public class PlanilhaTests
{
    private static readonly string[] Cabecalho = ["CPF", "Rubrica", "Valor"];

    [Theory]
    [InlineData(FormatoPlanilha.Xlsx)]
    [InlineData(FormatoPlanilha.Csv)]
    public void Gera_e_le_de_volta(FormatoPlanilha formato)
    {
        var svc = new PlanilhaService();
        IReadOnlyList<string>[] linhas = [["123.456.789-09", "Horas; extras \"50%\"", "10,5"], ["", "Comissão", ""]];
        var bytes = svc.Gerar(Cabecalho, linhas, formato, "Rubricas");

        var lidas = svc.Ler(new MemoryStream(bytes), formato, 100);

        Assert.Equal(3, lidas.Count);
        Assert.Equal(Cabecalho, lidas[0].Celulas);
        Assert.Equal("Horas; extras \"50%\"", lidas[1].Celulas[1]);
        Assert.Equal("Comissão", lidas[2].Celulas[1]);
        Assert.Equal([1, 2, 3], lidas.Select(l => l.Numero));
    }

    [Fact]
    public void Csv_com_virgula_e_latin1()
    {
        var bytes = System.Text.Encoding.Latin1.GetBytes("CPF,Rubrica\nX,Comissão\n");
        var lidas = new PlanilhaService().Ler(new MemoryStream(bytes), FormatoPlanilha.Csv, 100);
        Assert.Equal("Comissão", lidas[1].Celulas[1]);
    }

    [Fact]
    public void Linhas_vazias_nao_contam_no_limite_e_guardam_o_numero()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("CPF;Rubrica\n;\n\nA;X\n;;\nB;Y\n");
        var lidas = new PlanilhaService().Ler(new MemoryStream(bytes), FormatoPlanilha.Csv, 3);
        Assert.Equal([1, 4, 6], lidas.Select(l => l.Numero));
    }

    [Fact]
    public void Para_de_ler_logo_depois_do_limite()
    {
        var csv = "A\n" + string.Concat(Enumerable.Range(1, 50).Select(i => $"{i}\n"));
        var lidas = new PlanilhaService().Ler(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv)), FormatoPlanilha.Csv, 10);
        Assert.Equal(11, lidas.Count);
    }

    [Fact]
    public void Xlsx_com_indice_de_linha_e_coluna_enormes_nao_expande_memoria()
    {
        using var ms = new MemoryStream();
        using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Create(ms, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            var wb = doc.AddWorkbookPart();
            wb.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();
            var ws = wb.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>();
            static DocumentFormat.OpenXml.Spreadsheet.Cell Celula(string referencia, string texto) => new()
            {
                CellReference = referencia,
                DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.InlineString,
                InlineString = new DocumentFormat.OpenXml.Spreadsheet.InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(texto)),
            };
            ws.Worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet(new DocumentFormat.OpenXml.Spreadsheet.SheetData(
                new DocumentFormat.OpenXml.Spreadsheet.Row(Celula("A1", "Rubrica")) { RowIndex = 1 },
                new DocumentFormat.OpenXml.Spreadsheet.Row(Celula("A1048576", "X"), Celula("ZZZZZZZZ1048576", "fora")) { RowIndex = 1048576 }));
            wb.Workbook.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Sheets(
                new DocumentFormat.OpenXml.Spreadsheet.Sheet { Id = wb.GetIdOfPart(ws), SheetId = 1, Name = "R" }));
        }

        var lidas = new PlanilhaService().Ler(new MemoryStream(ms.ToArray()), FormatoPlanilha.Xlsx, 10);

        Assert.Equal(2, lidas.Count);
        Assert.Equal(1048576, lidas[1].Numero);
        Assert.Equal(["X"], lidas[1].Celulas);
    }

    [Fact]
    public void Xlsx_invalido_vira_FormatException() =>
        Assert.Throws<FormatException>(() => new PlanilhaService().Ler(new MemoryStream("nao e zip"u8.ToArray()), FormatoPlanilha.Xlsx, 10));

    [Fact]
    public void Csv_escapa_formulas()
    {
        var bytes = new PlanilhaService().Gerar(["A"], [["=HYPERLINK(\"x\")"]], FormatoPlanilha.Csv, "x");
        Assert.Contains("'=HYPERLINK", System.Text.Encoding.UTF8.GetString(bytes));
    }
}
