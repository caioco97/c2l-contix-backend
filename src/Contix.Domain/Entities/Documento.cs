using Contix.Domain.Common;

namespace Contix.Domain.Entities;

/// <summary>Documento publicado em uma pasta da empresa.</summary>
public class Documento : TenantEntity
{
    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }
    public Guid PastaId { get; set; }
    public Pasta? Pasta { get; set; }

    public string Nome { get; set; } = null!;
    public string NomeArquivoOriginal { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long TamanhoBytes { get; set; }
    /// <summary>Chave do arquivo no storage (local, S3, Blob...).</summary>
    public string StorageKey { get; set; } = null!;

    public string? Comentario { get; set; }
    /// <summary>Competência no formato do primeiro dia do mês.</summary>
    public DateOnly? Competencia { get; set; }
    public DateOnly? DataVencimento { get; set; }
    public DateTime PublicadoEm { get; set; }

    public Guid AdicionadoPorId { get; set; }
    public Usuario? AdicionadoPor { get; set; }

    public ICollection<DocumentoLeitura> Leituras { get; set; } = new List<DocumentoLeitura>();
}
