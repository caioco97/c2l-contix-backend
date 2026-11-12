using Contix.Api.Infrastructure;
using Contix.Application.Features.Escritorios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Dados do escritório contábil do usuário autenticado.</summary>
[Route("api/v1/escritorio")]
public sealed class EscritorioController(IEscritorioService service) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(EscritorioDto), StatusCodes.Status200OK)]
    public Task<EscritorioDto> Obter(CancellationToken ct) => service.ObterAtualAsync(ct);

    [HttpPut]
    [Authorize(Policy = Policies.Administrador)]
    [ProducesResponseType(typeof(EscritorioDto), StatusCodes.Status200OK)]
    public Task<EscritorioDto> Atualizar(AtualizarEscritorioRequest request, CancellationToken ct) =>
        service.AtualizarAtualAsync(request, ct);
}
