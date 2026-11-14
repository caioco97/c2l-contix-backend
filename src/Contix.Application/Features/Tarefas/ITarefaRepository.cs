using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Models;
using Contix.Domain.Entities;

namespace Contix.Application.Features.Tarefas;

public interface ITarefaRepository : IRepository<Tarefa>
{
    /// <param name="somenteEmpresaId">Restringe a uma empresa (aba Tarefas da empresa).</param>
    Task<PagedResult<TarefaResumoDto>> PesquisarAsync(TarefaFiltro filtro, Guid? somenteEmpresaId, DateOnly hoje, CancellationToken ct = default);

    /// <summary>Carrega a tarefa com histórico e anexos (rastreada, para alteração).</summary>
    Task<Tarefa?> ObterCompletaAsync(Guid empresaId, Guid tarefaId, CancellationToken ct = default);

    Task<TarefaAnexo?> ObterAnexoAsync(Guid empresaId, Guid tarefaId, Guid anexoId, CancellationToken ct = default);

    /// <summary>Reserva o próximo número da sequência de tarefas do escritório (atômico).</summary>
    Task<int> ProximoNumeroAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Reserva <paramref name="quantidade"/> números seguidos e devolve o primeiro (atômico).</summary>
    Task<int> ReservarNumerosAsync(Guid tenantId, int quantidade, CancellationToken ct = default);

    /// <summary>Tarefas vinculadas à solicitação do cliente.</summary>
    Task<IReadOnlyList<TarefaResumoDto>> ListarDaSolicitacaoAsync(Guid empresaId, Guid solicitacaoId, DateOnly hoje, CancellationToken ct = default);
}
