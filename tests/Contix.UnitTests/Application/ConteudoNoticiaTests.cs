using Contix.Application.Features.Noticias;

namespace Contix.UnitTests.Application;

public class ConteudoNoticiaTests
{
    [Fact]
    public void Texto_simples_antigo_vira_paragrafos_escapados()
    {
        var html = ConteudoNoticia.ParaExibir("Primeiro <b>parágrafo</b>.\n\nSegunda\nlinha & fim");
        Assert.Equal("<p>Primeiro &lt;b&gt;parágrafo&lt;/b&gt;.</p><p>Segunda<br>linha &amp; fim</p>", html);
    }

    [Fact]
    public void Html_mantem_formatacao_permitida()
    {
        const string html = "<h2>Título</h2><p><strong>N</strong> <em>I</em> <u>S</u> <s>R</s></p><ul><li>a</li></ul><ol><li>b</li></ol><blockquote><p>c</p></blockquote>";
        Assert.Equal(html, ConteudoNoticia.ParaGravar(html));
    }

    [Theory]
    [InlineData("<p onclick=\"x()\" style=\"color:red\" class=\"c\">a</p>", "<p>a</p>")]
    [InlineData("<p>a<script>alert(1)</script></p>", "<p>a</p>")]
    [InlineData("<p><img src=x onerror=alert(1)>a</p>", "<p>a</p>")]
    [InlineData("<p><a href=\"javascript:alert(1)\">a</a></p>", "<p><a>a</a></p>")]
    [InlineData("<p><a href=\"/relativo\">a</a></p>", "<p><a>a</a></p>")]
    [InlineData("<p><iframe src=\"https://x\"></iframe>a</p>", "<p>a</p>")]
    [InlineData("<h1>T</h1><p><span>a</span></p>", "T<p>a</p>")]
    public void Html_perigoso_ou_fora_da_lista_e_removido(string entrada, string esperado) =>
        Assert.Equal(esperado, ConteudoNoticia.ParaGravar(entrada));

    [Fact]
    public void Paragrafos_vazios_nas_pontas_sao_removidos() =>
        Assert.Equal("<p>a</p><p></p><p>b</p>", ConteudoNoticia.ParaGravar("<p><br></p><p>a</p><p></p><p>b</p><p><br></p><p></p>"));

    [Fact]
    public void Links_exibidos_abrem_em_nova_aba_com_seguranca()
    {
        const string html = "<p><a href=\"https://gov.br\" target=\"_self\">gov</a></p>";
        Assert.Equal("<p><a href=\"https://gov.br\">gov</a></p>", ConteudoNoticia.ParaGravar(html));
        Assert.Equal("<p><a href=\"https://gov.br\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">gov</a></p>",
            ConteudoNoticia.ParaExibir(html));
    }

    [Fact]
    public void Texto_puro_sai_sem_tags_e_com_entidades_decodificadas()
    {
        var texto = NoticiaService.Resumir("<h2>Prazo</h2><p>Envie <strong>até</strong> &amp; confira.</p><ul><li>Um</li><li>Dois</li></ul>");
        Assert.Equal("Prazo Envie até & confira. Um Dois", texto);
    }
}
