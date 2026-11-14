using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace Contix.Application.Features.Painel;

/// <summary>Filtro do painel. Sem período, vale o mês atual. O departamento filtra solicitações e tarefas.</summary>
public sealed record PainelFiltro
{
    public DateOnly? De { get; init; }
    public DateOnly? Ate { get; init; }
    public Departamento? Departamento { get; init; }
}

public sealed record ContagemPorStatus(StatusSolicitacao Status, int Total);

/// <summary>Indicadores do painel do escritório (CTX-72).</summary>
/// <param name="SolicitacoesPorStatus">Solicitações abertas no período, por status atual.</param>
/// <param name="SolicitacoesEmAberto">Todas as solicitações ainda não finalizadas (independe do período).</param>
/// <param name="DocumentosNaoLidos">Documentos publicados pelo escritório no período que nenhum usuário do cliente abriu.</param>
/// <param name="TarefasAtrasadas">Tarefas em aberto com prazo interno vencido (situação atual).</param>
/// <param name="TarefasNoPeriodo">Tarefas com prazo interno no período.</param>
/// <param name="CndsAVencer">Certidões vigentes que vencem nos próximos 30 dias (a mais recente de cada tipo por empresa).</param>
public sealed record PainelDto(
    DateOnly De, DateOnly Ate, Departamento? Departamento,
    int ClientesAtivos,
    IReadOnlyList<ContagemPorStatus> SolicitacoesPorStatus, int SolicitacoesNoPeriodo, int SolicitacoesEmAberto, int SolicitacoesAtrasadas,
    int DocumentosPublicados, int DocumentosNaoLidos,
    int TarefasNoPeriodo, int TarefasConcluidasNoPeriodo, int TarefasEmAberto, int TarefasAtrasadas,
    int CndsAVencer, int CndsVencidas,
    DateTime AtualizadoEm);

public interface IPainelRepository
{
    Task<PainelDto> CalcularAsync(DateOnly de, DateOnly ate, Departamento? departamento, DateOnly hoje, DateTime agora, CancellationToken ct = default);
}

public interface IPainelService
{
    Task<PainelDto> ObterAsync(PainelFiltro filtro, CancellationToken ct = default);
}

/// <summary>
/// Os indicadores são contagens agregadas no banco (uma consulta por indicador, todas por índice).
/// O resultado fica 60 segundos em cache por escritório e filtro, para o painel aberto por vários colaboradores
/// não repetir as mesmas consultas.
/// </summary>
public sealed class PainelService(IPainelRepository repositorio, IMemoryCache cache, ICurrentUser currentUser, IDateTimeProvider clock) : IPainelService
{
    private static readonly TimeSpan Duracao = TimeSpan.FromSeconds(60);

    public async Task<PainelDto> ObterAsync(PainelFiltro filtro, CancellationToken ct = default)
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
        var agora = clock.UtcNow;
        var hoje = DateOnly.FromDateTime(agora);
        var de = filtro.De ?? new DateOnly(hoje.Year, hoje.Month, 1);
        var ate = filtro.Ate ?? new DateOnly(de.Year, de.Month, 1).AddMonths(1).AddDays(-1);
        if (ate < de) (de, ate) = (ate, de);

        var chave = $"painel:{currentUser.RequireTenantId():N}:{de:yyyyMMdd}:{ate:yyyyMMdd}:{filtro.Departamento}:{hoje:yyyyMMdd}";
        return (await cache.GetOrCreateAsync(chave, e =>
        {
            e.AbsoluteExpirationRelativeToNow = Duracao;
            e.Size = 1;
            return repositorio.CalcularAsync(de, ate, filtro.Departamento, hoje, agora, ct);
        }))!;
    }
}
