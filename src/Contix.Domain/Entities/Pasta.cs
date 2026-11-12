using Contix.Domain.Common;

namespace Contix.Domain.Entities;

/// <summary>Pasta de documentos de uma empresa. Suporta hierarquia (subpastas).</summary>
public class Pasta : TenantEntity
{
    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }
    public Guid? PastaPaiId { get; set; }
    public Pasta? PastaPai { get; set; }
    public string Nome { get; set; } = null!;
    public int Ordem { get; set; }
    /// <summary>Se o usuário do cliente pode enviar arquivos para esta pasta.</summary>
    public bool PermiteUploadCliente { get; set; }

    public ICollection<Pasta> Subpastas { get; set; } = new List<Pasta>();
    public ICollection<Documento> Documentos { get; set; } = new List<Documento>();
}
