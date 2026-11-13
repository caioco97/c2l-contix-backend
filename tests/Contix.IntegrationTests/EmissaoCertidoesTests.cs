using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Contix.Application.Features.Certidoes.Emissao;
using Contix.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contix.IntegrationTests;

/// <summary>Emissão automática de CND com o emissor simulado: configuração, fila, renovação e falhas.</summary>
[Collection(nameof(ApiCollection))]
public sealed class EmissaoCertidoesTests(ContixApiFactory factory)
{
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

    private static string NovoCnpj()
    {
        var d = Enumerable.Range(0, 12).Select(_ => Random.Shared.Next(10)).ToList();
        int Dv(IReadOnlyList<int> n, int[] pesos) { var r = n.Select((x, i) => x * pesos[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        d.Add(Dv(d, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        d.Add(Dv(d, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        return string.Concat(d);
    }

    private static async Task<string> NovaEmpresaAsync(HttpClient admin, string nome) =>
        (await JsonAsync(await admin.PostAsJsonAsync("/api/v1/empresas", new
        {
            razaoSocial = nome, documento = NovoCnpj(), regimeTributario = "SimplesNacional", situacao = "Ativa"
        })))["id"]!.GetValue<string>();

    private async Task<int> ProcessarAsync(Func<IProcessadorEmissaoCertidao, Task<int>> acao)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await acao(scope.ServiceProvider.GetRequiredService<IProcessadorEmissaoCertidao>());
    }

    [Fact]
    public async Task Emitir_agora_gera_certidoes_com_pdf_e_historico()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var empresaId = await NovaEmpresaAsync(admin, "Emissão Automática Ltda");
        var url = $"/api/v1/empresas/{empresaId}/certidoes";

        var config = await JsonAsync(await admin.GetAsync($"{url}/emissao-automatica"));
        Assert.True(config["integracaoHabilitada"]!.GetValue<bool>());
        Assert.Equal("Simulado", config["provedor"]!.GetValue<string>());
        Assert.False(config["ativa"]!.GetValue<bool>());
        Assert.Equal(3, config["tiposSuportados"]!.AsArray().Count);

        // Cliente não configura nem emite.
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"{url}/emissao-automatica")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsJsonAsync($"{url}/emissoes", new { })).StatusCode);

        // Tipo sem integração é recusado.
        var estadual = await admin.PutAsJsonAsync($"{url}/emissao-automatica",
            new { ativa = true, tipos = new[] { "Estadual" }, diasAntecedencia = 10 });
        Assert.Equal(HttpStatusCode.BadRequest, estadual.StatusCode);

        var salvo = await JsonAsync(await admin.PutAsJsonAsync($"{url}/emissao-automatica",
            new { ativa = true, tipos = new[] { "Fgts", "FederalPgfn" }, diasAntecedencia = 15 }));
        Assert.True(salvo["ativa"]!.GetValue<bool>());
        Assert.Equal(15, salvo["diasAntecedencia"]!.GetValue<int>());
        Assert.Equal(["FederalPgfn", "Fgts"], salvo["tipos"]!.AsArray().Select(t => t!.GetValue<string>()));

        // "Emitir agora" sem tipos usa os configurados; pedir de novo não duplica a fila.
        var pedido = await admin.PostAsJsonAsync($"{url}/emissoes", new { });
        Assert.Equal(HttpStatusCode.Accepted, pedido.StatusCode);
        Assert.Equal(2, (await JsonAsync(pedido)).AsArray().Count);
        await JsonAsync(await admin.PostAsJsonAsync($"{url}/emissoes", new { tipos = new[] { "Fgts" } }));
        Assert.Equal(2, (await JsonAsync(await admin.GetAsync($"{url}/emissoes"))).AsArray().Count);

        Assert.True(await ProcessarAsync(p => p.ProcessarFilaAsync()) >= 2);

        var emissoes = (await JsonAsync(await admin.GetAsync($"{url}/emissoes"))).AsArray();
        Assert.All(emissoes, e =>
        {
            Assert.Equal("Emitida", e!["status"]!.GetValue<string>());
            Assert.Equal("Manual", e["origem"]!.GetValue<string>());
            Assert.NotNull(e["certidaoId"]);
        });

        // O cliente não tem vínculo com a empresa nova.
        Assert.False((await cliente.GetAsync($"{url}?somenteAtuais=true")).IsSuccessStatusCode);
        var certidoes = (await JsonAsync(await admin.GetAsync($"{url}?somenteAtuais=true"))).AsArray();
        Assert.Equal(2, certidoes.Count);
        var federal = certidoes.Single(c => c!["tipo"]!.GetValue<string>() == "FederalPgfn")!;
        Assert.True(federal["emitidaAutomaticamente"]!.GetValue<bool>());
        Assert.Equal("Emissão automática", federal["cadastradoPorNome"]!.GetValue<string>());
        Assert.Equal("Negativa", federal["situacao"]!.GetValue<string>());
        Assert.True(federal["possuiArquivo"]!.GetValue<bool>());

        var pdf = await admin.GetAsync($"{url}/{federal["id"]!.GetValue<string>()}/download");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync())[..4]);

        // Certidões recém-emitidas e dentro da validade: o agendamento não cria nada para esta empresa.
        await ProcessarAsync(p => p.AgendarAsync());
        Assert.Equal(2, (await JsonAsync(await admin.GetAsync($"{url}/emissoes"))).AsArray().Count);
    }

