using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Contix.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class TarefasTests(ContixApiFactory factory)
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

    private static async Task<string> IdDoUsuarioAsync(HttpClient http) =>
        (await JsonAsync(await http.GetAsync("/api/v1/auth/me")))["id"]!.GetValue<string>();

    private static MultipartFormDataContent Formulario(string titulo, string responsavelId, string prazoInterno, string? prazoLegal = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(titulo), "titulo" },
            { new StringContent("Conferir as notas de entrada."), "descricao" },
            { new StringContent("Fiscal"), "departamento" },
            { new StringContent(responsavelId), "responsavelId" },
            { new StringContent(prazoInterno), "prazoInterno" }
        };
        if (prazoLegal is not null) form.Add(new StringContent(prazoLegal), "prazoLegal");
        return form;
    }

    [Fact]
    public async Task Fluxo_completo_da_tarefa()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var colaboradorId = await IdDoUsuarioAsync(colaborador);
        var empresaId = (await JsonAsync(await admin.GetAsync("/api/v1/empresas")))["items"]![0]!["id"]!.GetValue<string>();
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        // Prazo interno depois do legal é recusado.
        var invalida = await admin.PostAsync($"/api/v1/empresas/{empresaId}/tarefas",
            Formulario("Apuração ICMS", colaboradorId, hoje.AddDays(10).ToString("O"), hoje.AddDays(5).ToString("O")));
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        Assert.NotNull((await JsonAsync(invalida))["errors"]!["prazoInterno"]);

        var form = Formulario("Apuração ICMS", colaboradorId, hoje.AddDays(-1).ToString("O"), hoje.AddDays(5).ToString("O"));
        form.Add(new ByteArrayContent("planilha"u8.ToArray()) { Headers = { ContentType = new("application/pdf") } }, "anexos", "apuracao.pdf");
        var criacao = await admin.PostAsync($"/api/v1/empresas/{empresaId}/tarefas", form);
        Assert.Equal(HttpStatusCode.Created, criacao.StatusCode);
        var tarefa = await JsonAsync(criacao);
        var id = tarefa["id"]!.GetValue<string>();
        Assert.Equal("Pendente", tarefa["status"]!.GetValue<string>());
        Assert.Equal("Colaborador Demo", tarefa["responsavelNome"]!.GetValue<string>());
        Assert.True(tarefa["atrasada"]!.GetValue<bool>());
        Assert.False(tarefa["prazoLegalVencido"]!.GetValue<bool>());
        var anexoId = tarefa["historico"]![0]!["anexos"]![0]!["id"]!.GetValue<string>();

        // O responsável é avisado.
        var notificacoes = await JsonAsync(await colaborador.GetAsync("/api/v1/notificacoes"));
        Assert.Contains(notificacoes["items"]!.AsArray(), n => n!["link"]?.GetValue<string>() == $"/tarefas/{empresaId}/{id}");

        // Aparece na lista geral filtrada por responsável e na aba da empresa.
        var minhas = await JsonAsync(await colaborador.GetAsync($"/api/v1/tarefas?responsavelId={colaboradorId}&emAberto=true&atrasadas=true"));
        Assert.Contains(minhas["items"]!.AsArray(), t => t!["id"]!.GetValue<string>() == id && t["anexos"]!.GetValue<int>() == 1);
        var daEmpresa = await JsonAsync(await colaborador.GetAsync($"/api/v1/empresas/{empresaId}/tarefas?busca=icms"));
        Assert.Contains(daEmpresa["items"]!.AsArray(), t => t!["id"]!.GetValue<string>() == id);

        var editada = await JsonAsync(await colaborador.PutAsJsonAsync($"/api/v1/empresas/{empresaId}/tarefas/{id}", new
        {
            titulo = "Apuração ICMS 10/2026", descricao = "Conferir as notas de entrada.", departamento = "Fiscal",
            responsavelId = colaboradorId, prazoInterno = hoje.AddDays(2).ToString("O"), prazoLegal = hoje.AddDays(5).ToString("O")
        }));
        Assert.False(editada["atrasada"]!.GetValue<bool>());
        Assert.Equal("Edicao", editada["historico"]!.AsArray().Last()!["tipo"]!.GetValue<string>());

        using var comentario = new MultipartFormDataContent { { new StringContent("Aguardando XML do cliente."), "mensagem" } };
        var comentada = await JsonAsync(await colaborador.PostAsync($"/api/v1/empresas/{empresaId}/tarefas/{id}/comentarios", comentario));
        Assert.Equal("Comentario", comentada["historico"]!.AsArray().Last()!["tipo"]!.GetValue<string>());

        var concluida = await JsonAsync(await colaborador.PostAsJsonAsync(
            $"/api/v1/empresas/{empresaId}/tarefas/{id}/status", new { status = "Concluida" }));
        Assert.Equal("Concluida", concluida["status"]!.GetValue<string>());
        Assert.Equal(["EmAndamento"], concluida["transicoesPermitidas"]!.AsArray().Select(s => s!.GetValue<string>()));

        var invalidaStatus = await colaborador.PostAsJsonAsync($"/api/v1/empresas/{empresaId}/tarefas/{id}/status", new { status = "Cancelada" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidaStatus.StatusCode);

        var download = await colaborador.GetAsync($"/api/v1/empresas/{empresaId}/tarefas/{id}/anexos/{anexoId}/download");
        Assert.Equal("planilha", await download.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cliente_nao_acessa_tarefas_e_responsavel_deve_ser_do_escritorio()
    {
        var cliente = await LoginAsync("cliente@contix.dev");
        var clienteId = await IdDoUsuarioAsync(cliente);
        var empresaId = (await JsonAsync(await cliente.GetAsync("/api/v1/empresas")))["items"]![0]!["id"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/v1/tarefas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"/api/v1/empresas/{empresaId}/tarefas")).StatusCode);

        var admin = await LoginAsync("admin@contix.dev");
        var resposta = await admin.PostAsync($"/api/v1/empresas/{empresaId}/tarefas",
            Formulario("Folha", clienteId, DateOnly.FromDateTime(DateTime.UtcNow).ToString("O")));
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.NotNull((await JsonAsync(resposta))["errors"]!["responsavelId"]);
    }
}
