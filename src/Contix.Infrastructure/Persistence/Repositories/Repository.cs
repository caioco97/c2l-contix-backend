using Contix.Application.Common.Abstractions;
using Contix.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence.Repositories;

public class Repository<T>(ContixDbContext context) : IRepository<T> where T : BaseEntity
{
    protected ContixDbContext Context { get; } = context;
    protected DbSet<T> Set => Context.Set<T>();

    public virtual Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task AddAsync(T entity, CancellationToken ct = default) => await Set.AddAsync(entity, ct);

    public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default) => Set.AddRangeAsync(entities, ct);

    public void Remove(T entity) => Set.Remove(entity);
}
