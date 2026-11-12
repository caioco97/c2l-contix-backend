using Contix.Application.Features.Notificacoes;
using Contix.Infrastructure.Email;
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

public sealed class LembreteVencimentoJob(
    IServiceScopeFactory scopeFactory, IOptions<LembreteVencimentoOptions> options, ILogger<LembreteVencimentoJob> logger)
    : JobPeriodico(logger)
{
    protected override bool Habilitado => options.Value.Habilitado;
    protected override TimeSpan Intervalo => TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervaloMinutos));

    protected override async Task ExecutarUmaVezAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ILembreteVencimentoService>().ProcessarAsync(ct);
    }
}
