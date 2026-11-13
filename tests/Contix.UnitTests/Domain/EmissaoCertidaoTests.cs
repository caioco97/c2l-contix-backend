using System.Text.Json;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;
using Contix.Infrastructure.Certidoes;

namespace Contix.UnitTests.Domain;

public class EmissaoCertidaoTests
{
    private static readonly DateTime Agora = new(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Hoje = DateOnly.FromDateTime(Agora);

    [Fact]
    public void Falha_temporaria_volta_para_a_fila_com_espera_ate_esgotar_as_tentativas()
    {
        var e = new EmissaoCertidao { Tipo = TipoCertidao.FederalPgfn, ProximaTentativaEm = Agora };

        Assert.False(e.RegistrarFalha("X", "fora do ar", retentavel: true, Agora));
        Assert.Equal(StatusEmissaoCertidao.Pendente, e.Status);
        Assert.Equal(Agora.AddMinutes(30), e.ProximaTentativaEm);

        Assert.False(e.RegistrarFalha("X", "fora do ar", retentavel: true, Agora));
        Assert.Equal(Agora.AddHours(2), e.ProximaTentativaEm);

        Assert.True(e.RegistrarFalha("X", "fora do ar", retentavel: true, Agora));
        Assert.Equal(StatusEmissaoCertidao.Falha, e.Status);
        Assert.Equal(3, e.Tentativas);
        Assert.Equal(Agora, e.ConcluidaEm);
    }

    [Fact]
    public void Recusa_do_orgao_e_falha_definitiva_na_primeira_tentativa()
    {
        var e = new EmissaoCertidao();
        Assert.True(e.RegistrarFalha("X", "há débitos", retentavel: false, Agora));
        Assert.Equal(StatusEmissaoCertidao.Falha, e.Status);
        Assert.Throws<DomainException>(() => e.RegistrarSucesso(Guid.NewGuid(), "X", null, Agora));
    }

    [Fact]
    public void Sucesso_conclui_e_guarda_a_certidao()
    {
        var e = new EmissaoCertidao();
        var certidao = Guid.NewGuid();
        e.RegistrarSucesso(certidao, "Simulado", null, Agora);
        Assert.Equal(StatusEmissaoCertidao.Emitida, e.Status);
        Assert.Equal(certidao, e.CertidaoId);
        Assert.Equal(1, e.Tentativas);
    }

    [Theory]
    [InlineData(null, null, true)]                                     // nunca emitida
    [InlineData(30, SituacaoCertidao.Negativa, false)]                 // longe do vencimento
    [InlineData(10, SituacaoCertidao.Negativa, true)]                  // dentro da antecedência
    [InlineData(-5, SituacaoCertidao.Negativa, true)]                  // vencida
    [InlineData(90, SituacaoCertidao.Positiva, true)]                  // positiva: tenta regularizar
    [InlineData(90, SituacaoCertidao.PositivaComEfeitoNegativa, false)]
    public void Precisa_emitir_conforme_validade_e_situacao(int? diasParaVencer, SituacaoCertidao? situacao, bool esperado)
    {
        var config = new ConfiguracaoEmissaoCertidao();
        config.DefinirAntecedencia(10);
        DateOnly? validade = diasParaVencer is null ? null : Hoje.AddDays(diasParaVencer.Value);
        Assert.Equal(esperado, config.PrecisaEmitir(validade, situacao, Hoje));
    }

    [Fact]
    public void Antecedencia_fora_do_limite_e_rejeitada() =>
        Assert.Throws<DomainException>(() => new ConfiguracaoEmissaoCertidao().DefinirAntecedencia(61));

    [Fact]
    public void Pdf_simulado_e_um_pdf()
    {
        var pdf = PdfSimples.Gerar("Teste (com parênteses)", "linha 2");
        Assert.True(PdfSimples.EhPdf(pdf));
        Assert.EndsWith("%%EOF\n", System.Text.Encoding.ASCII.GetString(pdf));
    }
}

public class InfosimplesParserTests
{
    private static InfosimplesParser.Resultado Analisar(TipoCertidao tipo, string json)
    {
        using var doc = JsonDocument.Parse(json);
        return InfosimplesParser.Analisar(tipo, doc.RootElement.Clone());
    }

