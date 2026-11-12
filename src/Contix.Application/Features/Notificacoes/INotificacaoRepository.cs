using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Models;
using Contix.Domain.Entities;
using Contix.Domain.Enums;

namespace Contix.Application.Features.Notificacoes;

/// <summary>Destinatário de uma notificação.</summary>
public sealed record Destinatario(Guid Id, string Nome, string Email, bool EhDoEscritorio);

/// <summary>Documento com vencimento que precisa de lembrete (consulta entre tenants, feita pelo job).</summary>
public sealed record DocumentoParaLembrete(Guid Id, Guid TenantId, Guid EmpresaId, string EmpresaNome, Guid PastaId,
    string Nome, DateOnly DataVencimento);

public interface INotificacaoRepository : IRepository<Notificacao>
{
    Task<PagedResult<NotificacaoDto>> PesquisarAsync(Guid usuarioId, NotificacaoFiltro filtro, CancellationToken ct = default);
    Task<int> ContarNaoLidasAsync(Guid usuarioId, CancellationToken ct = default);
    Task<int> MarcarTodasComoLidasAsync(Guid usuarioId, DateTime agora, CancellationToken ct = default);

    /* Destinatários. Recebem o tenant explicitamente para funcionar também fora de uma requisição (jobs). */
    Task<IReadOnlyList<Destinatario>> ListarClientesDaEmpresaAsync(Guid tenantId, Guid empresaId, CancellationToken ct = default);
    Task<IReadOnlyList<Destinatario>> ListarEquipeDoEscritorioAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<Destinatario>> ListarPorIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> usuarioIds, CancellationToken ct = default);

    Task<IReadOnlyList<PreferenciaNotificacao>> ListarPreferenciasAsync(IReadOnlyCollection<Guid> usuarioIds, CancellationToken ct = default);
    Task SalvarPreferenciasAsync(Guid usuarioId, IReadOnlyList<PreferenciaItem> preferencias, CancellationToken ct = default);

    /// <summary>Documentos de todos os escritórios cujo vencimento cai dentro da antecedência configurada e ainda sem lembrete.</summary>
    Task<IReadOnlyList<DocumentoParaLembrete>> ListarDocumentosParaLembreteAsync(DateOnly hoje, int limite, CancellationToken ct = default);
    Task MarcarLembreteEnviadoAsync(IReadOnlyCollection<Guid> documentoIds, DateTime agora, CancellationToken ct = default);
    Task<Escritorio?> ObterEscritorioAsync(Guid tenantId, CancellationToken ct = default);
}
