using Contix.Api.Infrastructure;
using Contix.Application.Common.Models;
using Contix.Application.Features.Empresas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Empresas clientes. Usuários do cliente veem apenas as empresas vinculadas.</summary>
[Route("api/v1/empresas")]
public sealed class EmpresasController(IEmpresaService service) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<EmpresaResumoDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<EmpresaResumoDto>> Pesquisar([FromQuery] EmpresaFiltro filtro, CancellationToken ct) =>
        service.PesquisarAsync(filtro, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(EmpresaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<EmpresaDto> Obter(Guid id, CancellationToken ct) => service.ObterAsync(id, ct);

    /// <summary>Cadastra uma empresa e cria suas pastas padrão.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(EmpresaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EmpresaDto>> Criar(SalvarEmpresaRequest request, CancellationToken ct)
    {
        var empresa = await service.CriarAsync(request, ct);
        return CreatedAtAction(nameof(Obter), new { id = empresa.Id }, empresa);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(EmpresaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<EmpresaDto> Atualizar(Guid id, SalvarEmpresaRequest request, CancellationToken ct) =>
        service.AtualizarAsync(id, request, ct);

    /// <summary>Exclusão lógica da empresa.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.Administrador)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        await service.ExcluirAsync(id, ct);
        return NoContent();
    }
}
