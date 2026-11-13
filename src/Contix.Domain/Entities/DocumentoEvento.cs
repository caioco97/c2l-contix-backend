using Contix.Domain.Common;
using Contix.Domain.Enums;

namespace Contix.Domain.Entities;

/// <summary>
/// Registro de auditoria de um documento: upload, visualização, download, envio e exclusão,
/// com o usuário e a data/hora. Nunca é alterado nem excluído.
/// </summary>
public class DocumentoEvento : BaseEntity
{
    public Guid DocumentoId { get; set; }
    public Documento? Documento { get; set; }
    public Guid UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public TipoEventoDocumento Tipo { get; set; }
    public DateTime OcorridoEm { get; set; }
    /// <summary>Informação complementar (ex.: destinatários do envio, download em lote).</summary>
    public string? Detalhe { get; set; }

    public static DocumentoEvento Criar(Guid documentoId, TipoEventoDocumento tipo, Guid usuarioId, DateTime quando, string? detalhe = null) =>
        new()
        {
            DocumentoId = documentoId,
            Tipo = tipo,
            UsuarioId = usuarioId,
            OcorridoEm = quando,
            Detalhe = detalhe is { Length: > 500 } ? detalhe[..497] + "..." : detalhe
        };
}
