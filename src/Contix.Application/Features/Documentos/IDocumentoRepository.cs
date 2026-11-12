using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Models;
using Contix.Domain.Entities;

namespace Contix.Application.Features.Documentos;

public interface IDocumentoRepository : IRepository<Documento>
{
    /// <summary>Lista documentos com status de leitura calculado para o usuário informado.</summary>
    Task<PagedResult<DocumentoDto>> PesquisarAsync(Guid empresaId, DocumentoFiltro filtro, Guid usuarioId, CancellationToken ct = default);
    Task<DocumentoDto?> ObterDtoAsync(Guid empresaId, Guid documentoId, Guid usuarioId, CancellationToken ct = default);
    Task<IReadOnlyList<Documento>> ListarPorIdsAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyList<VencimentoDto>> ListarVencimentosAsync(Guid empresaId, DateOnly inicio, DateOnly fim, Guid usuarioId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, int>> ContarNaoLidosPorPastaAsync(Guid empresaId, Guid usuarioId, CancellationToken ct = default);
    /// <summary>Registra a leitura dos documentos ainda não lidos pelo usuário.</summary>
    Task MarcarComoLidosAsync(IReadOnlyCollection<Guid> documentoIds, Guid usuarioId, DateTime agora, CancellationToken ct = default);
}
