using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Models;
using Contix.Domain.Entities;

namespace Contix.Application.Features.Empresas;

public interface IEmpresaRepository : IRepository<Empresa>
{
    Task<bool> DocumentoExisteAsync(string documento, Guid? exceto, CancellationToken ct = default);
    Task<int> ContarExistentesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<bool> UsuarioVinculadoAsync(Guid usuarioId, Guid empresaId, CancellationToken ct = default);
    Task<UsuarioEmpresa?> ObterVinculoAsync(Guid usuarioId, Guid empresaId, CancellationToken ct = default);

    /// <remarks>Quando <c>somenteDoUsuario</c> é informado, restringe às empresas vinculadas ao usuário (portal do cliente).</remarks>
    Task<PagedResult<EmpresaResumoDto>> PesquisarAsync(EmpresaFiltro filtro, Guid? somenteDoUsuario, CancellationToken ct = default);
}
