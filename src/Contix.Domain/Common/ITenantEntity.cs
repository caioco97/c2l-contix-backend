namespace Contix.Domain.Common;

/// <summary>Entidade isolada por escritório contábil (tenant).</summary>
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}
