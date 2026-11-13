using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Contix.Application.Common.Abstractions;
using Contix.Application.Features.NotasFiscais;
using Contix.Application.Features.NotasFiscais.Sefaz;
using Contix.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contix.Infrastructure.NotasFiscais;

/// <summary>Sem integração configurada: nada é consultado nem enviado.</summary>
public sealed class SefazNFeDesabilitada : ISefazNFeGateway
{
    public string Nome => "Desabilitado";
    public bool Habilitado => false;
    public string Ambiente => "";
    public Task<RetornoDistribuicao> DistribuicaoPorNsuAsync(CertificadoA1 certificado, string cnpj, int codigoUf, long ultimoNsu, CancellationToken ct = default) =>
        throw new InvalidOperationException("Integração com a SEFAZ desabilitada.");
    public Task<RetornoEvento> ManifestarAsync(CertificadoA1 certificado, string cnpj, string chaveAcesso, ManifestacaoNFe tipo, string? justificativa, CancellationToken ct = default) =>
        throw new InvalidOperationException("Integração com a SEFAZ desabilitada.");
}

/// <summary>
/// Web services reais da SEFAZ (Ambiente Nacional), com autenticação mútua TLS pelo certificado A1 da empresa.
/// Uma conexão por chamada: cada empresa usa o seu certificado.
/// </summary>
public sealed class SefazNFeGateway(IOptions<SefazOptions> options, IDateTimeProvider clock, ILogger<SefazNFeGateway> logger) : ISefazNFeGateway
{
    private readonly SefazOptions _o = options.Value;
    private readonly Lazy<X509Certificate2Collection> _icpBrasil = new(() => CarregarCadeia(options.Value.CadeiaIcpBrasil));

    public string Nome => "Sefaz";
    public bool Habilitado => true;
    public string Ambiente => _o.EhProducao ? "Producao" : "Homologacao";
    private int TpAmb => _o.EhProducao ? 1 : 2;

    public async Task<RetornoDistribuicao> DistribuicaoPorNsuAsync(CertificadoA1 certificado, string cnpj, int codigoUf, long ultimoNsu, CancellationToken ct = default)
    {
        var corpo = MensagensSefaz.Envelope(MensagensSefaz.DistDFeInt(TpAmb, codigoUf, cnpj, ultimoNsu), MensagensSefaz.WsdlDistribuicao, embrulharOperacao: true);
        using var cert = certificado.Carregar();
        var resposta = await PostarAsync(_o.Urls.DistribuicaoDfe, MensagensSefaz.AcaoDistribuicao, corpo, cert, ct);
        return MensagensSefaz.LerRetornoDistribuicao(resposta);
    }

    public async Task<RetornoEvento> ManifestarAsync(CertificadoA1 certificado, string cnpj, string chaveAcesso, ManifestacaoNFe tipo, string? justificativa, CancellationToken ct = default)
    {
        using var cert = certificado.Carregar();
        // Um pouco no passado: a SEFAZ rejeita dhEvento à frente do relógio dela (cStat 578).
        var agora = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero).AddSeconds(-30);
        var env = MensagensSefaz.EnvEventoAssinado(TpAmb, cnpj, chaveAcesso, tipo, justificativa, agora, cert);
        var resposta = await PostarAsync(_o.Urls.RecepcaoEvento, MensagensSefaz.AcaoEvento, MensagensSefaz.Envelope(env, MensagensSefaz.WsdlEvento, embrulharOperacao: false), cert, ct);
        return MensagensSefaz.LerRetornoEvento(resposta);
    }

    private async Task<string> PostarAsync(string url, string acao, string envelope, X509Certificate2 cert, CancellationToken ct)
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                ClientCertificates = new X509CertificateCollection { cert },
                RemoteCertificateValidationCallback = ValidarServidor
            },
            PooledConnectionLifetime = TimeSpan.FromMinutes(1)
        };
        using var http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(Math.Max(10, _o.TimeoutSegundos)) };
        using var conteudo = new StringContent(envelope, Encoding.UTF8);
        conteudo.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse($"application/soap+xml; charset=utf-8; action=\"{acao}\"");
        using var resposta = await http.PostAsync(url, conteudo, ct);
        var texto = await resposta.Content.ReadAsStringAsync(ct);
        if (!resposta.IsSuccessStatusCode)
        {
            logger.LogWarning("SEFAZ respondeu {Status} em {Url}: {Corpo}", (int)resposta.StatusCode, url, texto.Length > 500 ? texto[..500] : texto);
            // SOAP Fault com 500 ainda traz o motivo; o leitor procura o retorno e lança se não houver.
            if ((int)resposta.StatusCode != 500) throw new HttpRequestException($"A SEFAZ respondeu HTTP {(int)resposta.StatusCode}.");
        }
        return texto;
    }

    /// <summary>Aceita a cadeia do sistema e, se configurada, as ACs da ICP-Brasil (usadas nos servidores da SEFAZ).</summary>
    private bool ValidarServidor(object _, X509Certificate? certificado, X509Chain? cadeia, SslPolicyErrors erros)
    {
        if (erros == SslPolicyErrors.None) return true;
        if (erros != SslPolicyErrors.RemoteCertificateChainErrors || certificado is null || _icpBrasil.Value.Count == 0) return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(_icpBrasil.Value);
        chain.ChainPolicy.ExtraStore.AddRange(_icpBrasil.Value);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        using var servidor = new X509Certificate2(certificado);
        var ok = chain.Build(servidor);
        if (!ok) logger.LogWarning("Certificado do servidor da SEFAZ não validado pela cadeia ICP-Brasil: {Subject}", servidor.Subject);
        return ok;
    }

    private static X509Certificate2Collection CarregarCadeia(string? pasta)
    {
        var colecao = new X509Certificate2Collection();
        if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta)) return colecao;
        foreach (var arquivo in Directory.EnumerateFiles(pasta).Where(f => f.EndsWith(".crt", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".cer", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)))
        {
            if (arquivo.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)) colecao.ImportFromPemFile(arquivo);
            else colecao.Add(X509CertificateLoader.LoadCertificateFromFile(arquivo));
        }
        return colecao;
    }
}

