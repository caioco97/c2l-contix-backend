using System.Net;
using Contix.Application.Common.Abstractions;

namespace Contix.Application.Features.Notificacoes;

/// <summary>Monta os e-mails da plataforma com a identidade visual do escritório.</summary>
public static class EmailTemplates
{
    public sealed record Marca(string NomeEscritorio, string? LogoUrl);

    public static EmailMensagem Montar(
        string para, string? nomePara, string assunto, Marca marca,
        string titulo, IReadOnlyList<string> paragrafos, string? textoBotao, string? urlBotao, string? rodape = null)
    {
        static string h(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);
        var cabecalho = string.IsNullOrWhiteSpace(marca.LogoUrl)
            ? $"<span style=\"font-size:20px;font-weight:700;color:#0f3d6e\">{h(marca.NomeEscritorio)}</span>"
            : $"<img src=\"{h(marca.LogoUrl)}\" alt=\"{h(marca.NomeEscritorio)}\" style=\"max-height:48px\">";
        var corpo = string.Concat(paragrafos.Select(p => $"<p style=\"margin:0 0 16px;line-height:1.5\">{h(p)}</p>"));
        var botao = urlBotao is null ? "" :
            $"<p style=\"margin:24px 0\"><a href=\"{h(urlBotao)}\" style=\"background:#1d6fd1;color:#fff;text-decoration:none;" +
            $"padding:12px 20px;border-radius:8px;font-weight:600;display:inline-block\">{h(textoBotao ?? "Abrir")}</a></p>";
        var saudacao = string.IsNullOrWhiteSpace(nomePara) ? "Olá," : $"Olá, {h(nomePara)}!";
        var rodapeHtml = rodape is null ? "" : $"<p style=\"margin:16px 0 0;color:#64748b;font-size:12px\">{h(rodape)}</p>";

        var html = $"""
            <!doctype html>
            <html lang="pt-BR"><body style="margin:0;background:#f1f5f9;font-family:Arial,Helvetica,sans-serif;color:#0f172a">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:32px 16px">
            <table role="presentation" width="100%" style="max-width:560px;background:#fff;border-radius:12px;padding:32px">
            <tr><td style="padding-bottom:24px">{cabecalho}</td></tr>
            <tr><td>
            <h1 style="font-size:20px;margin:0 0 16px">{h(titulo)}</h1>
            <p style="margin:0 0 16px">{saudacao}</p>
            {corpo}{botao}{rodapeHtml}
            </td></tr></table>
            <p style="color:#94a3b8;font-size:12px;margin-top:16px">Enviado por {h(marca.NomeEscritorio)} via Contix.</p>
            </td></tr></table></body></html>
            """;

        var texto = string.Join("\n\n", new[]
        {
            string.IsNullOrWhiteSpace(nomePara) ? "Olá," : $"Olá, {nomePara}!",
            titulo
        }.Concat(paragrafos)
         .Concat(urlBotao is null ? [] : [$"{textoBotao ?? "Abrir"}: {urlBotao}"])
         .Concat(rodape is null ? [] : [rodape])
         .Append($"— {marca.NomeEscritorio} via Contix"));

        return new EmailMensagem(para, nomePara, assunto, html, texto);
    }

    public static string Url(string urlFrontend, string? rota) =>
        rota is null ? urlFrontend.TrimEnd('/') : urlFrontend.TrimEnd('/') + "/" + rota.TrimStart('/');
}