    [Fact]
    public void Cnd_federal_negativa()
    {
        var r = Analisar(TipoCertidao.FederalPgfn, """
            { "code": 200, "code_message": "OK", "errors": [],
              "data": [ { "tipo": "Certidão Negativa", "certidao_codigo": "ABCD.1234.EF56.7890",
                          "emissao_data": "13/11/2026", "validade_data": "12/05/2027",
                          "site_receipt": "https://storage.infosimples.com/r/1.pdf" } ],
              "site_receipts": [ "https://storage.infosimples.com/r/1.html" ] }
            """);
        Assert.Null(r.Erro);
        Assert.Equal("https://storage.infosimples.com/r/1.pdf", r.UrlPdf);
        Assert.Equal(SituacaoCertidao.Negativa, r.Dados!.Situacao);
        Assert.Equal("ABCD.1234.EF56.7890", r.Dados.Numero);
        Assert.Equal(new DateOnly(2026, 11, 13), r.Dados.Emissao);
        Assert.Equal(new DateOnly(2027, 5, 12), r.Dados.Validade);
    }

    [Fact]
    public void Positiva_com_efeitos_de_negativa_e_recibo_na_raiz()
    {
        var r = Analisar(TipoCertidao.FederalPgfn, """
            { "code": 200, "data": [ { "tipo": "Certidão Positiva com Efeitos de Negativa",
              "emissao_data": "13/11/2026", "validade_data": "12/05/2027" } ],
              "site_receipts": [ "https://x/1.pdf" ] }
            """);
        Assert.Equal(SituacaoCertidao.PositivaComEfeitoNegativa, r.Dados!.Situacao);
        Assert.Equal("https://x/1.pdf", r.UrlPdf);
    }

    [Fact]
    public void Sem_validade_o_orgao_nao_emitiu_e_nao_adianta_repetir()
    {
        var r = Analisar(TipoCertidao.FederalPgfn, """
            { "code": 200, "data": [ { "conseguiu_emitir_certidao_negativa": false,
              "mensagem": "As informações disponíveis não permitem a emissão da certidão." } ] }
            """);
        Assert.False(r.Retentavel);
        Assert.Contains("não permitem a emissão", r.Erro);
    }

    [Theory]
    [InlineData(605, true)]   // timeout na fonte
    [InlineData(609, true)]   // fonte instável
    [InlineData(601, false)]  // token inválido
    [InlineData(606, false)]  // parâmetros inválidos
    public void Codigos_de_erro_da_api(int codigo, bool retentavel)
    {
        var r = Analisar(TipoCertidao.Trabalhista, $$"""{ "code": {{codigo}}, "code_message": "Erro", "errors": ["detalhe"] }""");
        Assert.Equal(retentavel, r.Retentavel);
        Assert.Contains($"({codigo})", r.Erro);
        Assert.Contains("detalhe", r.Erro);
    }

    [Fact]
    public void Crf_regular_do_fgts()
    {
        var r = Analisar(TipoCertidao.Fgts, """
            { "code": 200, "data": [ { "situacao": "REGULAR", "crf": "2026111300000000000001",
              "validade_inicio_data": "13/11/2026", "validade_fim_data": "12/12/2026", "site_receipt": "https://x/crf.pdf" } ] }
            """);
        Assert.Equal(SituacaoCertidao.Negativa, r.Dados!.Situacao);
        Assert.Equal("2026111300000000000001", r.Dados.Numero);
        Assert.Equal(new DateOnly(2026, 12, 12), r.Dados.Validade);
    }

    [Fact]
    public void Empregador_irregular_no_fgts_nao_tem_crf()
    {
        var r = Analisar(TipoCertidao.Fgts, """
            { "code": 200, "data": [ { "situacao": "IRREGULAR", "validade_fim_data": "12/12/2026" } ] }
            """);
        Assert.NotNull(r.Erro);
        Assert.False(r.Retentavel);
    }

    [Fact]
    public void Cndt_positiva_pela_flag()
    {
        var r = Analisar(TipoCertidao.Trabalhista, """
            { "code": 200, "data": [ { "conseguiu_emitir_certidao_negativa": false, "certidao_codigo": "123/2026",
              "emissao_data": "13/11/2026", "validade_data": "12/05/2027", "site_receipt": "https://x/cndt.pdf" } ] }
            """);
        Assert.Equal(SituacaoCertidao.Positiva, r.Dados!.Situacao);
    }
}
