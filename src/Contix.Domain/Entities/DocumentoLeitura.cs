namespace Contix.Domain.Entities;

/// <summary>Registro de leitura (visualização/download) de um documento por usuário.</summary>
public class DocumentoLeitura
{
    public Guid DocumentoId { get; set; }
    public Documento? Documento { get; set; }
    public Guid UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public DateTime LidoEm { get; set; }
}
