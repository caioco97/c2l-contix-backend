using Contix.Api.Infrastructure;
using Contix.Application.Features.Empregados;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>
/// Cadastro de empregados da empresa. O escritório mantém o cadastro; o cliente com o módulo
/// Solicitações consulta a lista para escolher o empregado nos formulários.
/// </summary>
[Route("api/v1/empresas/{empresaId:guid}/empregados")]
public sealed class EmpregadosController(IEmpregadoService service) : ApiControllerBase
{
    /// <summary>Empregados da empresa, por nome. Use <c>ativos=true</c> para omitir os desligados.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<EmpregadoDto>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<EmpregadoDto>> Listar(Guid empresaId, [FromQuery] EmpregadoFiltro filtro, CancellationToken ct) =>
        service.ListarAsync(empresaId, filtro, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(EmpregadoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<EmpregadoDto> Obter(Guid empresaId, Guid id, CancellationToken ct) => service.ObterAsync(empresaId, id, ct);

    /// <summary>Ficha do empregado: cadastro, situação de hoje (férias, afastamento), férias, afastamentos, desligamento e rubricas.</summary>
    [HttpGet("{id:guid}/ficha")]
    [ProducesResponseType(typeof(FichaEmpregadoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<FichaEmpregadoDto> Ficha(Guid empresaId, Guid id, CancellationToken ct) => service.ObterFichaAsync(empresaId, id, ct);

    [HttpPost]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(EmpregadoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EmpregadoDto>> Criar(Guid empresaId, SalvarEmpregadoRequest request, CancellationToken ct)
    {
        var empregado = await service.CriarAsync(empresaId, request, ct);
        return CreatedAtAction(nameof(Obter), new { empresaId, id = empregado.Id }, empregado);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(EmpregadoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<EmpregadoDto> Atualizar(Guid empresaId, Guid id, SalvarEmpregadoRequest request, CancellationToken ct) =>
        service.AtualizarAsync(empresaId, id, request, ct);

    /// <summary>Exclusão lógica. Solicitações já abertas mantêm o nome do empregado.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Excluir(Guid empresaId, Guid id, CancellationToken ct)
    {
        await service.ExcluirAsync(empresaId, id, ct);
        return NoContent();
    }
}
