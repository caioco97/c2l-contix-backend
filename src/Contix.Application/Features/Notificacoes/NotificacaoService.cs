using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Common.Models;
using FluentValidation;

namespace Contix.Application.Features.Notificacoes;

/// <summary>Central de notificações do usuário autenticado.</summary>
public interface INotificacaoService
{
    Task<PagedResult<NotificacaoDto>> PesquisarAsync(NotificacaoFiltro filtro, CancellationToken ct = default);
    Task<ContadorNotificacoesDto> ContarAsync(CancellationToken ct = default);
    Task MarcarComoLidaAsync(Guid notificacaoId, CancellationToken ct = default);
    Task MarcarTodasComoLidasAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PreferenciaNotificacaoDto>> ListarPreferenciasAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PreferenciaNotificacaoDto>> AtualizarPreferenciasAsync(AtualizarPreferenciasRequest request, CancellationToken ct = default);
}

public sealed class NotificacaoService(
    INotificacaoRepository repositorio,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IValidator<AtualizarPreferenciasRequest> preferenciasValidator) : INotificacaoService
{
    public Task<PagedResult<NotificacaoDto>> PesquisarAsync(NotificacaoFiltro filtro, CancellationToken ct = default) =>
        repositorio.PesquisarAsync(currentUser.RequireUserId(), filtro, ct);

    public async Task<ContadorNotificacoesDto> ContarAsync(CancellationToken ct = default) =>
        new(await repositorio.ContarNaoLidasAsync(currentUser.RequireUserId(), ct));

    public async Task MarcarComoLidaAsync(Guid notificacaoId, CancellationToken ct = default)
    {
        var n = await repositorio.GetByIdAsync(notificacaoId, ct);
        if (n is null || n.UsuarioId != currentUser.RequireUserId()) throw new NotFoundException("Notificação", notificacaoId);
        n.MarcarComoLida(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task MarcarTodasComoLidasAsync(CancellationToken ct = default) =>
        await repositorio.MarcarTodasComoLidasAsync(currentUser.RequireUserId(), clock.UtcNow, ct);

    public async Task<IReadOnlyList<PreferenciaNotificacaoDto>> ListarPreferenciasAsync(CancellationToken ct = default)
    {
        var usuarioId = currentUser.RequireUserId();
        var salvas = (await repositorio.ListarPreferenciasAsync([usuarioId], ct)).ToDictionary(p => p.Tipo, p => p.ReceberEmail);
        return TiposNotificacao.Descricoes
            .Select(kv => new PreferenciaNotificacaoDto(kv.Key, kv.Value, salvas.GetValueOrDefault(kv.Key, true)))
            .ToList();
    }

    public async Task<IReadOnlyList<PreferenciaNotificacaoDto>> AtualizarPreferenciasAsync(AtualizarPreferenciasRequest request, CancellationToken ct = default)
    {
        await preferenciasValidator.ValidateAndThrowAsync(request, ct);
        var itens = request.Preferencias.GroupBy(p => p.Tipo).Select(g => g.Last()).ToList();
        await repositorio.SalvarPreferenciasAsync(currentUser.RequireUserId(), itens, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return await ListarPreferenciasAsync(ct);
    }
}
