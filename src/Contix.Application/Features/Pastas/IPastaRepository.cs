using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;

namespace Contix.Application.Features.Pastas;

public interface IPastaRepository : IRepository<Pasta>
{
    Task<IReadOnlyList<Pasta>> ListarPorEmpresaAsync(Guid empresaId, CancellationToken ct = default);
    Task<bool> PossuiConteudoAsync(Guid pastaId, CancellationToken ct = default);
    Task<bool> NomeExisteAsync(Guid empresaId, Guid? pastaPaiId, string nome, Guid? exceto, CancellationToken ct = default);
}
