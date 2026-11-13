using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Ganss.Xss;

namespace Contix.Application.Features.Noticias;

/// <summary>
/// Conteúdo das notícias em HTML. O editor do escritório envia HTML; as notícias antigas estão em texto
/// simples (parágrafos separados por linha em branco) e são convertidas para HTML na leitura, sem migration.
/// Todo HTML passa por uma lista de permissões antes de ser salvo e de novo antes de ser devolvido.
/// </summary>
public static partial class ConteudoNoticia
{
    /// <summary>Tags aceitas. Qualquer outra é removida, mas o texto dentro dela é mantido.</summary>
    public static readonly string[] TagsPermitidas =
        ["p", "br", "strong", "b", "em", "i", "u", "s", "h2", "h3", "ul", "ol", "li", "a", "blockquote"];

    private static readonly HashSet<string> TagsDescartadasComConteudo = new(
        ["script", "style", "iframe", "object", "embed", "noscript", "template", "textarea", "select", "svg", "math", "head", "title"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] EsquemasPermitidos = ["http", "https", "mailto", "tel"];

    /// <summary>Limite do HTML gravado (tamanho da coluna).</summary>
    public const int TamanhoMaximo = 20_000;

    private static readonly HtmlSanitizer SanitizerGravacao = CriarSanitizer(paraExibir: false);
    private static readonly HtmlSanitizer SanitizerExibicao = CriarSanitizer(paraExibir: true);

    private static HtmlSanitizer CriarSanitizer(bool paraExibir)
    {
        var options = new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(TagsPermitidas, StringComparer.OrdinalIgnoreCase),
            AllowedAttributes = new HashSet<string>(["href"], StringComparer.OrdinalIgnoreCase),
            AllowedSchemes = new HashSet<string>(EsquemasPermitidos, StringComparer.OrdinalIgnoreCase),
            UriAttributes = new HashSet<string>(["href"], StringComparer.OrdinalIgnoreCase),
            AllowedCssProperties = new HashSet<string>(),
            AllowedAtRules = new HashSet<AngleSharp.Css.Dom.CssRuleType>(),
        };
        var sanitizer = new HtmlSanitizer(options) { KeepChildNodes = true };
        // Para estas tags o conteúdo também sai (o texto de um <script> não é texto da notícia).
        sanitizer.RemovingTag += (_, e) =>
        {
            if (!TagsDescartadasComConteudo.Contains(e.Tag.LocalName)) return;
            e.Tag.Remove();
            e.Cancel = true;
        };
        // Só links absolutos com esquema permitido. Na exibição, abrem em nova aba sem expor a página de origem
        // (os atributos não são gravados, para não ocupar espaço da coluna).
        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is not AngleSharp.Dom.IElement el || !el.LocalName.Equals("a", StringComparison.OrdinalIgnoreCase)) return;
            var href = el.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || !Uri.TryCreate(href, UriKind.Absolute, out var uri)
                || !EsquemasPermitidos.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
                el.RemoveAttribute("href");
            else if (paraExibir)
            {
                el.SetAttribute("target", "_blank");
                el.SetAttribute("rel", "noopener noreferrer nofollow");
            }
        };
        return sanitizer;
    }

    /// <summary>O texto parece HTML gerado pelo editor (começa com uma tag de bloco conhecida)?</summary>
    public static bool EhHtml(string? conteudo) =>
        !string.IsNullOrWhiteSpace(conteudo) && InicioHtml().IsMatch(conteudo);

    /// <summary>Normaliza o conteúdo para gravar: HTML sanitizado; texto simples vira parágrafos.</summary>
    public static string ParaGravar(string? conteudo) => Normalizar(conteudo, SanitizerGravacao);

    /// <summary>HTML seguro para exibição, aceitando tanto o formato novo (HTML) quanto o antigo (texto).</summary>
    public static string ParaExibir(string? conteudo) => Normalizar(conteudo, SanitizerExibicao);

    private static string Normalizar(string? conteudo, HtmlSanitizer sanitizer)
    {
        if (string.IsNullOrWhiteSpace(conteudo)) return string.Empty;
        var texto = conteudo.Trim();
        return EhHtml(texto) ? sanitizer.Sanitize(texto).Trim() : TextoParaHtml(texto);
    }

    /// <summary>Texto puro do conteúdo (sem tags), para o resumo automático e para validar se há texto.</summary>
    public static string ParaTexto(string? conteudo)
    {
        if (string.IsNullOrWhiteSpace(conteudo)) return string.Empty;
        if (!EhHtml(conteudo)) return conteudo;
        var semTags = QuebraDeBloco().Replace(conteudo, " ");
        semTags = Tag().Replace(semTags, string.Empty);
        return WebUtility.HtmlDecode(semTags);
    }

    private static string TextoParaHtml(string texto)
    {
        var html = new StringBuilder();
        foreach (var paragrafo in ParagrafoSeparador().Split(texto))
        {
            var limpo = paragrafo.Trim();
            if (limpo.Length == 0) continue;
            var linhas = limpo.Split('\n').Select(l => Escapar(l.TrimEnd('\r')));
            html.Append("<p>").Append(string.Join("<br>", linhas)).Append("</p>");
        }
        return html.ToString();
    }

    /// <summary>Escapa só os caracteres especiais do HTML, mantendo acentos legíveis.</summary>
    private static string Escapar(string texto) => texto
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

    [GeneratedRegex(@"^\s*<(p|h[1-6]|ul|ol|blockquote|div|br)[\s/>]", RegexOptions.IgnoreCase)]
    private static partial Regex InicioHtml();

    [GeneratedRegex(@"\r?\n\s*\r?\n")]
    private static partial Regex ParagrafoSeparador();

    [GeneratedRegex(@"<\s*(br|/p|/h[1-6]|/li|/blockquote|/div)\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex QuebraDeBloco();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tag();
}
