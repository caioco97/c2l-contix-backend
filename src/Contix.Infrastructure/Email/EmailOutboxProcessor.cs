using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;
using Contix.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contix.Infrastructure.Email;

/// <summary>Envia os e-mails pendentes da caixa de saída, com novas tentativas e backoff exponencial.</summary>
public sealed class EmailOutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IEmailSender sender,
    IDateTimeProvider clock,
    IOptions<EmailOptions> options,
    ILogger<EmailOutboxProcessor> logger)
{
    /// <returns>Quantidade de e-mails enviados com sucesso.</returns>
    public async Task<int> ProcessarAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContixDbContext>();
        var agora = clock.UtcNow;

        // FOR UPDATE SKIP LOCKED: várias instâncias da API podem processar a fila sem enviar o mesmo e-mail duas vezes.
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var lote = db.Database.IsRelational()
            ? await db.EmailsPendentes.FromSql($"""
                SELECT * FROM emails_pendentes
                WHERE enviado_em IS NULL AND tentativas < {EmailPendente.MaxTentativas} AND proxima_tentativa_em <= {agora}
                ORDER BY proxima_tentativa_em
                LIMIT {options.Value.LoteFila}
                FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct)
            : await db.EmailsPendentes
                .Where(e => e.EnviadoEm == null && e.Tentativas < EmailPendente.MaxTentativas && e.ProximaTentativaEm <= agora)
                .OrderBy(e => e.ProximaTentativaEm).Take(options.Value.LoteFila).ToListAsync(ct);

        var enviados = 0;
        foreach (var email in lote)
        {
            try
            {
                await sender.EnviarAsync(email, ct);
                email.RegistrarEnvio(clock.UtcNow);
                enviados++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                email.RegistrarFalha(clock.UtcNow, ex.Message);
                if (email.Desistiu)
                    logger.LogError(ex, "E-mail {EmailId} descartado após {Tentativas} tentativas", email.Id, email.Tentativas);
                else
                    logger.LogWarning(ex, "Falha ao enviar e-mail {EmailId} (tentativa {Tentativas})", email.Id, email.Tentativas);
            }
        }

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return enviados;
    }
}
