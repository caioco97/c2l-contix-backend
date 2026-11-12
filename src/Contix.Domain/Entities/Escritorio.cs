using Contix.Domain.Common;

namespace Contix.Domain.Entities;

/// <summary>Escritório contábil. É o tenant da plataforma.</summary>
public class Escritorio : BaseEntity
{
    public string RazaoSocial { get; set; } = null!;
    public string? NomeFantasia { get; set; }
    public string Cnpj { get; set; } = null!;
    public string? Crc { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public Endereco? Endereco { get; set; }
    public string? LogoUrl { get; set; }
    public bool Ativo { get; set; } = true;
}
