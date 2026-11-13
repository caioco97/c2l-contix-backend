using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Contix.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class NoticiasCertidoesTests(ContixApiFactory factory)
{
    /// <summary>Tokens reaproveitados entre os testes da classe, para não esbarrar no rate limit do login.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Tokens = new();

    private async Task<HttpClient> LoginAsync(string email)
    {
        var http = factory.CreateClient();
        if (!Tokens.TryGetValue(email, out var token))
        {
            var body = await (await http.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = "Contix@123" }))
                .Content.ReadFromJsonAsync<JsonObject>();
            token = Tokens[email] = body!["accessToken"]!.GetValue<string>();
        }
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<JsonNode> JsonAsync(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {texto}");
        return JsonNode.Parse(texto)!;
    }

    private static async Task<string> EmpresaDemoAsync(HttpClient http) =>
        (await JsonAsync(await http.GetAsync("/api/v1/empresas")))["items"]![0]!["id"]!.GetValue<string>();

    private static ByteArrayContent Arquivo(string conteudo, string contentType) =>
        new(System.Text.Encoding.UTF8.GetBytes(conteudo)) { Headers = { ContentType = new(contentType) } };

    private static IEnumerable<string> Ids(JsonNode lista) =>
        lista.AsArray().Select(n => n!["id"]!.GetValue<string>());

    [Fact]
    public async Task Noticia_publicada_para_empresa_selecionada_ate_a_leitura()
    {
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var empresaId = await EmpresaDemoAsync(cliente);

        // Cliente não publica nem vê a gestão de notícias.
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/v1/noticias")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsync("/api/v1/noticias",
            new MultipartFormDataContent { { new StringContent("x"), "titulo" }, { new StringContent("y"), "conteudo" } })).StatusCode);

        var semEmpresa = await colaborador.PostAsync("/api/v1/noticias", new MultipartFormDataContent
        {
            { new StringContent("Comunicado"), "titulo" }, { new StringContent("Texto"), "conteudo" },
            { new StringContent("EmpresasSelecionadas"), "publico" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, semEmpresa.StatusCode);

        var criada = await JsonAsync(await colaborador.PostAsync("/api/v1/noticias", new MultipartFormDataContent
        {
            { new StringContent("Comunicado importante"), "titulo" },
            { new StringContent("Primeiro parágrafo.\n\nSegundo parágrafo."), "conteudo" },
            { new StringContent("EmpresasSelecionadas"), "publico" },
            { new StringContent(empresaId), "empresaIds" },
            { Arquivo("png", "image/png"), "imagem", "capa.png" },
            { Arquivo("conteudo-anexo", "application/pdf"), "anexos", "guia.pdf" }
        }));
        var noticiaId = criada["id"]!.GetValue<string>();
        Assert.Equal("Publicada", criada["situacao"]!.GetValue<string>());
        Assert.True(criada["possuiImagem"]!.GetValue<bool>());
        var anexoId = criada["anexos"]![0]!["id"]!.GetValue<string>();

        var naoLidas = await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/noticias?status=NaoLidos"));
        var item = naoLidas["items"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == noticiaId)!;
        Assert.Equal("Primeiro parágrafo. Segundo parágrafo.", item["resumo"]!.GetValue<string>());
        Assert.Equal(1, item["quantidadeAnexos"]!.GetValue<int>());

        var detalhe = await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/noticias/{noticiaId}"));
        Assert.False(detalhe["lida"]!.GetValue<bool>());
        // Texto simples é gravado como parágrafos HTML.
        Assert.Equal("<p>Primeiro parágrafo.</p><p>Segundo parágrafo.</p>", detalhe["conteudo"]!.GetValue<string>());
        Assert.Equal("png", await (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/noticias/{noticiaId}/imagem")).Content.ReadAsStringAsync());
        Assert.Equal("conteudo-anexo", await (await cliente.GetAsync(
            $"/api/v1/empresas/{empresaId}/noticias/{noticiaId}/anexos/{anexoId}/download")).Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await cliente.PostAsync($"/api/v1/empresas/{empresaId}/noticias/{noticiaId}/lida", null)).StatusCode);
        // Idempotente.
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.PostAsync($"/api/v1/empresas/{empresaId}/noticias/{noticiaId}/lida", null)).StatusCode);

        var lidas = await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/noticias?status=Lidos"));
        Assert.Contains(noticiaId, Ids(lidas["items"]!));
        Assert.DoesNotContain(noticiaId, Ids((await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/noticias?status=NaoLidos")))["items"]!));

        // Alcance para o escritório.
        var leituras = await JsonAsync(await colaborador.GetAsync($"/api/v1/noticias/{noticiaId}/leituras"));
        var leitura = Assert.Single(leituras.AsArray())!;
        Assert.True(leitura["lida"]!.GetValue<bool>());
        Assert.Equal(1, leitura["leitores"]!.GetValue<int>());

        var lista = await JsonAsync(await colaborador.GetAsync("/api/v1/noticias?busca=importante"));
        var resumo = Assert.Single(lista["items"]!.AsArray())!;
        Assert.Equal(1, resumo["empresasDestinatarias"]!.GetValue<int>());
        Assert.Equal(1, resumo["empresasLeram"]!.GetValue<int>());

        // Edição: remove o anexo e a imagem.
        var editada = await JsonAsync(await colaborador.PutAsync($"/api/v1/noticias/{noticiaId}", new MultipartFormDataContent
        {
            { new StringContent("Comunicado importante (atualizado)"), "titulo" },
            { new StringContent("Novo texto."), "conteudo" },
            { new StringContent("Todos"), "publico" },
            { new StringContent(anexoId), "anexosRemovidos" },
            { new StringContent("true"), "removerImagem" }
        }));
        Assert.Empty(editada["anexos"]!.AsArray());
        Assert.False(editada["possuiImagem"]!.GetValue<bool>());
        Assert.Equal("Todos", editada["publico"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.NoContent, (await colaborador.DeleteAsync($"/api/v1/noticias/{noticiaId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/noticias/{noticiaId}")).StatusCode);
    }

    [Fact]
    public async Task Conteudo_em_html_e_sanitizado_e_resumo_sai_sem_tags()
    {
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var empresaId = await EmpresaDemoAsync(cliente);

        var html = "<h2>Prazo</h2><p>Envie <strong>até sexta</strong>. <a href=\"javascript:alert(1)\">x</a> "
                   + "<a href=\"https://gov.br\">gov</a></p><script>alert(1)</script><img src=x onerror=alert(1)>";
        var criada = await JsonAsync(await colaborador.PostAsync("/api/v1/noticias", new MultipartFormDataContent
        {
            { new StringContent("Comunicado em HTML"), "titulo" }, { new StringContent(html), "conteudo" },
            { new StringContent("Todos"), "publico" }
        }));
        var noticiaId = criada["id"]!.GetValue<string>();
        Assert.Equal("<h2>Prazo</h2><p>Envie <strong>até sexta</strong>. <a>x</a> <a href=\"https://gov.br\">gov</a></p>",
            criada["conteudo"]!.GetValue<string>());

        var detalhe = await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/noticias/{noticiaId}"));
        var conteudo = detalhe["conteudo"]!.GetValue<string>();
        Assert.Contains("<a href=\"https://gov.br\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">gov</a>", conteudo);
        Assert.DoesNotContain("script", conteudo);
        Assert.DoesNotContain("javascript", conteudo);
        Assert.Equal("Prazo Envie até sexta. x gov", detalhe["resumo"]!.GetValue<string>());

        var semTexto = await colaborador.PostAsync("/api/v1/noticias", new MultipartFormDataContent
        {
            { new StringContent("Vazia"), "titulo" }, { new StringContent("<p><br></p>"), "conteudo" },
            { new StringContent("Todos"), "publico" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, semTexto.StatusCode);
    }

    [Fact]
    public async Task Noticia_agendada_ou_de_outro_regime_nao_aparece_no_portal()
    {
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var empresaId = await EmpresaDemoAsync(cliente);

        var agendada = await JsonAsync(await colaborador.PostAsync("/api/v1/noticias", new MultipartFormDataContent
        {
            { new StringContent("Agendada"), "titulo" }, { new StringContent("Texto"), "conteudo" },
            { new StringContent(DateTime.UtcNow.AddDays(3).ToString("O")), "publicarEm" }
        }));
        Assert.Equal("Agendada", agendada["situacao"]!.GetValue<string>());

        // A empresa demo é do Simples Nacional.
        var lucroReal = await JsonAsync(await colaborador.PostAsync("/api/v1/noticias", new MultipartFormDataContent
        {
            { new StringContent("Só Lucro Real"), "titulo" }, { new StringContent("Texto"), "conteudo" },
            { new StringContent("RegimeTributario"), "publico" }, { new StringContent("LucroReal"), "regimes" }
        }));

        var portal = await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/noticias?pageSize=100"));
        var ids = Ids(portal["items"]!).ToList();
        Assert.DoesNotContain(agendada["id"]!.GetValue<string>(), ids);
        Assert.DoesNotContain(lucroReal["id"]!.GetValue<string>(), ids);
        // As do seed (todas ou Simples/MEI) aparecem.
        Assert.Contains(portal["items"]!.AsArray(), n => n!["titulo"]!.GetValue<string>() == "Novo prazo para a DEFIS");
        Assert.Equal(HttpStatusCode.NotFound,
            (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/noticias/{agendada["id"]}")).StatusCode);

        var resumo = (await JsonAsync(await colaborador.GetAsync("/api/v1/noticias?busca=Lucro%20Real")))["items"]![0]!;
        Assert.Equal(0, resumo["empresasDestinatarias"]!.GetValue<int>());
    }

    [Fact]
    public async Task Certidoes_cadastro_renovacao_painel_e_download()
    {
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var empresaId = await EmpresaDemoAsync(cliente);

        var atuais = (await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/certidoes?somenteAtuais=true"))).AsArray();
        Assert.Equal(4, atuais.Count);
        var trabalhista = atuais.Single(c => c!["tipo"]!.GetValue<string>() == "Trabalhista")!;
        Assert.Equal("Vencida", trabalhista["statusValidade"]!.GetValue<string>());
        Assert.False(trabalhista["regular"]!.GetValue<bool>());
        Assert.Equal("AVencer", atuais.Single(c => c!["tipo"]!.GetValue<string>() == "Fgts")!["statusValidade"]!.GetValue<string>());

        var painel = await JsonAsync(await colaborador.GetAsync("/api/v1/certidoes/painel"));
        Assert.Contains(trabalhista["id"]!.GetValue<string>(), Ids(painel["itens"]!));
        Assert.True(painel["vencidas"]!.GetValue<int>() >= 1);
        Assert.True(painel["aVencer30Dias"]!.GetValue<int>() >= 1);

        // Cliente só consulta.
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsync($"/api/v1/empresas/{empresaId}/certidoes",
            new MultipartFormDataContent { { new StringContent("Fgts"), "tipo" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/v1/certidoes/painel")).StatusCode);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var invalida = await colaborador.PostAsync($"/api/v1/empresas/{empresaId}/certidoes", new MultipartFormDataContent
        {
            { new StringContent("Trabalhista"), "tipo" },
            { new StringContent(hoje.ToString("yyyy-MM-dd")), "emissao" },
            { new StringContent(hoje.AddDays(-1).ToString("yyyy-MM-dd")), "validade" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);

        // Renovação da trabalhista.
        var nova = await JsonAsync(await colaborador.PostAsync($"/api/v1/empresas/{empresaId}/certidoes", new MultipartFormDataContent
        {
            { new StringContent("Trabalhista"), "tipo" },
            { new StringContent("Negativa"), "situacao" },
            { new StringContent("123/2026"), "numero" },
            { new StringContent(hoje.ToString("yyyy-MM-dd")), "emissao" },
            { new StringContent(hoje.AddDays(180).ToString("yyyy-MM-dd")), "validade" },
            { Arquivo("pdf-cndt", "application/pdf"), "arquivo", "cndt.pdf" }
        }));
        var novaId = nova["id"]!.GetValue<string>();
        Assert.True(nova["atual"]!.GetValue<bool>());
        Assert.Equal("Vigente", nova["statusValidade"]!.GetValue<string>());
        Assert.Equal("cndt.pdf", nova["nomeArquivo"]!.GetValue<string>());

        var historico = (await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/certidoes?tipo=Trabalhista"))).AsArray();
        Assert.Equal(2, historico.Count);
        Assert.False(historico.Single(c => c!["id"]!.GetValue<string>() == trabalhista["id"]!.GetValue<string>())!["atual"]!.GetValue<bool>());

        // A vencida renovada sai do painel.
        painel = await JsonAsync(await colaborador.GetAsync("/api/v1/certidoes/painel"));
        Assert.DoesNotContain(trabalhista["id"]!.GetValue<string>(), Ids(painel["itens"]!));

        var download = await cliente.GetAsync($"/api/v1/empresas/{empresaId}/certidoes/{novaId}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("pdf-cndt", await download.Content.ReadAsStringAsync());

        var alterada = await JsonAsync(await colaborador.PutAsync($"/api/v1/empresas/{empresaId}/certidoes/{novaId}", new MultipartFormDataContent
        {
            { new StringContent("Trabalhista"), "tipo" },
            { new StringContent("Positiva"), "situacao" },
            { new StringContent(hoje.ToString("yyyy-MM-dd")), "emissao" },
            { new StringContent(hoje.AddDays(180).ToString("yyyy-MM-dd")), "validade" },
            { new StringContent("true"), "removerArquivo" }
        }));
        Assert.False(alterada["possuiArquivo"]!.GetValue<bool>());
        Assert.False(alterada["regular"]!.GetValue<bool>());
        Assert.Contains(novaId, Ids((await JsonAsync(await colaborador.GetAsync("/api/v1/certidoes/painel")))["itens"]!));

        Assert.Equal(HttpStatusCode.NoContent, (await colaborador.DeleteAsync($"/api/v1/empresas/{empresaId}/certidoes/{novaId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/certidoes/{novaId}")).StatusCode);
    }
}
