using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Contix.Application.Common.Abstractions;
using Contix.Application.Features.Certidoes;
using Contix.Application.Features.Certidoes.Emissao;
using Contix.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contix.Infrastructure.Certidoes;

public sealed class EmissaoCertidaoOptions
{
    public const string Secao = "Certidoes:Emissao";

    /// <summary>Desabilitado (padrão), Simulado (desenvolvimento) ou Infosimples.</summary>
    public string Provider { get; set; } = "Desabilitado";
    /// <summary>Liga o job de agendamento e da fila.</summary>
    public bool ProcessarFila { get; set; } = true;
    public int IntervaloMinutos { get; set; } = 5;
    public InfosimplesOptions Infosimples { get; set; } = new();
}

public sealed class InfosimplesOptions
{
    public string? Token { get; set; }
    public string UrlBase { get; set; } = "https://api.infosimples.com/api/v2/consultas/";
    /// <summary>Tempo que o integrador espera pela fonte (o parâmetro <c>timeout</c> da API).</summary>
    public int TimeoutSegundos { get; set; } = 300;
    /// <summary>Caminho da consulta por tipo de certidão (relativo a <see cref="UrlBase"/>).</summary>
    public Dictionary<TipoCertidao, string> Consultas { get; set; } = new()
    {
        [TipoCertidao.FederalPgfn] = "receita-federal/pgfn",
        [TipoCertidao.Fgts] = "caixa/regularidade",
        [TipoCertidao.Trabalhista] = "tribunal/tst/cndt",
    };
}

/// <summary>Sem integrador configurado: nada é emitido.</summary>
public sealed class EmissorCertidaoDesabilitado : IEmissorCertidao
{
    public string Nome => "Desabilitado";
    public bool Habilitado => false;
    public IReadOnlyCollection<TipoCertidao> TiposSuportados { get; } =
        [TipoCertidao.FederalPgfn, TipoCertidao.Fgts, TipoCertidao.Trabalhista];
    public Task<ResultadoEmissao> EmitirAsync(TipoCertidao tipo, DadosEmpresaEmissao empresa, CancellationToken ct = default) =>
        Task.FromResult(ResultadoEmissao.Falha("A emissão automática não está configurada.", false));
}

/// <summary>
/// Emissor de desenvolvimento/homologação: devolve uma certidão negativa fictícia, com PDF marcado como simulação.
/// Empresas com "PENDENTE" na razão social simulam uma recusa do órgão, para testar o fluxo de falha.
/// </summary>
public sealed class EmissorCertidaoSimulado(IDateTimeProvider clock) : IEmissorCertidao
{
    public string Nome => "Simulado";
    public bool Habilitado => true;
    public IReadOnlyCollection<TipoCertidao> TiposSuportados { get; } =
        [TipoCertidao.FederalPgfn, TipoCertidao.Fgts, TipoCertidao.Trabalhista];

