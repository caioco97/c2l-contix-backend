using Contix.Domain.Entities;
using Contix.Domain.Enums;

namespace Contix.Application.Features.Equipe;

public interface IEquipeRepository
{
    /// <summary>Departamentos personalizados (renomeados ou desativados) do escritório atual, rastreados.</summary>
    Task<List<DepartamentoEscritorio>> ListarDepartamentosAsync(CancellationToken ct = default);
    Task AdicionarDepartamentoAsync(DepartamentoEscritorio departamento, CancellationToken ct = default);
    /// <summary>Quantos usuários ativos do escritório atuam em cada departamento.</summary>
    Task<IReadOnlyDictionary<Departamento, int>> ContarColaboradoresAsync(CancellationToken ct = default);

    /// <summary>Responsáveis da empresa (rastreados), com o usuário carregado.</summary>
    Task<List<ResponsavelEmpresa>> ListarResponsaveisAsync(Guid empresaId, CancellationToken ct = default);
    Task AdicionarResponsavelAsync(ResponsavelEmpresa responsavel, CancellationToken ct = default);
    void RemoverResponsavel(ResponsavelEmpresa responsavel);

    /// <summary>Responsável ativo da empresa no departamento (id e nome), se houver.</summary>
    Task<(Guid Id, string Nome)?> ObterResponsavelAtivoAsync(Guid empresaId, Departamento departamento, CancellationToken ct = default);

    /// <summary>Usuários ativos do escritório entre os informados.</summary>
    Task<IReadOnlyDictionary<Guid, string>> ListarColaboradoresAtivosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
}
