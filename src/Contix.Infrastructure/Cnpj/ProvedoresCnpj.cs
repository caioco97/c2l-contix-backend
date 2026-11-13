using System.Globalization;
using System.Net;
using System.Text.Json;
using Contix.Application.Common.Models;
using Contix.Application.Features.Empresas;
using Contix.Domain.Enums;
using Contix.Domain.ValueObjects;
using Microsoft.Extensions.Caching.Memory;

namespace Contix.Infrastructure.Cnpj;

public sealed class ConsultaCnpjOptions
{
    public const string Secao = "ConsultaCnpj";

    /// <summary>Provedores padrão quando <c>ConsultaCnpj:Provedores</c> não está configurado (lista em ordem de preferência).</summary>
    public static readonly string[] ProvedoresPadrao = ["BrasilApi", "ReceitaWs"];
    public string UrlBrasilApi { get; set; } = "https://brasilapi.com.br/api/cnpj/v1/";
    public string UrlReceitaWs { get; set; } = "https://receitaws.com.br/v1/cnpj/";
    public int TimeoutSegundos { get; set; } = 10;
    /// <summary>Por quanto tempo uma resposta fica em cache (as APIs públicas têm limite de uso).</summary>
    public int CacheHoras { get; set; } = 24;
}

/// <summary>Leitura defensiva de JSON (as APIs públicas mudam tipos: número vira string, campo some...).</summary>
internal static class Json
{
    public static string? Texto(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String when !string.IsNullOrWhiteSpace(v.GetString()) => v.GetString()!.Trim(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        } : null;

    public static bool? Bool(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) ? v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object when v.TryGetProperty("optante", out var o) && o.ValueKind is JsonValueKind.True or JsonValueKind.False => o.GetBoolean(),
            _ => null
        } : null;

    public static DateOnly? Data(string? v) =>
        DateOnly.TryParseExact(v, ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static string? TituloCase(string? v) =>
        string.IsNullOrWhiteSpace(v) ? null : CultureInfo.GetCultureInfo("pt-BR").TextInfo.ToTitleCase(v.Trim().ToLowerInvariant());

    public static RegimeTributario? Regime(bool? mei, bool? simples) =>
        mei == true ? RegimeTributario.MEI : simples == true ? RegimeTributario.SimplesNacional : null;
}

/// <summary>BrasilAPI (dados da Receita Federal, sem chave de acesso).</summary>
public sealed class ConsultaCnpjBrasilApi(HttpClient http, Microsoft.Extensions.Options.IOptions<ConsultaCnpjOptions> options, IMemoryCache cache)
    : IProvedorConsultaCnpj
{
    public string Nome => "BrasilAPI";

    public Task<ResultadoConsultaCnpj> ConsultarAsync(string cnpj, CancellationToken ct = default) =>
        cache.GetOrCreateAsync<ResultadoConsultaCnpj>($"cnpj:brasilapi:{cnpj}", async entry =>
        {
            var r = await BuscarAsync(cnpj, ct);
            entry.AbsoluteExpirationRelativeToNow = r is ResultadoConsultaCnpj.Indisponivel ? TimeSpan.FromSeconds(1) : TimeSpan.FromHours(options.Value.CacheHoras);
            return r;
        })!;

    private async Task<ResultadoConsultaCnpj> BuscarAsync(string cnpj, CancellationToken ct)
    {
        using var resp = await http.GetAsync(options.Value.UrlBrasilApi.TrimEnd('/') + "/" + cnpj, ct);
        if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest) return new ResultadoConsultaCnpj.NaoEncontrado();
        if (!resp.IsSuccessStatusCode) return new ResultadoConsultaCnpj.Indisponivel($"HTTP {(int)resp.StatusCode}");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
        return new ResultadoConsultaCnpj.Encontrado(Ler(doc.RootElement, cnpj, Nome));
    }

