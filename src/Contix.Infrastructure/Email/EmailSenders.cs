using Contix.Domain.Entities;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Contix.Infrastructure.Email;

/// <summary>Entrega efetiva de um e-mail da caixa de saída.</summary>
public interface IEmailSender
{
    Task EnviarAsync(EmailPendente email, CancellationToken ct = default);
}

/// <summary>SMTP via MailKit. Em desenvolvimento aponta para o Mailpit do docker compose.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task EnviarAsync(EmailPendente email, CancellationToken ct = default)
    {
        var o = options.Value;
        var mensagem = new MimeMessage();
        mensagem.From.Add(new MailboxAddress(o.NomeRemetente, o.Remetente));
        mensagem.To.Add(new MailboxAddress(email.NomePara ?? email.Para, email.Para));
        mensagem.Subject = email.Assunto;
        mensagem.Body = new BodyBuilder { HtmlBody = email.CorpoHtml, TextBody = email.CorpoTexto }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(o.Smtp.Host, o.Smtp.Porta, Enum.Parse<SecureSocketOptions>(o.Smtp.Seguranca, ignoreCase: true), ct);
        if (!string.IsNullOrEmpty(o.Smtp.Usuario))
            await client.AuthenticateAsync(o.Smtp.Usuario, o.Smtp.Senha ?? string.Empty, ct);
        await client.SendAsync(mensagem, ct);
        await client.DisconnectAsync(true, ct);
    }
}

/// <summary>Não envia: apenas registra. Útil em testes e ambientes sem SMTP.</summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task EnviarAsync(EmailPendente email, CancellationToken ct = default)
    {
        logger.LogInformation("E-mail (não enviado, provider Log) para {Para}: {Assunto}\n{Corpo}", email.Para, email.Assunto, email.CorpoTexto);
        return Task.CompletedTask;
    }
}
