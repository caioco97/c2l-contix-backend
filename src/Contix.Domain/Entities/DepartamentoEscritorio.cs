using Contix.Domain.Common;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;

namespace Contix.Domain.Entities;

/// <summary>
/// Configuração de um departamento no escritório: nome exibido e se está em uso.
/// O código (<see cref="Enums.Departamento"/>) é estável e é o que tarefas, solicitações e responsáveis referenciam;
/// o escritório pode renomear e desativar, mas não excluir, para não quebrar o histórico.
/// </summary>
public class DepartamentoEscritorio : TenantEntity
{
    public Departamento Codigo { get; private set; }
    public string Nome { get; private set; } = null!;
    public bool Ativo { get; private set; } = true;

    public static string NomePadrao(Departamento codigo) => codigo switch
    {
        Departamento.Contabil => "Contábil",
        Departamento.Fiscal => "Fiscal",
        Departamento.Pessoal => "Pessoal",
        Departamento.Societario => "Societário",
        Departamento.Financeiro => "Financeiro",
        _ => codigo.ToString()
    };

    public static DepartamentoEscritorio Padrao(Guid tenantId, Departamento codigo) =>
        new() { TenantId = tenantId, Codigo = codigo, Nome = NomePadrao(codigo) };

    public void Atualizar(string nome, bool ativo)
    {
        if (string.IsNullOrWhiteSpace(nome)) throw new DomainException("Informe o nome do departamento.");
        Nome = nome.Trim();
        Ativo = ativo;
    }
}

/// <summary>
/// Colaborador responsável por uma empresa (cliente) em um departamento. Há no máximo um por empresa e departamento.
/// Solicitações novas do departamento são atribuídas a ele automaticamente, e as obrigações recorrentes geram tarefas para ele.
/// </summary>
public class ResponsavelEmpresa : TenantEntity
{
    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }
    public Departamento Departamento { get; set; }
    public Guid UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
}