    public static DadosCnpjDto Ler(JsonElement e, string cnpj, string fonte)
    {
        var situacao = Json.Texto(e, "descricao_situacao_cadastral");
        var logradouro = string.Join(' ', new[] { Json.Texto(e, "descricao_tipo_de_logradouro"), Json.Texto(e, "logradouro") }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        var telefone = DocumentoFiscal.SomenteDigitos(Json.Texto(e, "ddd_telefone_1"));
        return new DadosCnpjDto(
            cnpj,
            Json.Texto(e, "razao_social") ?? "",
            Json.Texto(e, "nome_fantasia"),
            Json.TituloCase(situacao),
            string.Equals(situacao, "ATIVA", StringComparison.OrdinalIgnoreCase),
            Json.Texto(e, "email")?.ToLowerInvariant(),
            telefone.Length >= 10 ? telefone : null,
            new EnderecoDto(Json.Texto(e, "cep"), Json.TituloCase(logradouro), Json.Texto(e, "numero"), Json.TituloCase(Json.Texto(e, "complemento")),
                Json.TituloCase(Json.Texto(e, "bairro")), Json.TituloCase(Json.Texto(e, "municipio")), Json.Texto(e, "uf")).Normalizar(),
            Json.Regime(Json.Bool(e, "opcao_pelo_mei"), Json.Bool(e, "opcao_pelo_simples")),
            Json.Texto(e, "cnae_fiscal_descricao"),
            Json.Data(Json.Texto(e, "data_inicio_atividade")),
            fonte);
    }
}

/// <summary>ReceitaWS (plano gratuito: 3 consultas por minuto). Usada quando a BrasilAPI não responde.</summary>
public sealed class ConsultaCnpjReceitaWs(HttpClient http, Microsoft.Extensions.Options.IOptions<ConsultaCnpjOptions> options, IMemoryCache cache)
    : IProvedorConsultaCnpj
{
    public string Nome => "ReceitaWS";

    public Task<ResultadoConsultaCnpj> ConsultarAsync(string cnpj, CancellationToken ct = default) =>
        cache.GetOrCreateAsync<ResultadoConsultaCnpj>($"cnpj:receitaws:{cnpj}", async entry =>
        {
            var r = await BuscarAsync(cnpj, ct);
            entry.AbsoluteExpirationRelativeToNow = r is ResultadoConsultaCnpj.Indisponivel ? TimeSpan.FromSeconds(1) : TimeSpan.FromHours(options.Value.CacheHoras);
            return r;
        })!;

    private async Task<ResultadoConsultaCnpj> BuscarAsync(string cnpj, CancellationToken ct)
    {
        using var resp = await http.GetAsync(options.Value.UrlReceitaWs.TrimEnd('/') + "/" + cnpj, ct);
        if (!resp.IsSuccessStatusCode) return new ResultadoConsultaCnpj.Indisponivel($"HTTP {(int)resp.StatusCode}");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
        var e = doc.RootElement;
        if (Json.Texto(e, "status") == "ERROR")
        {
            var msg = Json.Texto(e, "message") ?? "";
            return msg.Contains("inválido", StringComparison.OrdinalIgnoreCase) || msg.Contains("não encontrado", StringComparison.OrdinalIgnoreCase)
                ? new ResultadoConsultaCnpj.NaoEncontrado()
                : new ResultadoConsultaCnpj.Indisponivel(msg);
        }
        return new ResultadoConsultaCnpj.Encontrado(Ler(e, cnpj, Nome));
    }

    public static DadosCnpjDto Ler(JsonElement e, string cnpj, string fonte)
    {
        var situacao = Json.Texto(e, "situacao");
        var telefone = DocumentoFiscal.SomenteDigitos(Json.Texto(e, "telefone")?.Split('/')[0]);
        string? atividade = null;
        if (e.TryGetProperty("atividade_principal", out var ap) && ap.ValueKind == JsonValueKind.Array && ap.GetArrayLength() > 0)
            atividade = Json.Texto(ap[0], "text");
        return new DadosCnpjDto(
            cnpj,
            Json.Texto(e, "nome") ?? "",
            Json.Texto(e, "fantasia"),
            Json.TituloCase(situacao),
            string.Equals(situacao, "ATIVA", StringComparison.OrdinalIgnoreCase),
            Json.Texto(e, "email")?.ToLowerInvariant(),
            telefone.Length >= 10 ? telefone : null,
            new EnderecoDto(Json.Texto(e, "cep"), Json.TituloCase(Json.Texto(e, "logradouro")), Json.Texto(e, "numero"),
                Json.TituloCase(Json.Texto(e, "complemento")), Json.TituloCase(Json.Texto(e, "bairro")),
                Json.TituloCase(Json.Texto(e, "municipio")), Json.Texto(e, "uf")).Normalizar(),
            Json.Regime(Json.Bool(e, "simei"), Json.Bool(e, "simples")),
            atividade,
            Json.Data(Json.Texto(e, "abertura")),
            fonte);
    }
}

/// <summary>Consulta fictícia para desenvolvimento e testes: qualquer CNPJ válido "existe"; terminados em 0001-91 não.</summary>
public sealed class ConsultaCnpjSimulada : IProvedorConsultaCnpj
{
    public string Nome => "Simulado";

    public Task<ResultadoConsultaCnpj> ConsultarAsync(string cnpj, CancellationToken ct = default)
    {
        if (cnpj.EndsWith("000191", StringComparison.Ordinal))
            return Task.FromResult<ResultadoConsultaCnpj>(new ResultadoConsultaCnpj.NaoEncontrado());
        return Task.FromResult<ResultadoConsultaCnpj>(new ResultadoConsultaCnpj.Encontrado(new DadosCnpjDto(
            cnpj, $"Empresa Simulada {cnpj[..4]} Ltda", "Simulada", "Ativa", true, "contato@empresa-simulada.com.br", "11999990000",
            new EnderecoDto("01310100", "Avenida Paulista", "1000", "Sala 1", "Bela Vista", "São Paulo", "SP"),
            RegimeTributario.SimplesNacional, "Atividades de contabilidade", new DateOnly(2015, 3, 2), Nome)));
    }
}
