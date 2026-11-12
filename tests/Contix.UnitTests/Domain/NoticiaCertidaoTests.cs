using Contix.Application.Features.Noticias;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;

namespace Contix.UnitTests.Domain;

public class NoticiaTests
{
    private static readonly DateTime Agora = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);

    private static Empresa NovaEmpresa(RegimeTributario regime) =>
        new() { RazaoSocial = "X", Documento = "1", RegimeTributario = regime };

    [Fact]
    public void Situacao_depende_das_datas()
    {
        var n = new Noticia();
        n.DefinirPeriodo(Agora.AddDays(1), null);
        Assert.Equal(SituacaoNoticia.Agendada, n.Situacao(Agora));

        n.DefinirPeriodo(Agora.AddDays(-2), Agora.AddDays(-1));
        Assert.Equal(SituacaoNoticia.Expirada, n.Situacao(Agora));

        n.DefinirPeriodo(Agora.AddDays(-2), Agora.AddDays(1));
        Assert.Equal(SituacaoNoticia.Publicada, n.Situacao(Agora));
    }

    [Fact]
    public void Expiracao_antes_da_publicacao_e_rejeitada() =>
        Assert.Throws<DomainException>(() => new Noticia().DefinirPeriodo(Agora, Agora.AddMinutes(-1)));

    [Fact]
    public void Publico_por_empresas_selecionadas_sincroniza_vinculos()
    {
        var a = NovaEmpresa(RegimeTributario.LucroReal);
        var b = NovaEmpresa(RegimeTributario.LucroReal);
        var n = new Noticia();

        n.DefinirPublico(PublicoNoticia.EmpresasSelecionadas, [a.Id], []);
        Assert.True(n.DestinadaA(a));
        Assert.False(n.DestinadaA(b));

        n.DefinirPublico(PublicoNoticia.EmpresasSelecionadas, [b.Id], []);
        Assert.Equal(b.Id, Assert.Single(n.Empresas).EmpresaId);

        Assert.Throws<DomainException>(() => n.DefinirPublico(PublicoNoticia.EmpresasSelecionadas, [], []));
    }

    [Fact]
    public void Publico_por_regime_descarta_empresas_selecionadas()
    {
        var simples = NovaEmpresa(RegimeTributario.SimplesNacional);
        var n = new Noticia();
        n.DefinirPublico(PublicoNoticia.EmpresasSelecionadas, [simples.Id], []);

        n.DefinirPublico(PublicoNoticia.RegimeTributario, [simples.Id], [RegimeTributario.LucroPresumido, RegimeTributario.LucroPresumido]);

        Assert.Empty(n.Empresas);
        Assert.Equal([RegimeTributario.LucroPresumido], n.Regimes);
        Assert.False(n.DestinadaA(simples));
        Assert.True(n.DestinadaA(NovaEmpresa(RegimeTributario.LucroPresumido)));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("  Texto   curto\n\ncom quebras ", "Texto curto com quebras")]
    public void Resumo_normaliza_espacos(string? texto, string esperado) =>
        Assert.Equal(esperado, NoticiaService.Resumir(texto));

    [Fact]
    public void Resumo_longo_e_cortado_em_palavra_inteira()
    {
        var texto = string.Join(' ', Enumerable.Repeat("palavra", 60));
        var resumo = NoticiaService.Resumir(texto);
        Assert.True(resumo.Length <= NoticiaService.TamanhoResumoAutomatico + 1);
        Assert.EndsWith("palavra…", resumo);
    }
}

public class CertidaoTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 12);

    [Theory]
    [InlineData(-1, ValidadeCertidao.Vencida)]
    [InlineData(0, ValidadeCertidao.AVencer)]
    [InlineData(30, ValidadeCertidao.AVencer)]
    [InlineData(31, ValidadeCertidao.Vigente)]
    public void Validade_calculada_em_relacao_a_hoje(int dias, ValidadeCertidao esperado) =>
        Assert.Equal(esperado, Certidao.Calcular(Hoje.AddDays(dias), Hoje));

    [Fact]
    public void Validade_anterior_a_emissao_e_rejeitada() =>
        Assert.Throws<DomainException>(() => new Certidao().DefinirDatas(Hoje, Hoje.AddDays(-1)));

    [Theory]
    [InlineData(SituacaoCertidao.Negativa, 10, true)]
    [InlineData(SituacaoCertidao.PositivaComEfeitoNegativa, 10, true)]
    [InlineData(SituacaoCertidao.Positiva, 10, false)]
    [InlineData(SituacaoCertidao.Negativa, -1, false)]
    public void Regular_exige_certidao_nao_positiva_e_vigente(SituacaoCertidao situacao, int dias, bool esperado) =>
        Assert.Equal(esperado, Certidao.EhRegular(situacao, Hoje.AddDays(dias), Hoje));
}