    public Task<ResultadoEmissao> EmitirAsync(TipoCertidao tipo, DadosEmpresaEmissao empresa, CancellationToken ct = default)
    {
        if (empresa.RazaoSocial.Contains("PENDENTE", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(ResultadoEmissao.Falha("Simulação: o órgão recusou a emissão (há pendências).", false));

        var hoje = DateOnly.FromDateTime(clock.UtcNow);
        var validade = hoje.AddDays(tipo == TipoCertidao.Fgts ? 30 : 180);
        var numero = $"SIM-{(int)tipo}-{hoje:yyyyMMdd}-{empresa.Documento[^4..]}";
        var pdf = PdfSimples.Gerar(
            "CERTIDAO SIMULADA - SEM VALOR LEGAL",
            CatalogoCertidoes.Nome(tipo),
            $"Contribuinte: {empresa.RazaoSocial}",
            $"Documento: {empresa.Documento}",
            $"Numero: {numero}",
            $"Emissao: {hoje:dd/MM/yyyy}  Validade: {validade:dd/MM/yyyy}");
        return Task.FromResult(ResultadoEmissao.Ok(new CertidaoEmitida(
            SituacaoCertidao.Negativa, numero, hoje, validade, pdf, $"{NomeArquivo(tipo)}-{hoje:yyyyMMdd}.pdf",
            "Emitida pelo emissor simulado (ambiente de desenvolvimento).")));
    }

    internal static string NomeArquivo(TipoCertidao tipo) => tipo switch
    {
        TipoCertidao.FederalPgfn => "cnd-federal",
        TipoCertidao.Fgts => "crf-fgts",
        TipoCertidao.Trabalhista => "cndt",
        TipoCertidao.Estadual => "cnd-estadual",
        _ => "cnd-municipal"
    };
}

/// <summary>
/// Emissão pela API da Infosimples (https://infosimples.com/consultas/), que resolve o captcha dos portais oficiais
/// e devolve os dados e o comprovante (PDF) da certidão. Cobrança por consulta.
/// </summary>
public sealed class EmissorCertidaoInfosimples(
    HttpClient http, IOptions<EmissaoCertidaoOptions> options, ILogger<EmissorCertidaoInfosimples> logger) : IEmissorCertidao
{
    private InfosimplesOptions Opcoes => options.Value.Infosimples;

    public string Nome => "Infosimples";
    public bool Habilitado => !string.IsNullOrWhiteSpace(Opcoes.Token);
    public IReadOnlyCollection<TipoCertidao> TiposSuportados => Opcoes.Consultas.Keys;

    public async Task<ResultadoEmissao> EmitirAsync(TipoCertidao tipo, DadosEmpresaEmissao empresa, CancellationToken ct = default)
    {
        if (!Opcoes.Consultas.TryGetValue(tipo, out var consulta))
            return ResultadoEmissao.Falha($"Consulta da certidão {CatalogoCertidoes.Nome(tipo)} não configurada.", false);

        var campos = new Dictionary<string, string>
        {
            ["token"] = Opcoes.Token!,
            [empresa.EhCnpj ? "cnpj" : "cpf"] = empresa.Documento,
            ["timeout"] = Opcoes.TimeoutSegundos.ToString(CultureInfo.InvariantCulture),
        };
        // Com uma certidão positiva com efeitos de negativa vigente, a RFB/PGFN não emite outra:
        // "2via" faz a API buscar primeiro a segunda via da certidão válida.
        if (tipo == TipoCertidao.FederalPgfn) campos["preferencia_emissao"] = "2via";

        using var resposta = await http.PostAsync(new Uri(new Uri(Opcoes.UrlBase), consulta), new FormUrlEncodedContent(campos), ct);
        if (!resposta.IsSuccessStatusCode)
            return ResultadoEmissao.Falha($"Infosimples respondeu HTTP {(int)resposta.StatusCode}.",
                resposta.StatusCode is HttpStatusCode.TooManyRequests || (int)resposta.StatusCode >= 500);

        using var json = await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var analise = InfosimplesParser.Analisar(tipo, json.RootElement);
        if (analise.Erro is not null) return ResultadoEmissao.Falha(analise.Erro, analise.Retentavel);

        var pdf = await BaixarPdfAsync(analise.UrlPdf!, ct);
        if (pdf is null)
            return ResultadoEmissao.Falha("A certidão foi consultada, mas o comprovante em PDF não pôde ser baixado.", true);

        var d = analise.Dados!;
        logger.LogInformation("Infosimples: certidão {Tipo} obtida (situação {Situacao}, validade {Validade})", tipo, d.Situacao, d.Validade);
        return ResultadoEmissao.Ok(d with { Pdf = pdf });
    }

    private async Task<byte[]?> BaixarPdfAsync(string url, CancellationToken ct)
    {
        try
        {
            var bytes = await http.GetByteArrayAsync(url, ct);
            return PdfSimples.EhPdf(bytes) ? bytes : null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Falha ao baixar o PDF da certidão na Infosimples");
            return null;
        }
    }
}

/// <summary>Interpreta a resposta da Infosimples (separado do HTTP para ser testável).</summary>
public static class InfosimplesParser
{
    /// <summary>Códigos de erro da API que indicam problema temporário (fonte fora do ar, timeout, instabilidade).</summary>
    private static readonly HashSet<int> Retentaveis = [600, 605, 609, 610, 613, 614, 615, 618];
    private static readonly string[] FormatosData = ["dd/MM/yyyy", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss"];

    public sealed record Resultado(CertidaoEmitida? Dados, string? UrlPdf, string? Erro, bool Retentavel);

    public static Resultado Analisar(TipoCertidao tipo, JsonElement raiz)
    {
        var codigo = raiz.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : 0;
        if (codigo != 200)
        {
            var mensagem = Texto(raiz, "code_message") ?? "Erro não informado.";
            if (raiz.TryGetProperty("errors", out var erros) && erros.ValueKind == JsonValueKind.Array)
            {
                var detalhes = erros.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
                if (detalhes.Count > 0) mensagem += " " + string.Join(" ", detalhes);
            }
            return new(null, null, $"Infosimples ({codigo}): {mensagem.Trim()}", Retentaveis.Contains(codigo));
        }

        if (!raiz.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
            return new(null, null, "A consulta não retornou dados da certidão.", false);
        var item = data[0];

        var url = UrlComprovante(raiz, item);

        var (numero, emissao, validade, situacao) = tipo switch
        {
            TipoCertidao.Fgts => (
                Texto(item, "crf", "certidao_codigo"),
                Data(item, "validade_inicio_data", "emissao_data", "datahora"),
                Data(item, "validade_fim_data", "validade_data"),
                SituacaoFgts(item)),
            _ => (
                Texto(item, "certidao_codigo", "numero_certidao", "codigo_controle"),
                Data(item, "emissao_data", "data_emissao", "normalizado_expedicao", "expedicao"),
                Data(item, "validade_data", "data_validade", "normalizado_validade", "validade"),
                Situacao(item))
        };

        // Sem validade: o órgão não emitiu (ex.: débitos impedem a CND federal; empregador irregular no FGTS).
        if (validade is null || situacao is null)
        {
            var motivo = Texto(item, "mensagem", "situacao", "observacoes") ?? "o órgão não emitiu a certidão";
            return new(null, null, $"Certidão não emitida: {motivo}", false);
        }
        if (url is null) return new(null, null, "A consulta não devolveu o comprovante (PDF) da certidão.", true);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var dados = new CertidaoEmitida(situacao.Value, numero, emissao ?? hoje, validade.Value, [],
            $"{EmissorCertidaoSimulado.NomeArquivo(tipo)}-{(emissao ?? hoje):yyyyMMdd}.pdf",
            Texto(item, "observacoes", "mensagem"));
        return new(dados, url, null, false);
    }

    /// <summary>
    /// URL do comprovante (PDF). A Infosimples já devolveu isso em lugares diferentes conforme a versão/consulta:
    /// <c>receipt.sites_urls</c> (cliente oficial), <c>site_receipts</c> na raiz, ou <c>site_receipt</c>/<c>consulta_comprovante</c>
    /// no item. Prefere links de PDF; o download confere a assinatura do arquivo de qualquer forma.
    /// </summary>
    private static string? UrlComprovante(JsonElement raiz, JsonElement item)
    {
        var candidatas = new List<string>();
        void Adicionar(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.String && Uri.TryCreate(e.GetString(), UriKind.Absolute, out var u)
                && u.Scheme is "https" or "http")
                candidatas.Add(u.ToString());
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray()) Adicionar(x);
        }

        if (raiz.TryGetProperty("receipt", out var receipt) && receipt.ValueKind == JsonValueKind.Object
            && receipt.TryGetProperty("sites_urls", out var sites)) Adicionar(sites);
        foreach (var campo in new[] { "site_receipt", "site_receipts", "consulta_comprovante" })
            if (item.TryGetProperty(campo, out var v)) Adicionar(v);
        if (raiz.TryGetProperty("site_receipts", out var recibos)) Adicionar(recibos);

        return candidatas.FirstOrDefault(u => u.Contains(".pdf", StringComparison.OrdinalIgnoreCase)) ?? candidatas.FirstOrDefault();
    }

    private static SituacaoCertidao? SituacaoFgts(JsonElement item)
    {
        var s = Texto(item, "situacao");
        if (s is null) return Texto(item, "crf") is null ? null : SituacaoCertidao.Negativa;
        return s.Contains("irregular", StringComparison.OrdinalIgnoreCase) ? null
            : s.Contains("regular", StringComparison.OrdinalIgnoreCase) ? SituacaoCertidao.Negativa
            : null;
    }

    /// <summary>Negativa / positiva com efeitos de negativa / positiva, pelo texto do tipo ou pela flag da consulta.</summary>
    private static SituacaoCertidao? Situacao(JsonElement item)
    {
        var texto = string.Join(" ", new[] { Texto(item, "tipo"), Texto(item, "certidao"), Texto(item, "titulo"), Texto(item, "situacao") }
            .Where(t => t is not null)).ToLowerInvariant();
        if (texto.Contains("efeito de negativa") || texto.Contains("efeitos de negativa")) return SituacaoCertidao.PositivaComEfeitoNegativa;
        if (texto.Contains("positiva")) return SituacaoCertidao.Positiva;
        if (texto.Contains("negativa")) return SituacaoCertidao.Negativa;
        return item.TryGetProperty("conseguiu_emitir_certidao_negativa", out var ok) && ok.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? ok.GetBoolean() ? SituacaoCertidao.Negativa : SituacaoCertidao.Positiva
            : SituacaoCertidao.Negativa;
    }

    private static string? Texto(JsonElement item, params string[] campos)
    {
        foreach (var campo in campos)
        {
            if (!item.TryGetProperty(campo, out var v)) continue;
            var s = v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
        }
        return null;
    }

    private static DateOnly? Data(JsonElement item, params string[] campos)
    {
        foreach (var campo in campos)
        {
            var s = Texto(item, campo);
            if (s is null) continue;
            if (DateTime.TryParseExact(s, FormatosData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return DateOnly.FromDateTime(d);
        }
        return null;
    }
}

/// <summary>Gera um PDF mínimo de uma página com linhas de texto (para o emissor simulado).</summary>
public static class PdfSimples
{
    public static bool EhPdf(byte[] bytes) =>
        bytes.Length > 4 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F';

    public static byte[] Gerar(params string[] linhas)
    {
        static string Escapar(string s) =>
            new string(s.Select(ch => ch < 128 ? ch : '?').ToArray()).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

        var texto = new StringBuilder("BT /F1 14 Tf 50 780 Td 18 TL\n");
        foreach (var l in linhas) texto.Append('(').Append(Escapar(l)).Append(") Tj T*\n");
        texto.Append("ET");
        var conteudo = texto.ToString();

        string[] objetos =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {conteudo.Length} >>\nstream\n{conteudo}\nendstream"
        ];

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objetos.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objetos[i]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objetos.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) pdf.Append($"{o:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objetos.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
