namespace Contix.Application.Common.Abstractions;

public sealed record EmailMensagem(string Para, string? NomePara, string Assunto, string CorpoHtml, string CorpoTexto);

/// <summary>
/// Enfileira e-mails na caixa de saída. A gravação acontece no próximo <see cref="IUnitOfWork.SaveChangesAsync"/>,
/// junto com a ação que originou o e-mail; o envio é feito em segundo plano.
/// </summary>
public interface IEmailQueue
{
    Task EnfileirarAsync(EmailMensagem mensagem, CancellationToken ct = default);
}
