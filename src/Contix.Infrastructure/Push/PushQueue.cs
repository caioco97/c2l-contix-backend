using System.Text.Json;
using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;
using Contix.Infrastructure.Persistence;

namespace Contix.Infrastructure.Push;

/// <summary>Grava o push na caixa de saída (outbox) do mesmo DbContext da requisição.</summary>
public sealed class PushQueue(ContixDbContext context, IDateTimeProvider clock) : IPushQueue
{
    public async Task EnfileirarAsync(PushMensagem m, CancellationToken ct = default) =>
        await context.PushesPendentes.AddAsync(new PushPendente
        {
            UsuarioId = m.UsuarioId,
            Titulo = Limitar(m.Titulo, 300),
            Mensagem = Limitar(m.Mensagem, 2000),
            Dados = JsonSerializer.Serialize(m.Dados.Where(d => d.Value is not null).ToDictionary()),
            ProximaTentativaEm = clock.UtcNow
        }, ct);

    private static string Limitar(string texto, int max) => texto.Length > max ? texto[..max] : texto;
}
