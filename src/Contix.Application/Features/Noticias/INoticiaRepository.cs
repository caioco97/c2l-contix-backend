using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Models;
using Contix.Domain.Entities;

namespace Contix.Application.Features.Noticias;

public interface INoticiaRepository : IRepository<Noticia>
{
    /// <summary>Lista do escritório, com o alcance de cada notícia.</summary>
    Task<PagedResult<NoticiaResumoDto>> PesquisarAsync(NoticiaFiltro filtro, DateTime agora, CancellationToken ct = default);

    /// <summary>Carrega a notícia com empresas, anexos e autor (rastreada, para alteração).</summary>
    Task<Noticia?> ObterCompletaAsync(Guid noticiaId, CancellationToken ct = default);

    /// <summary>Notícias publicadas, não expiradas e destinadas à empresa.</summary>
    Task<PagedResult<NoticiaPortalDto>> PesquisarDaEmpresaAsync(Empresa empresa, NoticiaPortalFiltro filtro, Guid usuarioId, DateTime agora, CancellationToken ct = default);

    /// <summary>Notícia visível para a empresa agora (com anexos), ou null.</summary>
    Task<Noticia?> ObterVisivelAsync(Empresa empresa, Guid noticiaId, DateTime agora, CancellationToken ct = default);

    Task<DateTime?> ObterLeituraAsync(Guid noticiaId, Guid empresaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Registra a leitura (idempotente).</summary>
    Task MarcarComoLidaAsync(Guid noticiaId, Guid empresaId, Guid usuarioId, DateTime agora, CancellationToken ct = default);

    /// <summary>Empresas destinatárias com a situação de leitura pelos usuários do cliente.</summary>
    Task<IReadOnlyList<LeituraNoticiaDto>> ListarLeiturasAsync(Noticia noticia, CancellationToken ct = default);
}
