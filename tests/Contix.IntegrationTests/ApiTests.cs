using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Contix.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class ApiTests(ContixApiFactory factory)
{
    private const string Senha = "Contix@123";

    private async Task<HttpClient> ClienteAutenticadoAsync(string email, string senha = Senha)
    {
        var http = factory.CreateClient();
        var response = await http.PostAsJsonAsync("/api/v1/auth/login", new { email, senha });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!["accessToken"]!.GetValue<string>());
        return http;
    }

    private static async Task<JsonNode> JsonAsync(HttpResponseMessage r) =>
        JsonNode.Parse(await r.Content.ReadAsStringAsync())!;

    [Fact]
    public async Task Login_invalido_retorna_401_problem_details()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = "admin@contix.dev", senha = "errada" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Endpoint_protegido_sem_token_retorna_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/empresas");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cliente_ve_apenas_empresas_vinculadas()
    {
        var admin = await ClienteAutenticadoAsync("admin@contix.dev");
        var criada = await admin.PostAsJsonAsync("/api/v1/empresas", new
        {
            razaoSocial = "Não Vinculada Ltda", documento = "52998224725",
            regimeTributario = "MEI", situacao = "Ativa"
        });
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var naoVinculadaId = (await JsonAsync(criada))["id"]!.GetValue<string>();

        var cliente = await ClienteAutenticadoAsync("cliente@contix.dev");
        var lista = await JsonAsync(await cliente.GetAsync("/api/v1/empresas"));
        Assert.DoesNotContain(lista["items"]!.AsArray(), e => e!["id"]!.GetValue<string>() == naoVinculadaId);

        var direto = await cliente.GetAsync($"/api/v1/empresas/{naoVinculadaId}");
        Assert.Equal(HttpStatusCode.NotFound, direto.StatusCode);
        var pastas = await cliente.GetAsync($"/api/v1/empresas/{naoVinculadaId}/pastas");
        Assert.Equal(HttpStatusCode.NotFound, pastas.StatusCode);
    }

    [Fact]
    public async Task Escritorio_nao_acessa_empresa_de_outro_escritorio()
    {
        var anon = factory.CreateClient();
        var registro = await anon.PostAsJsonAsync("/api/v1/auth/registrar", new
        {
            razaoSocial = "Outro Escritório", cnpj = "11444777000161",
            nomeAdministrador = "Bia", emailAdministrador = $"bia-{Guid.NewGuid():N}@outro.dev", senha = "Senha@1234"
        });
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        var token = (await JsonAsync(registro))["accessToken"]!.GetValue<string>();

        var demo = await ClienteAutenticadoAsync("admin@contix.dev");
        var empresasDemo = await JsonAsync(await demo.GetAsync("/api/v1/empresas"));
        var empresaDemoId = empresasDemo["items"]![0]!["id"]!.GetValue<string>();

        var outro = factory.CreateClient();
        outro.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.NotFound, (await outro.GetAsync($"/api/v1/empresas/{empresaDemoId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outro.GetAsync($"/api/v1/empresas/{empresaDemoId}/documentos")).StatusCode);
        var lista = await JsonAsync(await outro.GetAsync("/api/v1/empresas"));
        Assert.Equal(0, lista["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task Colaborador_nao_cria_administrador()
    {
        var colaborador = await ClienteAutenticadoAsync("colaborador@contix.dev");
        var response = await colaborador.PostAsJsonAsync("/api/v1/usuarios", new
        {
            nome = "Novo Admin", email = $"adm-{Guid.NewGuid():N}@contix.dev", perfil = "Administrador", senha = "Senha@1234"
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_e_download_marcam_documento_como_lido()
    {
        var admin = await ClienteAutenticadoAsync("admin@contix.dev");
        var empresaId = (await JsonAsync(await admin.GetAsync("/api/v1/empresas?busca=DEMO")))["items"]![0]!["id"]!.GetValue<string>();
        var pastas = (await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{empresaId}/pastas"))).AsArray();
        var fiscalId = pastas.First(p => p!["nome"]!.GetValue<string>() == "Fiscal")!["id"]!.GetValue<string>();

        using var form = new MultipartFormDataContent
        {
            { new StringContent(fiscalId), "pastaId" },
            { new StringContent("2026-12-10"), "dataVencimento" },
            { new ByteArrayContent("conteúdo"u8.ToArray()) { Headers = { ContentType = new("text/plain") } }, "arquivos", "guia.txt" }
        };
        var upload = await admin.PostAsync($"/api/v1/empresas/{empresaId}/documentos", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var enviado = (await JsonAsync(upload))[0]!;
        var documentoId = enviado["id"]!.GetValue<string>();
        // Quem enviou não vê o próprio documento como "não lido".
        Assert.True(enviado["lido"]!.GetValue<bool>());

        var cliente = await ClienteAutenticadoAsync("cliente@contix.dev");
        var naoLidos = await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/documentos?status=NaoLidos&pastaId={fiscalId}"));
        Assert.Contains(naoLidos["items"]!.AsArray(), d => d!["id"]!.GetValue<string>() == documentoId);

        var download = await cliente.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{documentoId}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("conteúdo", await download.Content.ReadAsStringAsync());

        var doc = await JsonAsync(await cliente.GetAsync($"/api/v1/empresas/{empresaId}/documentos/{documentoId}"));
        Assert.True(doc["lido"]!.GetValue<bool>());

        // Cliente não pode excluir documento enviado pelo escritório.
        var exclusao = await cliente.DeleteAsync($"/api/v1/empresas/{empresaId}/documentos/{documentoId}");
        Assert.Equal(HttpStatusCode.Forbidden, exclusao.StatusCode);
    }

    [Fact]
    public async Task Refresh_token_rotaciona_e_detecta_reuso()
    {
        var http = factory.CreateClient();
        var login = await JsonAsync(await http.PostAsJsonAsync("/api/v1/auth/login", new { email = "colaborador@contix.dev", senha = Senha }));
        var primeiro = login["refreshToken"]!.GetValue<string>();

        var refresh = await http.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = primeiro });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var segundo = (await JsonAsync(refresh))["refreshToken"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = primeiro })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = segundo })).StatusCode);
    }

    [Fact]
    public async Task Health_check_retorna_ok()
    {
        var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);
    }
}
