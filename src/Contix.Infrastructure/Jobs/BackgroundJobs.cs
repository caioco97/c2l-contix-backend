using Contix.Application.Features.Certidoes.Emissao;
using Contix.Application.Features.Notificacoes;
using Contix.Application.Features.NotasFiscais;
using Contix.Infrastructure.NotasFiscais;
using Contix.Infrastructure.Certidoes;
using Contix.Infrastructure.Email;
using Contix.Infrastructure.Push;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contix.Infrastructure.Jobs;

public sealed class LembreteVencimentoOptions
{
    public const string Secao = "Notificacoes:LembreteVencimento";
    public bool Habilitado { get; set; } = true;
    public int IntervaloMinutos { get; set; } = 60;
}

/// <summary>Executa uma ação em intervalos fixos, isolando falhas para o job não parar.</summary>
public abstract class JobPeriodico(ILogger logger) : BackgroundService
{
    protected abstract bool Habilitado { get; }
    protected abstract TimeSpan Intervalo { get; }
    protected abstract Task ExecutarUmaVezAsync(CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Habilitado) return;
        // Dá tempo da aplicação aplicar migrations antes da primeira execução.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);

        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            try { await ExecutarUmaVezAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Falha no job {Job}", GetType().Name); }
        } while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken).AsTask()
                     .ContinueWith(t => t.IsCompletedSuccessfully && t.Result, TaskScheduler.Default));
    }
}

public sealed class EmailOutboxJob(EmailOutboxProcessor processor, IOptions<EmailOptions> options, ILogger<EmailOutboxJob> logger)
    : JobPeriodico(logger)
{
    protected override bool Habilitado => options.Value.ProcessarFila;
    protected override TimeSpan Intervalo => TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervaloFilaSegundos));
    protected override Task ExecutarUmaVezAsync(CancellationToken ct) => processor.ProcessarAsync(ct);
}

public sealed class PushOutboxJob(PushOutboxProcessor processor, IOptions<PushOptions> options, ILogger<PushOutboxJob> logger)
    : JobPeriodico(logger)
{
    protected override bool Habilitado => options.Value.ProcessarFila;
    protected override TimeSpan Intervalo => TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervaloFilaSegundos));
    protected override Task ExecutarUmaVezAsync(CancellationToken ct) => processor.ProcessarAsync(ct);
}

public sealed class LembreteVencimentoJob(
    IServiceScopeFactory scopeFactory, IOptions<LembreteVencimentoOptions> options, ILogger<LembreteVencimentoJob> logger)
    : JobPeriodico(logger)
{
    protected override bool Habilitado => options.Value.Habilitado;
    protected override TimeSpan Intervalo => TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervaloMinutos));

    /// <summary>Lembretes de documentos, avisos de notícias publicadas (inclusive agendadas) e de CND a vencer.</summary>
    protected override async Task ExecutarUmaVezAsync(CancellationToken ct)
    {
        await ExecutarAsync<ILembreteVencimentoService>(s => s.ProcessarAsync(ct));
        await ExecutarAsync<IAvisoNoticiaService>(s => s.ProcessarAsync(ct));
        await ExecutarAsync<IAvisoCertidaoService>(s => s.ProcessarAsync(ct));
    }

    // Cada aviso em seu escopo: a falha de um não impede os outros.
    private async Task ExecutarAsync<T>(Func<T, Task<int>> acao) where T : notnull
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await acao(scope.ServiceProvider.GetRequiredService<T>());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha ao processar {Servico}", typeof(T).Name);
        }
    }
}

/// <summary>Emissão automática de CND: agenda as renovações devidas e processa a fila de emissões.</summary>
public sealed class EmissaoCertidaoJob(
    IServiceScopeFactory scopeFactory, IOptions<EmissaoCertidaoOptions> options, ILogger<EmissaoCertidaoJob> logger)
    : JobPeriodico(logger)
{
    protected override bool Habilitado => options.Value.ProcessarFila
        && !string.Equals(options.Value.Provider, "Desabilitado", StringComparison.OrdinalIgnoreCase);
    protected override TimeSpan Intervalo => TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervaloMinutos));

    protected override async Task ExecutarUmaVezAsync(CancellationToken ct)
    {
        // Escopos separados: uma falha no agendamento não trava a fila (e vice-versa).
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            try { await scope.ServiceProvider.GetRequiredService<IProcessadorEmissaoCertidao>().AgendarAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Falha ao agendar emissões de CND"); }
        }
        await using (var scope = scopeFactory.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IProcessadorEmissaoCertidao>().ProcessarFilaAsync(ct);
    }
}

/// <summary>Busca automática de NF-e na SEFAZ (Distribuição DF-e) das empresas com a busca ligada.</summary>
public sealed class SincronizacaoNFeJob(
    IServiceScopeFactory scopeFactory, IOptions<SefazOptions> options, ILogger<SincronizacaoNFeJob> logger)
    : JobPeriodico(logger)
{
    protected override bool Habilitado => options.Value.ProcessarFila
        && !string.Equals(options.Value.Provider, "Desabilitado", StringComparison.OrdinalIgnoreCase);
    protected override TimeSpan Intervalo => TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervaloMinutos));

    protected override async Task ExecutarUmaVezAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> prontas;
        await using (var scope = scopeFactory.CreateAsyncScope())
            prontas = await scope.ServiceProvider.GetRequiredService<IProcessadorSincronizacaoNFe>().ListarProntasAsync(ct);

        // Um escopo por empresa: a falha de uma não afeta as outras.
        foreach (var id in prontas)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IProcessadorSincronizacaoNFe>().ProcessarAsync(id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Falha na busca de NF-e da sincronização {SincronizacaoId}", id);
            }
        }
    }
}

/// <summary>Gera as tarefas das obrigações recorrentes cujo vencimento se aproxima (CTX-67). Idempotente.</summary>
public sealed class ObrigacoesRecorrentesJob(
    IServiceScopeFactory scopeFactory, IOptions<Contix.Application.Features.Obrigacoes.ObrigacoesOptions> options,
    ILogger<ObrigacoesRecorrentesJob> logger) : JobPeriodico(logger)
{
    protected override bool Habilitado => options.Value.Habilitado;
    protected override TimeSpan Intervalo => TimeSpan.FromMinutes(Math.Max(5, options.Value.IntervaloMinutos));

    protected override async Task ExecutarUmaVezAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> ids;
        await using (var scope = scopeFactory.CreateAsyncScope())
            ids = await scope.ServiceProvider.GetRequiredService<Contix.Application.Features.Obrigacoes.IObrigacaoRepository>()
                .ListarAtivasIgnorandoTenantAsync(ct);

        var total = 0;
        foreach (var id in ids)
        {
            // Cada obrigação em seu escopo: a falha de uma (ex.: conflito com outra instância) não impede as demais.
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                total += await scope.ServiceProvider.GetRequiredService<Contix.Application.Features.Obrigacoes.IGeradorTarefasObrigacao>()
                    .ProcessarAsync(id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Falha ao gerar as tarefas da obrigação {ObrigacaoId}", id);
            }
        }
        if (total > 0) logger.LogInformation("{Total} tarefas geradas pelas obrigações recorrentes", total);
    }
}
