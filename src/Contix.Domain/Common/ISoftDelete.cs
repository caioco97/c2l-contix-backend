namespace Contix.Domain.Common;

/// <summary>Entidade com exclusão lógica.</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}
