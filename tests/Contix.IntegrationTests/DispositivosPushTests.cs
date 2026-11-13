using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Contix.Application.Common.Abstractions;
using Contix.Infrastructure.Persistence;
using Contix.Infrastructure.Push;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Contix.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class DispositivosPushTests(ContixApiFactory factory)
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

    private async Task<T> NoBancoAsync<T>(Func<ContixDbContext, Task<T>> consulta)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await consulta(scope.ServiceProvider.GetRequiredService<ContixDbContext>());
    }

    private static string NovoToken(string prefixo = "ok") => $"ExponentPushToken[{prefixo}-{Guid.NewGuid():N}]";

    private static Task<HttpResponseMessage> RegistrarAsync(HttpClient http, string token, string plataforma = "Android") =>
        http.PostAsJsonAsync("/api/v1/dispositivos", new { token, plataforma, nome = "Aparelho de teste" });

    /// <summary>Envio simulado: tokens com "invalido" respondem DeviceNotRegistered, como o Expo faz após desinstalar o app.</summary>
    private sealed class SenderFalso : IPushSender
    {
        public List<MensagemPush> Enviadas { get; } = [];

        public Task<IReadOnlyList<ResultadoPush>> EnviarAsync(IReadOnlyList<MensagemPush> mensagens, CancellationToken ct = default)
        {
            Enviadas.AddRange(mensagens);
            return Task.FromResult<IReadOnlyList<ResultadoPush>>(mensagens
                .Select(m => m.Token.Contains("invalido")
                    ? new ResultadoPush(m.Token, false, true, "DeviceNotRegistered")
                    : new ResultadoPush(m.Token, true, false, null))
                .ToList());
        }
    }

    private async Task ProcessarFilaAsync(IPushSender sender)
    {
        var processor = new PushOutboxProcessor(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), sender,
            factory.Services.GetRequiredService<IDateTimeProvider>(),
            Options.Create(new PushOptions { LoteFila = 200 }), NullLogger<PushOutboxProcessor>.Instance);
        for (var i = 0; i < 20 && await processor.ProcessarAsync() > 0; i++) { }
    }

    [Fact]
    public async Task Registro_valida_o_token_e_remocao_so_vale_para_o_dono()
    {
        var cliente = await LoginAsync("cliente@contix.dev");
        var colaborador = await LoginAsync("colaborador@contix.dev");

        Assert.Equal(HttpStatusCode.BadRequest, (await RegistrarAsync(cliente, "token-qualquer")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RegistrarAsync(factory.CreateClient(), NovoToken())).StatusCode);

        var token = NovoToken();
        Assert.Equal(HttpStatusCode.NoContent, (await RegistrarAsync(cliente, token, "Ios")).StatusCode);
        // Registrar de novo é idempotente.
        Assert.Equal(HttpStatusCode.NoContent, (await RegistrarAsync(cliente, token, "Ios")).StatusCode);
        Assert.Equal(1, await NoBancoAsync(db => db.DispositivosPush.CountAsync(d => d.Token == token)));

        // Outro usuário não remove o aparelho de quem não é ele.
        Assert.Equal(HttpStatusCode.NoContent, (await colaborador.DeleteAsync($"/api/v1/dispositivos?token={Uri.EscapeDataString(token)}")).StatusCode);
        Assert.True(await NoBancoAsync(db => db.DispositivosPush.AnyAsync(d => d.Token == token)));

        Assert.Equal(HttpStatusCode.NoContent, (await cliente.DeleteAsync($"/api/v1/dispositivos?token={Uri.EscapeDataString(token)}")).StatusCode);
        Assert.False(await NoBancoAsync(db => db.DispositivosPush.AnyAsync(d => d.Token == token)));
    }

    [Fact]
    public async Task Mesmo_aparelho_com_outra_conta_passa_a_ser_do_novo_usuario()
    {
        var cliente = await LoginAsync("cliente@contix.dev");
        var colaborador = await LoginAsync("colaborador@contix.dev");
        var colaboradorId = (await JsonAsync(await colaborador.GetAsync("/api/v1/auth/me")))["id"]!.GetValue<Guid>();
        var token = NovoToken();

        await RegistrarAsync(cliente, token);
        await RegistrarAsync(colaborador, token);

        var dono = await NoBancoAsync(db => db.DispositivosPush.Where(d => d.Token == token).Select(d => d.UsuarioId).SingleAsync());
        Assert.Equal(colaboradorId, dono);
    }

    [Fact]
    public async Task Notificacao_gera_push_para_os_aparelhos_e_remove_os_invalidos()
    {
        var admin = await LoginAsync("admin@contix.dev");
        var cliente = await LoginAsync("cliente@contix.dev");
        var clienteId = (await JsonAsync(await cliente.GetAsync("/api/v1/auth/me")))["id"]!.GetValue<Guid>();
        var valido = NovoToken();
        var invalido = NovoToken("invalido");
        Assert.Equal(HttpStatusCode.NoContent, (await RegistrarAsync(cliente, valido)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await RegistrarAsync(cliente, invalido)).StatusCode);

        // O escritório publica um documento: o cliente recebe a notificação in-app e o push.
        var empresaId = (await JsonAsync(await admin.GetAsync("/api/v1/empresas?busca=DEMO")))["items"]![0]!["id"]!.GetValue<string>();
        var pastas = (await JsonAsync(await admin.GetAsync($"/api/v1/empresas/{empresaId}/pastas"))).AsArray();
        var pastaId = pastas.First(p => p!["nome"]!.GetValue<string>() == "Fiscal")!["id"]!.GetValue<string>();
        var marcador = $"guia-push-{Guid.NewGuid():N}";
        var nome = $"{marcador}.txt";
        using var form = new MultipartFormDataContent
        {
            { new StringContent(pastaId), "pastaId" },
            { new ByteArrayContent("conteúdo"u8.ToArray()) { Headers = { ContentType = new("text/plain") } }, "arquivos", nome }
        };
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsync($"/api/v1/empresas/{empresaId}/documentos", form)).StatusCode);

        var notificacao = await NoBancoAsync(db => db.Notificacoes.IgnoreQueryFilters()
            .Where(n => n.UsuarioId == clienteId && n.Mensagem.Contains(marcador)).SingleAsync());
        var push = await NoBancoAsync(db => db.PushesPendentes
            .Where(p => p.UsuarioId == clienteId && p.Mensagem.Contains(marcador)).SingleAsync());
        Assert.Equal(notificacao.Titulo, push.Titulo);
        Assert.Null(push.EnviadoEm);

        var sender = new SenderFalso();
        await ProcessarFilaAsync(sender);

        // Só os aparelhos deste teste (o cliente pode ter outros registrados por outros testes).
        var enviadas = sender.Enviadas.Where(m => m.Mensagem.Contains(marcador) && (m.Token == valido || m.Token == invalido)).ToList();
        Assert.Equal(2, enviadas.Count);
        Assert.Equal(new HashSet<string> { valido, invalido }, enviadas.Select(m => m.Token).ToHashSet());
        var dados = enviadas[0].Dados;
        Assert.Equal(notificacao.Id.ToString(), dados["notificacaoId"]);
        Assert.Equal("NovoDocumento", dados["tipo"]);
        Assert.Equal(empresaId, dados["empresaId"]);
        Assert.StartsWith("/documentos/", dados["link"]);

        Assert.NotNull(await NoBancoAsync(db => db.PushesPendentes.Where(p => p.Id == push.Id).Select(p => p.EnviadoEm).SingleAsync()));
        Assert.True(await NoBancoAsync(db => db.DispositivosPush.AnyAsync(d => d.Token == valido)));
        Assert.False(await NoBancoAsync(db => db.DispositivosPush.AnyAsync(d => d.Token == invalido)));
    }
}
