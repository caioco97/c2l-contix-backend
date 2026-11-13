using Contix.Api.Infrastructure;
using Contix.Application.Common.Models;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Empresas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Empresas clientes. Usuários do cliente veem apenas as empresas vinculadas.</summary>
[Route("api/v1/empresas")]
public sealed class EmpresasController(
    IEmpresaService service, IHistoricoEmpresaService historico, IIdentidadeVisualService visual) : ApiControllerBase
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

    /// <summary>Indicadores da empresa: usuários, documentos pendentes, solicitações e certidões.</summary>
    [HttpGet("{id:guid}/painel")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(PainelEmpresaDto), StatusCodes.Status200OK)]
    public Task<PainelEmpresaDto> Painel(Guid id, CancellationToken ct) => historico.ObterPainelAsync(id, ct);

    /// <summary>Linha do tempo da empresa (documentos, solicitações, certidões e acessos), da mais recente para a mais antiga.</summary>
    [HttpGet("{id:guid}/historico")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(HistoricoEmpresaDto), StatusCodes.Status200OK)]
    public Task<HistoricoEmpresaDto> Historico(Guid id, [FromQuery] HistoricoEmpresaFiltro filtro, CancellationToken ct) =>
        historico.ListarHistoricoAsync(id, filtro, ct);

    /// <summary>Cores do portal da empresa. Campos nulos voltam para o padrão do Contix.</summary>
    [HttpPut("{id:guid}/identidade-visual")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(IdentidadeVisualDto), StatusCodes.Status200OK)]
    public Task<IdentidadeVisualDto> SalvarCores(Guid id, SalvarCoresRequest request, CancellationToken ct) =>
        visual.SalvarCoresAsync(id, request, ct);

    /// <summary>Envia (ou troca) o logotipo da empresa: PNG, JPG ou WEBP de até 1 MB.</summary>
    [HttpPut("{id:guid}/identidade-visual/logo")]
    [Authorize(Policy = Policies.Escritorio)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    [ProducesResponseType(typeof(IdentidadeVisualDto), StatusCodes.Status200OK)]
    public Task<IdentidadeVisualDto> EnviarLogo(Guid id, IFormFile arquivo, CancellationToken ct) =>
        visual.EnviarLogoAsync(id, new ArquivoUpload(arquivo.FileName, arquivo.ContentType, arquivo.Length, arquivo.OpenReadStream), ct);

    [HttpDelete("{id:guid}/identidade-visual/logo")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(IdentidadeVisualDto), StatusCodes.Status200OK)]
    public Task<IdentidadeVisualDto> RemoverLogo(Guid id, CancellationToken ct) => visual.RemoverLogoAsync(id, ct);

    /// <summary>Logotipo da empresa (escritório e usuários vinculados a ela).</summary>
    [HttpGet("{id:guid}/identidade-visual/logo")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Logo(Guid id, CancellationToken ct)
    {
        var logo = await visual.ObterLogoAsync(id, ct);
        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(logo.Conteudo, logo.ContentType);
    }
}
