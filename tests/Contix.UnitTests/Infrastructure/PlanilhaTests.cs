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
        Assert.Equal(Cabecalho, lidas[0]);
        Assert.Equal("Horas; extras \"50%\"", lidas[1][1]);
        Assert.Equal("Comissão", lidas[2][1]);
    }

    [Fact]
    public void Csv_com_virgula_e_latin1()
    {
        var bytes = System.Text.Encoding.Latin1.GetBytes("CPF,Rubrica\nX,Comissão\n");
        var lidas = new PlanilhaService().Ler(new MemoryStream(bytes), FormatoPlanilha.Csv, 100);
        Assert.Equal("Comissão", lidas[1][1]);
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