/// <summary>
/// SEFAZ de mentira para desenvolvimento e testes. Na primeira busca de cada CNPJ devolve três resumos (resNFe)
/// de fornecedores fictícios; depois da ciência de uma nota, a busca seguinte traz o XML completo dela.
/// Notas com emitente "Fornecedor Cancelado" chegam já com o evento de cancelamento.
/// </summary>
public sealed class SefazNFeSimulada(IDateTimeProvider clock) : ISefazNFeGateway
{
    private sealed class Fila
    {
        public readonly List<DocumentoDistribuido> Documentos = [];
        public readonly Dictionary<string, (string Emitente, string Nome, decimal Valor, DateTime Emissao)> Notas = [];
    }

    private static readonly ConcurrentDictionary<string, Fila> Filas = new();
    private static readonly XNamespace N = LeitorXmlNFe.Ns;

    public string Nome => "Simulado";
    public bool Habilitado => true;
    public string Ambiente => "Homologacao";

    public Task<RetornoDistribuicao> DistribuicaoPorNsuAsync(CertificadoA1 certificado, string cnpj, int codigoUf, long ultimoNsu, CancellationToken ct = default)
    {
        var fila = Filas.GetOrAdd(cnpj, _ => new Fila());
        lock (fila)
        {
            if (fila.Documentos.Count == 0) Popular(fila, cnpj, codigoUf);
            var max = fila.Documentos[^1].Nsu;
            var docs = fila.Documentos.Where(d => d.Nsu > ultimoNsu).Take(50).ToList();
            return Task.FromResult(docs.Count == 0
                ? new RetornoDistribuicao("137", "Nenhum documento localizado", max, max, [])
                : new RetornoDistribuicao("138", "Documento(s) localizado(s)", docs[^1].Nsu, max, docs));
        }
    }

    public Task<RetornoEvento> ManifestarAsync(CertificadoA1 certificado, string cnpj, string chaveAcesso, ManifestacaoNFe tipo, string? justificativa, CancellationToken ct = default)
    {
        var protocolo = "9" + clock.UtcNow.ToString("yyMMddHHmmssfff", CultureInfo.InvariantCulture)[..14];
        var fila = Filas.GetOrAdd(cnpj, _ => new Fila());
        lock (fila)
        {
            // Depois da ciência (ou de outra manifestação), a SEFAZ libera o XML completo da nota.
            if (fila.Notas.TryGetValue(chaveAcesso, out var n) && !fila.Documentos.Any(d => d.Schema.StartsWith("procNFe") && d.Xml.Contains(chaveAcesso)))
                Adicionar(fila, "procNFe_v4.00.xsd", ProcNFe(chaveAcesso, n.Emitente, n.Nome, cnpj, n.Valor, n.Emissao));
        }
        return Task.FromResult(new RetornoEvento(true, "135", "Evento registrado e vinculado a NF-e", protocolo));
    }

    private void Popular(Fila fila, string cnpj, int codigoUf)
    {
        var hoje = clock.UtcNow;
        var fornecedores = new[]
        {
            ("Distribuidora Simulada de Papelaria Ltda", 1_234.56m, 2),
            ("Fornecedor Simulado de Informática S.A.", 8_900.00m, 5),
            ("Fornecedor Cancelado Comércio Ltda", 450.10m, 9)
        };
        var numero = 1000 + Random.Shared.Next(8000);
        foreach (var (nome, valor, diasAtras) in fornecedores)
        {
            var emitente = NovoCnpj();
            var emissao = hoje.AddDays(-diasAtras);
            var chave = Chave(codigoUf, emissao, emitente, numero++);
            fila.Notas[chave] = (emitente, nome, valor, emissao);
            Adicionar(fila, "resNFe_v1.01.xsd", ResNFe(chave, emitente, nome, valor, emissao));
            if (nome.Contains("Cancelado"))
                Adicionar(fila, "resEvento_v1.01.xsd", new XElement(N + "resEvento", new XAttribute("versao", "1.01"),
                    new XElement(N + "cOrgao", codigoUf), new XElement(N + "CNPJ", emitente), new XElement(N + "chNFe", chave),
                    new XElement(N + "dhEvento", Data(emissao.AddHours(2))), new XElement(N + "tpEvento", "110111"),
                    new XElement(N + "nSeqEvento", 1), new XElement(N + "xEvento", "Cancelamento"),
                    new XElement(N + "dhRecbto", Data(emissao.AddHours(2))), new XElement(N + "nProt", "135000000000001")).ToString());
        }
    }

