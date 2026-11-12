namespace Contix.Domain.Entities;

/// <summary>Vínculo entre um usuário do portal do cliente e uma empresa.</summary>
public class UsuarioEmpresa
{
    public Guid UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }
    public DateTime VinculadoEm { get; set; }
}
