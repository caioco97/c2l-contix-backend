using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contix.Infrastructure.Push;

/// <summary>Uma mensagem para um aparelho.</summary>
public sealed record MensagemPush(string Token, string Titulo, string Mensagem, IReadOnlyDictionary<string, string> Dados);

/// <summary>Resultado por aparelho. <see cref="TokenInvalido"/> indica que o aparelho deve ser removido.</summary>
public sealed record ResultadoPush(string Token, bool Sucesso, bool TokenInvalido, string? Erro);

/// <summary>Entrega efetiva do push. Lança exceção quando o serviço inteiro falha (o lote é tentado de novo).</summary>
public interface IPushSender
{
    Task<IReadOnlyList<ResultadoPush>> EnviarAsync(IReadOnlyList<MensagemPush> mensagens, CancellationToken ct = default);
}

/// <summary>
/// Expo Push Service (https://docs.expo.dev/push-notifications/sending-notifications/): entrega para
/// APNs (iOS) e FCM (Android) com um único token por aparelho. Até 100 mensagens por requisição.
/// </summary>
public sealed class ExpoPushSender(HttpClient http, IOptions<PushOptions> options, ILogger<ExpoPushSender> logger) : IPushSender
{
    public const int MaxPorRequisicao = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<IReadOnlyList<ResultadoPush>> EnviarAsync(IReadOnlyList<MensagemPush> mensagens, CancellationToken ct = default)
    {
        var resultados = new List<ResultadoPush>(mensagens.Count);
        foreach (var lote in mensagens.Chunk(MaxPorRequisicao))
        {
            var corpo = lote.Select(m => new ExpoMensagem(m.Token, m.Titulo, m.Mensagem, m.Dados, "default", "default", "high")).ToList();
            using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.UrlExpo)
            {
                Content = JsonContent.Create(corpo, options: Json)
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(options.Value.AccessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.AccessToken);

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var detalhe = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"Expo Push respondeu {(int)response.StatusCode}: {Resumir(detalhe)}");
            }

            var tickets = (await response.Content.ReadFromJsonAsync<ExpoResposta>(Json, ct))?.Data ?? [];
            for (var i = 0; i < lote.Length; i++)
            {
                var ticket = i < tickets.Count ? tickets[i] : null;
                if (ticket?.Status == "ok")
                {
                    resultados.Add(new ResultadoPush(lote[i].Token, true, false, null));
                    continue;
                }
                var erro = ticket?.Details?.Error ?? ticket?.Message ?? "Sem resposta do Expo para a mensagem.";
                var invalido = ticket?.Details?.Error == "DeviceNotRegistered";
                if (!invalido) logger.LogWarning("Push recusado pelo Expo: {Erro}", erro);
                resultados.Add(new ResultadoPush(lote[i].Token, false, invalido, erro));
            }
        }
        return resultados;
    }

    private static string Resumir(string texto) => texto.Length > 500 ? texto[..500] : texto;

    private sealed record ExpoMensagem(
        string To, string Title, string Body, IReadOnlyDictionary<string, string> Data,
        string Sound, string ChannelId, string Priority);

    private sealed record ExpoResposta(List<ExpoTicket>? Data);

    private sealed record ExpoTicket(string Status, string? Id, string? Message, ExpoDetalhes? Details);

    private sealed record ExpoDetalhes(string? Error);
}

/// <summary>Não envia: apenas registra. Útil em desenvolvimento, testes e ambientes sem push.</summary>
public sealed class LogPushSender(ILogger<LogPushSender> logger) : IPushSender
{
    public Task<IReadOnlyList<ResultadoPush>> EnviarAsync(IReadOnlyList<MensagemPush> mensagens, CancellationToken ct = default)
    {
        foreach (var m in mensagens)
            logger.LogInformation("Push (não enviado, provider Log) para {Token}: {Titulo} - {Mensagem}", m.Token, m.Titulo, m.Mensagem);
        return Task.FromResult<IReadOnlyList<ResultadoPush>>(mensagens.Select(m => new ResultadoPush(m.Token, true, false, null)).ToList());
    }
}
