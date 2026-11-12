namespace Contix.Domain.Common;

/// <summary>Entidade pertencente a um tenant, com exclusão lógica.</summary>
public abstract class TenantEntity : BaseEntity, ITenantEntity, ISoftDelete
{
    public Guid TenantId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
