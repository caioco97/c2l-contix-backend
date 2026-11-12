using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Models;
using Contix.Domain.Entities;

namespace Contix.Application.Features.Solicitacoes;

public interface ISolicitacaoRepository : IRepository<Solicitacao>
{
    /// <param name="somenteEmpresaId">Restringe a uma empresa (rota da empresa / portal do cliente).</param>
    Task<PagedResult<SolicitacaoResumoDto>> PesquisarAsync(SolicitacaoFiltro filtro, Guid? somenteEmpresaId, DateOnly hoje, CancellationToken ct = default);

    /// <summary>Carrega a solicitação com histórico e anexos (rastreada, para alteração).</summary>
    Task<Solicitacao?> ObterCompletaAsync(Guid empresaId, Guid solicitacaoId, CancellationToken ct = default);

    Task<SolicitacaoAnexo?> ObterAnexoAsync(Guid empresaId, Guid solicitacaoId, Guid anexoId, CancellationToken ct = default);

    /// <summary>Reserva o próximo número da sequência do escritório (atômico).</summary>
    Task<int> ProximoNumeroAsync(Guid tenantId, CancellationToken ct = default);
}