    [Fact]
    public async Task Agendamento_renova_e_falha_definitiva_notifica_o_responsavel()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var colaboradorId = (await JsonAsync(await colaborador.GetAsync("/api/v1/auth/me")))["id"]!.GetValue<string>();
        var empresaId = await NovaEmpresaAsync(admin, "Comércio PENDENTE Ltda");
        var url = $"/api/v1/empresas/{empresaId}/certidoes";

        await JsonAsync(await admin.PutAsJsonAsync($"{url}/emissao-automatica",
            new { ativa = true, tipos = new[] { "Trabalhista" }, diasAntecedencia = 10, responsavelId = colaboradorId }));

        // Sem certidão: o agendamento põe na fila; a segunda rodada não duplica.
        await ProcessarAsync(p => p.AgendarAsync());
        await ProcessarAsync(p => p.AgendarAsync());
        var fila = (await JsonAsync(await admin.GetAsync($"{url}/emissoes"))).AsArray();
        Assert.Single(fila);
        Assert.Equal("Agendada", fila[0]!["origem"]!.GetValue<string>());

        // O emissor simulado recusa empresas "PENDENTE": falha definitiva, registrada e avisada ao responsável.
        await ProcessarAsync(p => p.ProcessarFilaAsync());
        var falha = (await JsonAsync(await admin.GetAsync($"{url}/emissoes"))).AsArray().Single()!;
        Assert.Equal("Falha", falha["status"]!.GetValue<string>());
        Assert.Contains("recusou", falha["mensagem"]!.GetValue<string>());

        var notificacoes = (await JsonAsync(await colaborador.GetAsync("/api/v1/notificacoes?pageSize=50")))["items"]!.AsArray();
        Assert.Contains(notificacoes, n => n!["tipo"]!.GetValue<string>() == "EmissaoCertidaoFalha"
                                           && n["titulo"]!.GetValue<string>().Contains("PENDENTE"));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContixDbContext>();
        Assert.False(await db.Certidoes.IgnoreQueryFilters().AnyAsync(c => c.EmpresaId == Guid.Parse(empresaId)));
    }

    [Fact]
    public async Task Desativada_nao_agenda()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var empresaId = await NovaEmpresaAsync(admin, "Sem Emissão Ltda");
        var url = $"/api/v1/empresas/{empresaId}/certidoes";
        await JsonAsync(await admin.PutAsJsonAsync($"{url}/emissao-automatica",
            new { ativa = false, tipos = new[] { "FederalPgfn" }, diasAntecedencia = 10 }));

        await ProcessarAsync(p => p.AgendarAsync());
        Assert.Empty((await JsonAsync(await admin.GetAsync($"{url}/emissoes"))).AsArray());
    }
}
