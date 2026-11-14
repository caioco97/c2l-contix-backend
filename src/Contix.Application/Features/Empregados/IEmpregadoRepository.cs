using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;

namespace Contix.Application.Features.Empregados;

public interface IEmpregadoRepository : IRepository<Empregado>
{
    Task<IReadOnlyList<Empregado>> ListarAsync(Guid empresaId, EmpregadoFiltro filtro, CancellationToken ct = default);
    Task<Empregado?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);
    /// <summary>Empregados da empresa com os ids informados (os de outras empresas são ignorados).</summary>
    Task<IReadOnlyList<Empregado>> ListarPorIdsAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    /// <summary>Todos os empregados não desligados, por nome (sem o teto da listagem; para a planilha modelo).</summary>
    Task<IReadOnlyList<Empregado>> ListarAtivosAsync(Guid empresaId, CancellationToken ct = default);
    /// <summary>Empregados da empresa com algum dos CPFs (só dígitos) ou matrículas (sem diferenciar maiúsculas).</summary>
    Task<IReadOnlyList<Empregado>> ListarPorCpfsOuMatriculasAsync(Guid empresaId, IReadOnlyCollection<string> cpfs, IReadOnlyCollection<string> matriculas, CancellationToken ct = default);
    Task<Empregado?> ObterPorCpfAsync(Guid empresaId, string cpf, CancellationToken ct = default);
}
