using Contix.Application.Features.Relatorios;
using Contix.Infrastructure.Pdf;
using Contix.Tests.Support;

namespace Contix.UnitTests.Infrastructure;

public class PdfTests
{
    [Fact]
    public void Padroes_do_code128_tem_11_modulos_e_o_stop_13()
    {
        Assert.Equal(107, Code128.Padroes.Count);
        Assert.All(Code128.Padroes.Take(106), p => Assert.Equal(11, p.Sum(c => c - '0')));
        Assert.Equal(13, Code128.Padroes[106].Sum(c => c - '0'));
        Assert.Equal(Code128.Padroes.Count, Code128.Padroes.Distinct().Count());
    }

    [Fact]
    public void Code128C_calcula_o_digito_verificador()
    {
        // Exemplo clássico: "102030" -> start C (105), 10, 20, 30; (105 + 10*1 + 20*2 + 30*3) % 103 = 39.
        Assert.Equal([105, 10, 20, 30, 39, 106], Code128.Codificar("102030"));
        Assert.Throws<ArgumentException>(() => Code128.Codificar("123"));
        Assert.StartsWith("<svg", Code128.Svg("1234"));
    }

    [Fact]
    public void Gera_o_danfe_a_partir_do_xml()
    {
        var emit = CertificadoTeste.NovoCnpj();
        var chave = CertificadoTeste.Chave(emit, 123);
        var pdf = new GeradorDanfe().Gerar(CertificadoTeste.NfeProc(chave, emit, CertificadoTeste.NovoCnpj(), 1500m));
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));

        var modelo = DanfeModelo.Ler(CertificadoTeste.NfeProc(chave, emit, CertificadoTeste.NovoCnpj(), 1500m));
        Assert.Equal(chave, modelo.Chave);
        Assert.Equal(1500m, modelo.ValorTotal);
        Assert.Equal("135260000012345", modelo.Protocolo);
    }

    [Fact]
    public void Gera_o_pdf_do_relatorio()
    {
        var tabela = new RelatorioTabela("Relatório de tarefas", "Período: 01/11/2026 a 30/11/2026", ["Nº", "Tarefa"],
            [["#1", "DCTFWeb — 10/2026"], ["#2", "Folha — 10/2026"]], [new("Tarefas", "2")], 2, false, DateTime.UtcNow);
        var pdf = new ExportadorPdf().Relatorio(tabela);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
