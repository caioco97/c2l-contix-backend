using Contix.Application.Common.Abstractions;
using Contix.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Contix.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Preenche datas de auditoria, atribui o TenantId em novas entidades
/// e converte exclusões em soft delete.
/// </summary>
public sealed class AuditInterceptor(ICurrentUser currentUser, IDateTimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Aplicar(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Aplicar(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Aplicar(DbContext? context)
    {
        if (context is null) return;
        var agora = clock.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is ITenantEntity tenant && entry.State == EntityState.Added && tenant.TenantId == Guid.Empty)
            {
                tenant.TenantId = currentUser.TenantId
                    ?? throw new InvalidOperationException(
                        $"Não foi possível determinar o tenant para a entidade {entry.Entity.GetType().Name}.");
            }

            if (entry.Entity is ISoftDelete soft && entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                soft.IsDeleted = true;
                soft.DeletedAt = agora;
            }

            if (entry.Entity is BaseEntity baseEntity)
            {
                if (entry.State == EntityState.Added && baseEntity.CreatedAt == default) baseEntity.CreatedAt = agora;
                else if (entry.State == EntityState.Modified) baseEntity.UpdatedAt = agora;
            }
        }
    }
}
