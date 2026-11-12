using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;

namespace Contix.Application.Features.Auth;

public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<RefreshToken>> ListarAtivosDaFamiliaAsync(Guid familia, DateTime agora, CancellationToken ct = default);
    Task<IReadOnlyList<RefreshToken>> ListarAtivosDoUsuarioAsync(Guid usuarioId, DateTime agora, CancellationToken ct = default);
}
