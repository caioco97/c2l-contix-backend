using Contix.Domain.Common;

namespace Contix.Domain.Entities;

/// <summary>
/// Certificado digital A1 (e-CNPJ) da empresa, usado para falar com a SEFAZ. O arquivo .pfx e a senha
/// ficam criptografados; só os metadados são legíveis.
/// </summary>
public class CertificadoDigital : TenantEntity
{
    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }

    public string NomeArquivo { get; set; } = null!;
    /// <summary>Nome do titular (CN do certificado).</summary>
    public string Titular { get; set; } = null!;
    /// <summary>CNPJ do titular extraído do certificado (só dígitos), quando presente.</summary>
    public string? Documento { get; set; }
    public string Emissor { get; set; } = null!;
    public string NumeroSerie { get; set; } = null!;
    public string Thumbprint { get; set; } = null!;
    public DateTime ValidoDe { get; set; }
    public DateTime ValidoAte { get; set; }

    public byte[] ArquivoCriptografado { get; set; } = null!;
    public byte[] SenhaCriptografada { get; set; } = null!;

    public Guid? EnviadoPorId { get; set; }
    public Usuario? EnviadoPor { get; set; }

    public bool VencidoEm(DateTime agora) => agora >= ValidoAte;
}
