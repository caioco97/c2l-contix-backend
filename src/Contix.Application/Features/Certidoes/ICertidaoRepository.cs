using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;

namespace Contix.Application.Features.Certidoes;

public interface ICertidaoRepository : IRepository<Certidao>
{
    Task<IReadOnlyList<CertidaoDto>> ListarDaEmpresaAsync(Guid empresaId, CertidaoFiltro filtro, DateOnly hoje, CancellationToken ct = default);

    Task<CertidaoDto?> ObterDtoAsync(Guid empresaId, Guid certidaoId, DateOnly hoje, CancellationToken ct = default);

    /// <summary>Certidões atuais (de todas as empresas) que vencem até <paramref name="ate"/> ou estão positivas.</summary>
    Task<IReadOnlyList<CertidaoDto>> ListarAlertasAsync(DateOnly hoje, DateOnly ate, CancellationToken ct = default);
}
