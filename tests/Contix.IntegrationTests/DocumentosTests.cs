using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Contix.IntegrationTests;

/// <summary>Origem, competência, quem visualizou/baixou e histórico de auditoria dos documentos.</summary>
[Collection(nameof(ApiCollection))]
public sealed class DocumentosTests(ContixApiFactory factory)
{
    private const string Senha = "Contix@123";

    private async Task<HttpClient> LoginAsync(string email)
    {
        var http = factory.CreateClient();
        var response = await http.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = Senha });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!["accessToken"]!.GetValue<string>());
        return http;
    }

    private static async Task<JsonNode> JsonAsync(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!;

    private static async Task<(string EmpresaId, Dictionary<string, string> Pastas)> EmpresaAsync(HttpClient http)
    {
        var empresaId = (await JsonAsync(await http.GetAsync("/api/v1/empresas?busca=DEMO")))["items"]![0]!["id"]!.GetValue<string>();
        var pastas = (await JsonAsync(await http.GetAsync($"/api/v1/empresas/{empresaId}/pastas"))).AsArray()
            .ToDictionary(p => p!["nome"]!.GetValue<string>(), p => p!["id"]!.GetValue<string>());
        return (empresaId, pastas);
    }

    private static async Task<string> UploadAsync(HttpClient http, string empresaId, string pastaId, string nome, string? competencia = null)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(pastaId), "pastaId" },
            { new ByteArrayContent("conteúdo"u8.ToArray()) { Headers = { ContentType = new("text/plain") } }, "arquivos", nome }
        };
        if (competencia is not null) form.Add(new StringContent(competencia), "competencia");
        var r = await http.PostAsync($"/api/v1/empresas/{empresaId}/documentos", form);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonAsync(r))[0]!["id"]!.GetValue<string>();
    }

    private static List<string> Ids(JsonNode pagina) =>
        pagina["items"]!.AsArray().Select(i => i!["id"]!.GetValue<string>()).ToList();

    [Fact]
    public async Task Filtra_por_competencia_e_origem()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var (empresaId, pastas) = await EmpresaAsync(admin);
        var sufixo = Guid.NewGuid().ToString("N")[..8];

        var guiaOutubro = await UploadAsync(admin, empresaId, pastas["Fiscal"], $"das-{sufixo}.pdf", "2026-10-15");
        var guiaSetembro = await UploadAsync(admin, empresaId, pastas["Fiscal"], $"das-set-{sufixo}.pdf", "2026-09-01");
        var extrato = await UploadAsync(cliente, empresaId, pastas["Financeiro"], $"extrato-{sufixo}.pdf");

        var doc = await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{guiaOutubro}"));
        Assert.Equal("2026-10-01", doc["competencia"]!.GetValue<string>());
        Assert.Equal("Escritorio", doc["origem"]!.GetValue<string>());

        var outubro = Ids(await JsonAsync(await admin.GetAsync(
            $"/api/v1/empresas/{empresaId}/documentos?competencia=2026-10-01&busca={sufixo}")));
        Assert.Contains(guiaOutubro, outubro);
        Assert.DoesNotContain(guiaSetembro, outubro);

        // Sem pasta: "Recebidos do cliente" em todas as pastas.
        var doCliente = Ids(await JsonAsync(await admin.GetAsync(
            $"/api/v1/empresas/{empresaId}/documentos?origem=Cliente&busca={sufixo}")));
        Assert.Equal([extrato], doCliente);
    }

    [Fact]
    public async Task Escritorio_ve_quem_visualizou_e_baixou_e_o_historico()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var (empresaId, pastas) = await EmpresaAsync(admin);
        var docId = await UploadAsync(admin, empresaId, pastas["Pessoal"], $"folha-{Guid.NewGuid():N}.pdf", "2026-10-01");

        var antes = await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{docId}/acessos"));
        var clienteDemo = antes["usuarios"]!.AsArray().Single(u => u!["email"]!.GetValue<string>() == "cliente@contix.dev")!;
        Assert.True(clienteDemo["destinatario"]!.GetValue<bool>());
        Assert.Null(clienteDemo["visualizadoEm"]?.GetValue<DateTime?>());
        Assert.Equal(0, antes["destinatariosQueVisualizaram"]!.GetValue<int>());
        // Quem publicou não aparece como acesso.
        Assert.DoesNotContain(antes["usuarios"]!.AsArray(), u => u!["email"]!.GetValue<string>() == "admin@contix.dev");

        Assert.Equal(HttpStatusCode.NoContent, (await cliente.PostAsync($"/api/v1/empresas/{empresaId}/documentos/{docId}/lido", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{docId}/download")).StatusCode);
        var zip = await cliente.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/documentos/download", new { documentoIds = new[] { docId } });
        Assert.Equal(HttpStatusCode.OK, zip.StatusCode);

        var depois = await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{docId}/acessos"));
        var acesso = depois["usuarios"]!.AsArray().Single(u => u!["email"]!.GetValue<string>() == "cliente@contix.dev")!;
        Assert.NotNull(acesso["visualizadoEm"]);
        Assert.NotNull(acesso["ultimoDownloadEm"]);
        Assert.Equal(2, acesso["downloads"]!.GetValue<int>());
        Assert.True(depois["destinatariosQueVisualizaram"]!.GetValue<int>() >= 1);

        var lista = await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{docId}"));
        Assert.True(lista["visualizacoesDestinatarios"]!.GetValue<int>() >= 1);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/documentos/enviar",
            new { documentoIds = new[] { docId }, mensagem = "Segue a folha." })).StatusCode);

        var historico = (await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{docId}/historico"))).AsArray();
        var tipos = historico.Select(e => e!["tipo"]!.GetValue<string>()).Reverse().ToList();
        Assert.Equal(["Upload", "Visualizacao", "Download", "Download", "Envio"], tipos);
        Assert.Equal("Cliente Demo", historico.First(e => e!["tipo"]!.GetValue<string>() == "Download")!["usuarioNome"]!.GetValue<string>());
        Assert.Contains("Cliente Demo", historico[0]!["detalhe"]!.GetValue<string>());

        // Acessos e histórico são do escritório.
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{docId}/acessos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{docId}/historico")).StatusCode);
    }

    [Fact]
    public async Task Historico_registra_o_ip_de_origem_de_cada_acao()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var (empresaId, pastas) = await EmpresaAsync(admin);
        var docId = await UploadAsync(admin, empresaId, pastas["Fiscal"], $"guia-{Guid.NewGuid():N}.pdf");

        // O nginx do frontend repassa o IP real em X-Forwarded-For; o proxy local (loopback) é confiável.
        using var download = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/empresas/{empresaId}/documentos/{docId}/download");
        download.Headers.Add("X-Forwarded-For", "203.0.113.45");
        Assert.Equal(HttpStatusCode.OK, (await cliente.SendAsync(download)).StatusCode);

        using var envio = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/empresas/{empresaId}/documentos/enviar")
        {
            Content = JsonContent.Create(new { documentoIds = new[] { docId } })
        };
        envio.Headers.Add("X-Forwarded-For", "2001:db8::7");
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(envio)).StatusCode);

        var historico = (await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{docId}/historico"))).AsArray();
        string? Ip(string tipo) => historico.First(e => e!["tipo"]!.GetValue<string>() == tipo)!["ip"]?.GetValue<string>();
        Assert.Equal("203.0.113.45", Ip("Download"));
        Assert.Equal("2001:db8::7", Ip("Envio"));
        Assert.True(historico.All(e => e!.AsObject().ContainsKey("ip")));
    }

    [Fact]
    public async Task Documento_do_cliente_tem_o_escritorio_como_destinatario_e_exclusao_fica_no_historico()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var (empresaId, pastas) = await EmpresaAsync(admin);
        var docId = await UploadAsync(cliente, empresaId, pastas["Financeiro"], $"nota-{Guid.NewGuid():N}.pdf");

        var acessos = await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{docId}/acessos"));
        Assert.Equal("Cliente", acessos["origem"]!.GetValue<string>());
        Assert.All(acessos["usuarios"]!.AsArray().Where(u => u!["destinatario"]!.GetValue<bool>()),
            u => Assert.True(u!["ehDoEscritorio"]!.GetValue<bool>()));

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{docId}/download")).StatusCode);
        var doc = await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{docId}"));
        Assert.Equal(1, doc["visualizacoesDestinatarios"]!.GetValue<int>());

        Assert.Equal(HttpStatusCode.NoContent, (await cliente.DeleteAsync($"/api/v1/empresas/{empresaId}/documentos/{docId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{docId}")).StatusCode);
    }
}
