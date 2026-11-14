using Contix.Api.Infrastructure;
using Contix.Application.Features.Obrigacoes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Modelos de obrigações recorrentes: geram as tarefas mensais, trimestrais ou anuais das empresas aplicáveis.</summary>
[Route("api/v1/obrigacoes")]
[Authorize(Policy = Policies.Escritorio)]
public sealed class ObrigacoesController(IObrigacaoService service) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ObrigacaoDto>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ObrigacaoDto>> Listar(CancellationToken ct) => service.ListarAsync(ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ObrigacaoDto), StatusCodes.Status200OK)]
    public Task<ObrigacaoDto> Obter(Guid id, CancellationToken ct) => service.ObterAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = Policies.Administrador)]
    [ProducesResponseType(typeof(ObrigacaoDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ObrigacaoDto>> Criar(SalvarObrigacaoRequest request, CancellationToken ct)
    {
        var o = await service.CriarAsync(request, ct);
        return CreatedAtAction(nameof(Obter), new { id = o.Id }, o);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Administrador)]
    [ProducesResponseType(typeof(ObrigacaoDto), StatusCodes.Status200OK)]
    public Task<ObrigacaoDto> Atualizar(Guid id, SalvarObrigacaoRequest request, CancellationToken ct) =>
        service.AtualizarAsync(id, request, ct);

    /// <summary>Exclui o modelo. As tarefas já geradas continuam no quadro.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.Administrador)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        await service.ExcluirAsync(id, ct);
        return NoContent();
    }

    /// <summary>Gera agora as tarefas de uma competência. Empresas que já têm a tarefa são ignoradas.</summary>
    [HttpPost("{id:guid}/geracoes")]
    [Authorize(Policy = Policies.Administrador)]
    [ProducesResponseType(typeof(GeracaoTarefasDto), StatusCodes.Status200OK)]
    public Task<GeracaoTarefasDto> Gerar(Guid id, GerarTarefasRequest request, CancellationToken ct) =>
        service.GerarAsync(id, request, ct);
}
