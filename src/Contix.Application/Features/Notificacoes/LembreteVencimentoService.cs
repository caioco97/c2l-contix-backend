using Contix.Application.Common.Abstractions;
using Contix.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.Notificacoes;

/// <summary>
/// Lembra os usuários do cliente dos documentos que vencem nos próximos dias
/// (antecedência configurada por escritório). Executado periodicamente por um job.
/// </summary>
public interface ILembreteVencimentoService
{
    /// <returns>Quantidade de documentos lembrados.</returns>
    Task<int> ProcessarAsync(CancellationToken ct = default);
}

public sealed class LembreteVencimentoService(
    INotificacaoRepository repositorio,
    INotificador notificador,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<LembreteVencimentoService> logger) : ILembreteVencimentoService
{
    private const int Lote = 200;

    public async Task<int> ProcessarAsync(CancellationToken ct = default)
    {
        var agora = clock.UtcNow;
        var hoje = DateOnly.FromDateTime(agora);
        var total = 0;

        while (!ct.IsCancellationRequested)
        {
            var documentos = await repositorio.ListarDocumentosParaLembreteAsync(hoje, Lote, ct);
            if (documentos.Count == 0) break;

            foreach (var porPasta in documentos.GroupBy(d => (d.TenantId, d.EmpresaId, d.PastaId)))
            {
                // Só quem tem o módulo de documentos e enxerga a pasta.
                var clientes = await repositorio.ListarClientesComAcessoAPastaAsync(
                    porPasta.Key.TenantId, porPasta.Key.EmpresaId, porPasta.Key.PastaId, ct);
                foreach (var doc in porPasta)
                {
                    var dias = doc.DataVencimento.DayNumber - hoje.DayNumber;
                    var quando = dias switch { 0 => "vence hoje", 1 => "vence amanhã", _ => $"vence em {dias} dias" };
                    await notificador.NotificarAsync(doc.TenantId, clientes, new EventoNotificacao(
                        TipoNotificacao.VencimentoProximo,
                        $"Vencimento próximo: {doc.Nome}",
                        $"O documento \"{doc.Nome}\" da empresa {doc.EmpresaNome} {quando} ({doc.DataVencimento:dd/MM/yyyy}).",
                        doc.EmpresaId, doc.Id,
                        _ => $"/documentos/{doc.PastaId}",
                        "Ver documento"), ct);
                }
            }

            await repositorio.MarcarLembreteEnviadoAsync(documentos.Select(d => d.Id).ToList(), agora, ct);
            await unitOfWork.SaveChangesAsync(ct);
            total += documentos.Count;
            if (documentos.Count < Lote) break;
        }

        if (total > 0) logger.LogInformation("Lembretes de vencimento gerados para {Quantidade} documento(s)", total);
        return total;
    }
}
