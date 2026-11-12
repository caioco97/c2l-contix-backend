using Contix.Api.Infrastructure;
using Contix.Application.Features.Pastas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Árvore de pastas de documentos de uma empresa.</summary>
[Route("api/v1/empresas/{empresaId:guid}/pastas")]
public sealed class PastasController(IPastaService service) : ApiControllerBase
{
    /// <summary>Lista a árvore de pastas com a contagem de documentos não lidos pelo usuário.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PastaDto>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<PastaDto>> Listar(Guid empresaId, CancellationToken ct) =>
        service.ListarArvoreAsync(empresaId, ct);

    [HttpPost]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(PastaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PastaDto>> Criar(Guid empresaId, CriarPastaRequest request, CancellationToken ct)
    {
        var pasta = await service.CriarAsync(empresaId, request, ct);
        return CreatedAtAction(nameof(Listar), new { empresaId }, pasta);
    }

    [HttpPut("{pastaId:guid}")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(PastaDto), StatusCodes.Status200OK)]
    public Task<PastaDto> Atualizar(Guid empresaId, Guid pastaId, AtualizarPastaRequest request, CancellationToken ct) =>
        service.AtualizarAsync(empresaId, pastaId, request, ct);

    /// <summary>Exclui uma pasta vazia.</summary>
    [HttpDelete("{pastaId:guid}")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Excluir(Guid empresaId, Guid pastaId, CancellationToken ct)
    {
        await service.ExcluirAsync(empresaId, pastaId, ct);
        return NoContent();
    }
}
