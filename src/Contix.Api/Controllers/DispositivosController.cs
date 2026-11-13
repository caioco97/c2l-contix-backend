using Contix.Application.Features.Dispositivos;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Aparelhos do usuário autenticado que recebem push (app mobile).</summary>
[Route("api/v1/dispositivos")]
public sealed class DispositivosController(IDispositivoService service) : ApiControllerBase
{
    /// <summary>Registra (ou atualiza) o aparelho para receber push. Chamado pelo app após o login.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Registrar(RegistrarDispositivoRequest request, CancellationToken ct)
    {
        await service.RegistrarAsync(request, ct);
        return NoContent();
    }

    /// <summary>Remove o aparelho (logout no app): ele deixa de receber push desta conta.</summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remover([FromQuery] string token, CancellationToken ct)
    {
        await service.RemoverAsync(token, ct);
        return NoContent();
    }
}
