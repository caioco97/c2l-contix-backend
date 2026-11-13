using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;

namespace Contix.Application.Features.Empregados;

public interface IEmpregadoRepository : IRepository<Empregado>
{
    Task<IReadOnlyList<Empregado>> ListarAsync(Guid empresaId, EmpregadoFiltro filtro, CancellationToken ct = default);
    Task<Empregado?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);
    /// <summary>Empregados da empresa com os ids informados (os de outras empresas são ignorados).</summary>
    Task<IReadOnlyList<Empregado>> ListarPorIdsAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<Empregado?> ObterPorCpfAsync(Guid empresaId, string cpf, CancellationToken ct = default);
}
