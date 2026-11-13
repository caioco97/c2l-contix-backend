using System.Text.Json;
using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;
using Contix.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contix.Infrastructure.Push;

/// <summary>
/// Envia os pushes pendentes para os aparelhos dos usuários, com novas tentativas e backoff exponencial.
/// Aparelhos que o Expo informa como não registrados (app desinstalado) são removidos.
/// </summary>
public sealed class PushOutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IPushSender sender,
    IDateTimeProvider clock,
    IOptions<PushOptions> options,
    ILogger<PushOutboxProcessor> logger)
{
    /// <returns>Quantidade de pushes processados com sucesso (inclusive de usuários sem aparelho).</returns>
    public async Task<int> ProcessarAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContixDbContext>();
        var agora = clock.UtcNow;

        // FOR UPDATE SKIP LOCKED: várias instâncias da API podem processar a fila sem enviar o mesmo push duas vezes.
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var lote = db.Database.IsRelational()
            ? await db.PushesPendentes.FromSql($"""
                SELECT * FROM pushes_pendentes
                WHERE enviado_em IS NULL AND tentativas < {PushPendente.MaxTentativas} AND proxima_tentativa_em <= {agora}
                ORDER BY proxima_tentativa_em
                LIMIT {options.Value.LoteFila}
                FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct)
            : await db.PushesPendentes
                .Where(p => p.EnviadoEm == null && p.Tentativas < PushPendente.MaxTentativas && p.ProximaTentativaEm <= agora)
                .OrderBy(p => p.ProximaTentativaEm).Take(options.Value.LoteFila).ToListAsync(ct);
        if (lote.Count == 0) return 0;

        // Aparelhos dos usuários do lote (só usuários ativos recebem push).
        var usuarioIds = lote.Select(p => p.UsuarioId).Distinct().ToList();
        var dispositivos = await db.DispositivosPush
            .Where(d => usuarioIds.Contains(d.UsuarioId)
                        && db.Usuarios.IgnoreQueryFilters().Any(u => u.Id == d.UsuarioId && u.Ativo && !u.IsDeleted))
            .ToListAsync(ct);
        var porUsuario = dispositivos.ToLookup(d => d.UsuarioId);

        var mensagens = new List<(PushPendente Push, MensagemPush Mensagem)>();
        foreach (var push in lote)
        {
            var dados = Dados(push);
            mensagens.AddRange(porUsuario[push.UsuarioId].Select(d => (push, new MensagemPush(d.Token, push.Titulo, push.Mensagem, dados))));
        }

        var processados = 0;
        try
        {
            var resultados = mensagens.Count == 0 ? [] : await sender.EnviarAsync(mensagens.Select(m => m.Mensagem).ToList(), ct);

            var invalidos = resultados.Where(r => r.TokenInvalido).Select(r => r.Token).ToHashSet();
            if (invalidos.Count > 0)
            {
                db.DispositivosPush.RemoveRange(dispositivos.Where(d => invalidos.Contains(d.Token)));
                logger.LogInformation("{Quantidade} aparelho(s) removido(s) do push (não registrados no Expo)", invalidos.Count);
            }

            // Um push conta como enviado quando ao menos um aparelho recebeu, ou quando não há o que tentar de novo
            // (usuário sem aparelho ou todos os aparelhos inválidos). Falhas temporárias voltam para a fila.
            var porPush = mensagens.Zip(resultados).ToLookup(x => x.First.Push, x => x.Second);
            foreach (var push in lote)
            {
                var deste = porPush[push].ToList();
                if (deste.Count == 0 || deste.Any(r => r.Sucesso) || deste.All(r => r.TokenInvalido))
                {
                    push.RegistrarEnvio(clock.UtcNow);
                    processados++;
                }
                else
                    RegistrarFalha(push, deste.First(r => !r.Sucesso).Erro ?? "Falha no envio do push.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Falha ao enviar o lote de {Quantidade} push(es)", lote.Count);
            foreach (var push in lote) RegistrarFalha(push, ex.Message);
        }

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return processados;
    }

    private void RegistrarFalha(PushPendente push, string erro)
    {
        push.RegistrarFalha(clock.UtcNow, erro);
        if (push.Desistiu)
            logger.LogError("Push {PushId} descartado após {Tentativas} tentativas: {Erro}", push.Id, push.Tentativas, erro);
    }

    private static IReadOnlyDictionary<string, string> Dados(PushPendente push)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(push.Dados) ?? [];
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
