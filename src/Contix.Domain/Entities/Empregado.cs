using Contix.Domain.Common;
using Contix.Domain.Enums;

namespace Contix.Domain.Entities;

/// <summary>
/// Empregado (ou contribuinte/estagiário) de uma empresa cliente. As solicitações do departamento
/// pessoal (férias, rescisão, afastamento, rubricas) referenciam o empregado por este cadastro.
/// </summary>
public class Empregado : TenantEntity
{
    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }

    public string Nome { get; set; } = null!;
    /// <summary>CPF, somente dígitos. Opcional para cadastros importados do texto livre antigo.</summary>
    public string? Cpf { get; set; }
    public string? Matricula { get; set; }
    public string? Cargo { get; set; }
    public VinculoEmpregado Vinculo { get; set; } = VinculoEmpregado.Empregado;
    public SituacaoEmpregado Situacao { get; set; } = SituacaoEmpregado.Ativo;
    public DateOnly? DataAdmissao { get; set; }
    public DateOnly? DataDesligamento { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }

    /// <summary>Solicitação de cadastro que originou o registro, quando houver.</summary>
    public Guid? SolicitacaoOrigemId { get; set; }

    public bool EstaDesligado => Situacao == SituacaoEmpregado.Desligado;

    public void Desligar(DateOnly data)
    {
        Situacao = SituacaoEmpregado.Desligado;
        DataDesligamento = data;
    }
}
