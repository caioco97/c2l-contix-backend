using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;
using Contix.Infrastructure.Persistence;

namespace Contix.Infrastructure.Email;

/// <summary>Grava o e-mail na caixa de saída (outbox) do mesmo DbContext da requisição.</summary>
public sealed class EmailQueue(ContixDbContext context, IDateTimeProvider clock) : IEmailQueue
{
    public async Task EnfileirarAsync(EmailMensagem m, CancellationToken ct = default) =>
        await context.EmailsPendentes.AddAsync(new EmailPendente
        {
            Para = m.Para,
            NomePara = m.NomePara,
            Assunto = m.Assunto,
            CorpoHtml = m.CorpoHtml,
            CorpoTexto = m.CorpoTexto,
            ProximaTentativaEm = clock.UtcNow
        }, ct);
}
