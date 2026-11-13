using Contix.Domain.Common;
using Contix.Domain.Enums;

namespace Contix.Domain.Entities;

/// <summary>Empresa cliente do escritório contábil.</summary>
public class Empresa : TenantEntity
{
    public static readonly string[] PastasPadrao =
    [
        "Administrativo", "Contabilidade", "Financeiro", "Fiscal",
        "Patrimônio", "Pessoal", "Registro de Empresas"
    ];

    public string RazaoSocial { get; set; } = null!;
    public string? NomeFantasia { get; set; }
    public string? Apelido { get; set; }
    /// <summary>CNPJ ou CPF, somente dígitos.</summary>
    public string Documento { get; set; } = null!;
    public string? InscricaoEstadual { get; set; }
    public string? InscricaoMunicipal { get; set; }
    public RegimeTributario RegimeTributario { get; set; }
    public SituacaoEmpresa Situacao { get; set; } = SituacaoEmpresa.Ativa;
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public Endereco? Endereco { get; set; }
    /// <summary>Usuário do escritório que excluiu a empresa (exclusão lógica).</summary>
    public Guid? ExcluidaPorId { get; set; }

    public ICollection<Pasta> Pastas { get; set; } = new List<Pasta>();
    public ICollection<UsuarioEmpresa> Usuarios { get; set; } = new List<UsuarioEmpresa>();

    /// <summary>Cria as pastas raiz padrão da empresa.</summary>
    public IReadOnlyList<Pasta> CriarPastasPadrao()
    {
        var pastas = PastasPadrao
            .Select((nome, i) => new Pasta { Nome = nome, EmpresaId = Id, TenantId = TenantId, Ordem = i })
            .ToList();
        foreach (var p in pastas) Pastas.Add(p);
        return pastas;
    }
}
