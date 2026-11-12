using Contix.Domain.Common;

namespace Contix.Application.Common.Abstractions;

/// <summary>Repositório genérico. Consultas respeitam o filtro de tenant e soft delete.</summary>
public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);
    void Remove(T entity);
}
