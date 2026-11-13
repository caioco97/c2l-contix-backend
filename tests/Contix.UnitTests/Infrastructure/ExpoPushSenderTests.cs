using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Contix.Application.Features.Dispositivos;
using Contix.Infrastructure.Push;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Contix.UnitTests.Infrastructure;

public class ExpoPushSenderTests
{
    private sealed class HandlerFalso(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Chamadas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            Chamadas.Add((request, body));
            return responder(request, body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static MensagemPush Mensagem(string token) =>
        new(token, "Novo documento", "Guia DAS disponível", new Dictionary<string, string> { ["link"] = "/documentos/1" });

    private static ExpoPushSender Sender(HandlerFalso handler, string? accessToken = null) =>
        new(new HttpClient(handler), Options.Create(new PushOptions { AccessToken = accessToken }), NullLogger<ExpoPushSender>.Instance);

    [Fact]
    public async Task Envia_no_formato_do_Expo_e_interpreta_os_tickets()
    {
        var handler = new HandlerFalso((_, _) => Json("""
            {"data":[
              {"status":"ok","id":"1"},
              {"status":"error","message":"not registered","details":{"error":"DeviceNotRegistered"}},
              {"status":"error","message":"rate","details":{"error":"MessageRateExceeded"}}
            ]}
            """));

        var r = await Sender(handler, "segredo").EnviarAsync([Mensagem("A"), Mensagem("B"), Mensagem("C")]);

        Assert.True(r[0].Sucesso);
        Assert.True(r[1] is { Sucesso: false, TokenInvalido: true });
        Assert.True(r[2] is { Sucesso: false, TokenInvalido: false, Erro: "MessageRateExceeded" });

        var (request, body) = Assert.Single(handler.Chamadas);
        Assert.Equal("Bearer segredo", request.Headers.Authorization?.ToString());
        var primeira = JsonNode.Parse(body)!.AsArray()[0]!;
        Assert.Equal("A", primeira["to"]!.GetValue<string>());
        Assert.Equal("Novo documento", primeira["title"]!.GetValue<string>());
        Assert.Equal("Guia DAS disponível", primeira["body"]!.GetValue<string>());
        Assert.Equal("/documentos/1", primeira["data"]!["link"]!.GetValue<string>());
        Assert.Equal("default", primeira["channelId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Divide_em_lotes_de_100()
    {
        var handler = new HandlerFalso((_, body) =>
        {
            var n = JsonNode.Parse(body)!.AsArray().Count;
            return Json($$"""{"data":[{{string.Join(",", Enumerable.Repeat("""{"status":"ok"}""", n))}}]}""");
        });

        var r = await Sender(handler).EnviarAsync(Enumerable.Range(0, 150).Select(i => Mensagem($"T{i}")).ToList());

        Assert.Equal(150, r.Count);
        Assert.All(r, x => Assert.True(x.Sucesso));
        Assert.Equal([100, 50], handler.Chamadas.Select(c => JsonNode.Parse(c.Body)!.AsArray().Count));
        Assert.Null(handler.Chamadas[0].Request.Headers.Authorization);
    }

    [Fact]
    public async Task Falha_do_servico_lanca_para_o_lote_ser_tentado_de_novo()
    {
        var handler = new HandlerFalso((_, _) => Json("""{"errors":[{"message":"down"}]}""", HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<HttpRequestException>(() => Sender(handler).EnviarAsync([Mensagem("A")]));
    }

    [Theory]
    [InlineData("ExponentPushToken[xxxxxxxxxxxxxxxxxxxxxx]", true)]
    [InlineData("ExpoPushToken[xxxxxxxxxxxxxxxxxxxxxx]", true)]
    [InlineData("ExponentPushToken[xxx", false)]
    [InlineData("fcm-token-qualquer", false)]
    [InlineData("", false)]
    public void Valida_o_formato_do_token(string token, bool valido) =>
        Assert.Equal(valido, RegistrarDispositivoRequestValidator.TokenExpoValido(token));
}
