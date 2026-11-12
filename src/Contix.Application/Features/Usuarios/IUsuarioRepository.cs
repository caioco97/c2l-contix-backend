using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Models;
using Contix.Domain.Entities;

namespace Contix.Application.Features.Usuarios;

public interface IUsuarioRepository : IRepository<Usuario>
{
    /// <summary>Busca para login, fora do contexto de tenant.</summary>
    Task<Usuario?> GetByEmailIgnorandoTenantAsync(string email, CancellationToken ct = default);
    Task<Usuario?> GetByIdIgnorandoTenantAsync(Guid id, CancellationToken ct = default);
    Task<bool> EmailExisteIgnorandoTenantAsync(string email, Guid? exceto, CancellationToken ct = default);
    Task<Usuario?> GetComEmpresasAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Usuario>> PesquisarAsync(UsuarioFiltro filtro, CancellationToken ct = default);
}
