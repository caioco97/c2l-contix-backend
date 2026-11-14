using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;
using Contix.Domain.Enums;

namespace Contix.Application.Features.Obrigacoes;

/// <summary>
/// Obrigações recorrentes. Os métodos que recebem <c>tenantId</c> consultam fora do filtro de tenant,
/// pois também são usados pelo job de geração (sem usuário autenticado).
/// </summary>
public interface IObrigacaoRepository : IRepository<ObrigacaoRecorrente>
{
    /// <summary>Modelos do escritório atual, com o responsável padrão e a quantidade de tarefas geradas.</summary>
    Task<IReadOnlyList<(ObrigacaoRecorrente Obrigacao, int Tarefas)>> ListarAsync(CancellationToken ct = default);
    Task<ObrigacaoRecorrente?> ObterComResponsavelAsync(Guid id, CancellationToken ct = default);
    Task<int> ContarTarefasAsync(Guid obrigacaoId, CancellationToken ct = default);

    /// <summary>Ids das obrigações ativas de todos os escritórios (job).</summary>
    Task<IReadOnlyList<Guid>> ListarAtivasIgnorandoTenantAsync(CancellationToken ct = default);
    Task<ObrigacaoRecorrente?> ObterIgnorandoTenantAsync(Guid id, CancellationToken ct = default);

    /// <summary>Empresas ativas do escritório nos regimes informados (vazio: todos).</summary>
    Task<IReadOnlyList<EmpresaParaObrigacao>> ListarEmpresasAplicaveisAsync(Guid tenantId, IReadOnlyCollection<RegimeTributario> regimes, CancellationToken ct = default);
    /// <summary>Empresas que já têm tarefa da obrigação na competência.</summary>
    Task<IReadOnlySet<Guid>> EmpresasComTarefaAsync(Guid tenantId, Guid obrigacaoId, DateOnly competencia, CancellationToken ct = default);
    /// <summary>Responsável ativo de cada empresa no departamento.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> ResponsaveisDoDepartamentoAsync(Guid tenantId, Departamento departamento, CancellationToken ct = default);
    Task<bool> UsuarioAtivoDoEscritorioAsync(Guid tenantId, Guid usuarioId, CancellationToken ct = default);
}
