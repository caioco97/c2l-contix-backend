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
    /// <summary>Usuários do cliente vinculados à empresa, com todos os vínculos carregados.</summary>
    Task<IReadOnlyList<Usuario>> ListarClientesDaEmpresaAsync(Guid empresaId, CancellationToken ct = default);
    /// <summary>O usuário tem vínculo com ao menos uma empresa não excluída? Consulta fora do contexto de tenant (login).</summary>
    Task<bool> PossuiEmpresaAtivaIgnorandoTenantAsync(Guid usuarioId, CancellationToken ct = default);
    /// <summary>Usuários do cliente cuja única empresa não excluída é a informada.</summary>
    Task<IReadOnlyList<Guid>> ListarClientesExclusivosDaEmpresaAsync(Guid empresaId, CancellationToken ct = default);
    Task<PagedResult<Usuario>> PesquisarAsync(UsuarioFiltro filtro, CancellationToken ct = default);
}
