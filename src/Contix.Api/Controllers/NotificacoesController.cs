using Contix.Application.Common.Models;
using Contix.Application.Features.Notificacoes;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Central de notificações do usuário autenticado (sino) e preferências de e-mail.</summary>
[Route("api/v1/notificacoes")]
public sealed class NotificacoesController(INotificacaoService service) : ApiControllerBase
{
    /// <summary>Lista as notificações, mais recentes primeiro.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<NotificacaoDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<NotificacaoDto>> Pesquisar([FromQuery] NotificacaoFiltro filtro, CancellationToken ct) =>
        service.PesquisarAsync(filtro, ct);

    /// <summary>Quantidade de notificações não lidas (contador do sino).</summary>
    [HttpGet("contador")]
    [ProducesResponseType(typeof(ContadorNotificacoesDto), StatusCodes.Status200OK)]
    public Task<ContadorNotificacoesDto> Contador(CancellationToken ct) => service.ContarAsync(ct);

    [HttpPost("{notificacaoId:guid}/lida")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarcarComoLida(Guid notificacaoId, CancellationToken ct)
    {
        await service.MarcarComoLidaAsync(notificacaoId, ct);
        return NoContent();
    }

    [HttpPost("lidas")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarcarTodasComoLidas(CancellationToken ct)
    {
        await service.MarcarTodasComoLidasAsync(ct);
        return NoContent();
    }

    /// <summary>Preferências de e-mail por tipo de notificação (in-app é sempre gerada).</summary>
    [HttpGet("preferencias")]
    [ProducesResponseType(typeof(IReadOnlyList<PreferenciaNotificacaoDto>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<PreferenciaNotificacaoDto>> Preferencias(CancellationToken ct) => service.ListarPreferenciasAsync(ct);

    [HttpPut("preferencias")]
    [ProducesResponseType(typeof(IReadOnlyList<PreferenciaNotificacaoDto>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<PreferenciaNotificacaoDto>> AtualizarPreferencias(AtualizarPreferenciasRequest request, CancellationToken ct) =>
        service.AtualizarPreferenciasAsync(request, ct);
}