    private static void Adicionar(Fila fila, string schema, string xml) =>
        fila.Documentos.Add(new DocumentoDistribuido((fila.Documentos.Count == 0 ? 0 : fila.Documentos[^1].Nsu) + 1, schema, xml));

    private static string ResNFe(string chave, string emitente, string nome, decimal valor, DateTime emissao) =>
        new XElement(N + "resNFe", new XAttribute("versao", "1.01"),
            new XElement(N + "chNFe", chave), new XElement(N + "CNPJ", emitente), new XElement(N + "xNome", nome),
            new XElement(N + "IE", "123456789110"), new XElement(N + "dhEmi", Data(emissao)), new XElement(N + "tpNF", 1),
            new XElement(N + "vNF", valor.ToString("0.00", CultureInfo.InvariantCulture)), new XElement(N + "digVal", "c2ltdWxhZG8="),
            new XElement(N + "dhRecbto", Data(emissao.AddMinutes(1))), new XElement(N + "nProt", "135" + chave[^12..]),
            new XElement(N + "cSitNFe", 1)).ToString();

    private static string ProcNFe(string chave, string emitente, string nome, string destinatario, decimal valor, DateTime emissao) =>
        new XElement(N + "nfeProc", new XAttribute("versao", "4.00"),
            new XElement(N + "NFe",
                new XElement(N + "infNFe", new XAttribute("versao", "4.00"), new XAttribute("Id", "NFe" + chave),
                    new XElement(N + "ide", new XElement(N + "cUF", chave[..2]), new XElement(N + "natOp", "VENDA"),
                        new XElement(N + "mod", 55), new XElement(N + "serie", int.Parse(chave.AsSpan(22, 3))),
                        new XElement(N + "nNF", long.Parse(chave.AsSpan(25, 9))), new XElement(N + "dhEmi", Data(emissao)),
                        new XElement(N + "tpNF", 1), new XElement(N + "tpAmb", 2)),
                    new XElement(N + "emit", new XElement(N + "CNPJ", emitente), new XElement(N + "xNome", nome), new XElement(N + "IE", "123456789110")),
                    new XElement(N + "dest", new XElement(N + "CNPJ", destinatario), new XElement(N + "xNome", "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL")),
                    new XElement(N + "det", new XAttribute("nItem", 1),
                        new XElement(N + "prod", new XElement(N + "cProd", "001"), new XElement(N + "xProd", "Produto simulado"),
                            new XElement(N + "qCom", "1.0000"), new XElement(N + "vProd", valor.ToString("0.00", CultureInfo.InvariantCulture)))),
                    new XElement(N + "total", new XElement(N + "ICMSTot", new XElement(N + "vNF", valor.ToString("0.00", CultureInfo.InvariantCulture)))))),
            new XElement(N + "protNFe", new XAttribute("versao", "4.00"),
                new XElement(N + "infProt", new XElement(N + "tpAmb", 2), new XElement(N + "chNFe", chave),
                    new XElement(N + "dhRecbto", Data(emissao.AddMinutes(1))), new XElement(N + "nProt", "135" + chave[^12..]),
                    new XElement(N + "cStat", 100), new XElement(N + "xMotivo", "Autorizado o uso da NF-e")))).ToString();

    private static string Data(DateTime utc) =>
        new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>Chave de acesso com DV válido (módulo 11).</summary>
    public static string Chave(int cUf, DateTime emissao, string cnpjEmitente, int numero)
    {
        var semDv = $"{cUf:D2}{emissao:yyMM}{cnpjEmitente}55001{numero:D9}1{Random.Shared.Next(10_000_000, 99_999_999):D8}";
        var soma = 0;
        var peso = 2;
        for (var i = semDv.Length - 1; i >= 0; i--)
        {
            soma += (semDv[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }
        var resto = soma % 11;
        return semDv + (resto < 2 ? 0 : 11 - resto);
    }

    private static string NovoCnpj()
    {
        var d = Enumerable.Range(0, 8).Select(_ => Random.Shared.Next(10)).Concat([0, 0, 0, 1]).ToList();
        int Dv(IReadOnlyList<int> n, int[] pesos) { var r = n.Select((x, i) => x * pesos[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        d.Add(Dv(d, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        d.Add(Dv(d, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        return string.Concat(d);
    }
}
