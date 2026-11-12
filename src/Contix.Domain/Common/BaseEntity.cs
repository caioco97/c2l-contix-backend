namespace Contix.Domain.Common;

/// <summary>Entidade base com identidade e auditoria temporal.</summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
