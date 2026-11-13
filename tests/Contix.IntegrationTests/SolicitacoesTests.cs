using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Contix.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class SolicitacoesTests(ContixApiFactory factory)
{
    private async Task<HttpClient> LoginAsync(string email)
    {
        var http = factory.CreateClient();
        var body = await (await http.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = "Contix@123" }))
            .Content.ReadFromJsonAsync<JsonObject>();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!["accessToken"]!.GetValue<string>());
        return http;
    }

    private static async Task<JsonNode> JsonAsync(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!;

    private static async Task<string> EmpresaDemoAsync(HttpClient http) =>
        (await JsonAsync(await http.GetAsync("/api/v1/empresas")))["items"]![0]!["id"]!.GetValue<string>();

    [Fact]
    public async Task Fluxo_completo_cliente_e_escritorio()
    {
        var cliente = await LoginAsync("cliente@contix.dev");
        var empresaId = await EmpresaDemoAsync(cliente);

        // Afastamento exige anexo.
        var semAnexo = await cliente.PostAsync($"/api/v1/empresas/{empresaId}/solicitacoes", new MultipartFormDataContent
        {
            { new StringContent("Afastamento"), "tipo" },
            { new StringContent("""{"empregado":"João","motivo":"Doenca","dataInicio":"2026-11-10"}"""), "dados" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, semAnexo.StatusCode);
        Assert.NotNull((await JsonAsync(semAnexo))["errors"]!["anexos"]);

        var abertura = await cliente.PostAsync($"/api/v1/empresas/{empresaId}/solicitacoes", new MultipartFormDataContent
        {
            { new StringContent("Afastamento"), "tipo" },
            { new StringContent("""{"empregado":"João","motivo":"Doenca","dataInicio":"2026-11-10"}"""), "dados" },
            { new ByteArrayContent("atestado"u8.ToArray()) { Headers = { ContentType = new("application/pdf") } }, "anexos", "atestado.pdf" }
        });
        Assert.Equal(HttpStatusCode.Created, abertura.StatusCode);
        var criada = await JsonAsync(abertura);
        var id = criada["id"]!.GetValue<string>();
        Assert.Equal("Aberta", criada["status"]!.GetValue<string>());
        Assert.Equal("Pessoal", criada["departamento"]!.GetValue<string>());
        Assert.Equal("Afastamento de Empregado - João", criada["assunto"]!.GetValue<string>());
        var anexoId = criada["historico"]![0]!["anexos"]![0]!["id"]!.GetValue<string>();

        // Cliente não acessa a fila do escritório nem atribui.
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/v1/solicitacoes")).StatusCode);

        var colaborador = await LoginAsync("colaborador@contix.dev");
        // O escritório atende, mas não abre solicitações em nome do cliente.
        var aberturaEscritorio = await colaborador.PostAsync($"/api/v1/empresas/{empresaId}/solicitacoes", new MultipartFormDataContent
        {
            { new StringContent("Geral"), "tipo" },
            { new StringContent("Teste"), "assunto" },
        });
        Assert.Equal(HttpStatusCode.Forbidden, aberturaEscritorio.StatusCode);

        var fila = await JsonAsync(await colaborador.GetAsync("/api/v1/solicitacoes?emAndamento=true&tipo=Afastamento"));
        Assert.Contains(fila["items"]!.AsArray(), s => s!["id"]!.GetValue<string>() == id);

        var pedido = await colaborador.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/solicitacoes/{id}/status",
            new { status = "AguardandoCliente", mensagem = "Envie o CID." });
        Assert.Equal(HttpStatusCode.OK, pedido.StatusCode);

        using var resposta = new MultipartFormDataContent { { new StringContent("CID J11"), "mensagem" } };
        var respondida = await JsonAsync(await cliente.PostAsync($"/api/v1/empresas/{empresaId}/solicitacoes/{id}/comentarios", resposta));
        Assert.Equal("EmAnalise", respondida["status"]!.GetValue<string>());

        var concluir = await cliente.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/solicitacoes/{id}/status", new { status = "Concluida" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, concluir.StatusCode);

        var concluida = await JsonAsync(await colaborador.PostAsJsonAsync(
            $"/api/v1/empresas/{empresaId}/solicitacoes/{id}/status", new { status = "Concluida" }));
        Assert.Equal("Concluida", concluida["status"]!.GetValue<string>());
        Assert.Equal(5, concluida["historico"]!.AsArray().Count);

        var download = await cliente.GetAsync($"/api/v1/empresas/{empresaId}/solicitacoes/{id}/anexos/{anexoId}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("atestado", await download.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Numeracao_e_sequencial_por_escritorio()
    {
        var cliente = await LoginAsync("cliente@contix.dev");
        var empresaId = await EmpresaDemoAsync(cliente);

        async Task<int> AbrirAsync()
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent("Geral"), "tipo" },
                { new StringContent("Pedido"), "assunto" },
                { new StringContent("Fiscal"), "departamento" },
                { new StringContent("Detalhes"), "descricao" }
            };
            var r = await cliente.PostAsync($"/api/v1/empresas/{empresaId}/solicitacoes", form);
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            return (await JsonAsync(r))["numero"]!.GetValue<int>();
        }

        var numeros = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => AbrirAsync()));
        Assert.Equal(5, numeros.Distinct().Count());
        Assert.Equal(numeros.Min() + 4, numeros.Max());
    }

    [Fact]
    public async Task Atribuicao_aceita_apenas_usuario_do_escritorio()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var empresaId = await EmpresaDemoAsync(admin);
        var solicitacao = (await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{empresaId}/solicitacoes")))["items"]![0]!;
        var usuarios = (await JsonAsync(await admin.GetAsync("/api/v1/usuarios?pageSize=100")))["items"]!.AsArray();
        var clienteId = usuarios.First(u => u!["perfil"]!.GetValue<string>() == "Cliente")!["id"]!.GetValue<string>();
        var colaboradorId = usuarios.First(u => u!["perfil"]!.GetValue<string>() == "Colaborador")!["id"]!.GetValue<string>();
        var url = $"/api/v1/empresas/{empresaId}/solicitacoes/{solicitacao["id"]}/atribuicao";

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url, new { responsavelId = clienteId })).StatusCode);

        var ok = await JsonAsync(await admin.PutAsJsonAsync(url, new { responsavelId = colaboradorId, prazo = "2020-01-01" }));
        Assert.Equal("Colaborador Demo", ok["responsavelNome"]!.GetValue<string>());
        Assert.True(ok["atrasada"]!.GetValue<bool>());
    }
}
