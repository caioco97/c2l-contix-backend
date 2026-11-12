using Contix.Application.Common.Models;
using Contix.Application.Features.Documentos;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Documentos de uma empresa: listagem, upload, download e status de leitura.</summary>
[Route("api/v1/empresas/{empresaId:guid}/documentos")]
public sealed class DocumentosController(IDocumentoService service) : ApiControllerBase
{
    /// <summary>Lista documentos (filtros: pasta, texto, lidos/não lidos; ordenação: nome, publicadoEm, dataVencimento, modificado).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<DocumentoDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<DocumentoDto>> Pesquisar(Guid empresaId, [FromQuery] DocumentoFiltro filtro, CancellationToken ct) =>
        service.PesquisarAsync(empresaId, filtro, ct);

    [HttpGet("{documentoId:guid}")]
    [ProducesResponseType(typeof(DocumentoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<DocumentoDto> Obter(Guid empresaId, Guid documentoId, CancellationToken ct) =>
        service.ObterAsync(empresaId, documentoId, ct);

    /// <summary>
    /// Envia um ou mais arquivos para uma pasta. O escritório pode enviar para qualquer pasta;
    /// o cliente apenas para pastas com upload liberado.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentoDto>), StatusCodes.Status201Created)]
    public async Task<ActionResult<IReadOnlyList<DocumentoDto>>> Enviar(
        Guid empresaId, [FromForm] UploadDocumentosForm form, CancellationToken ct)
    {
        var command = new UploadDocumentosCommand(
            form.PastaId, form.Comentario, form.Competencia, form.DataVencimento,
            (form.Arquivos ?? []).Select(f => new ArquivoUpload(f.FileName, f.ContentType, f.Length, f.OpenReadStream)).ToList());

        var documentos = await service.EnviarAsync(empresaId, command, ct);
        return StatusCode(StatusCodes.Status201Created, documentos);
    }

    /// <summary>Baixa o arquivo e marca o documento como lido.</summary>
    [HttpGet("{documentoId:guid}/download")]
    [Produces("application/octet-stream", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Baixar(Guid empresaId, Guid documentoId, CancellationToken ct)
    {
        var arquivo = await service.BaixarAsync(empresaId, documentoId, ct);
        return File(arquivo.Conteudo, arquivo.ContentType, arquivo.NomeArquivo);
    }

    /// <summary>Baixa vários documentos em um arquivo .zip e marca todos como lidos.</summary>
    [HttpPost("download")]
    [Produces("application/zip", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> BaixarZip(Guid empresaId, DownloadDocumentosRequest request, CancellationToken ct)
    {
        var arquivo = await service.BaixarZipAsync(empresaId, request, ct);
        return File(arquivo.Conteudo, arquivo.ContentType, arquivo.NomeArquivo);
    }

    [HttpPost("{documentoId:guid}/lido")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarcarComoLido(Guid empresaId, Guid documentoId, CancellationToken ct)
    {
        await service.MarcarComoLidoAsync(empresaId, documentoId, ct);
        return NoContent();
    }

    /// <summary>Exclusão lógica. Cliente só exclui documentos que ele mesmo enviou.</summary>
    [HttpDelete("{documentoId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Excluir(Guid empresaId, Guid documentoId, CancellationToken ct)
    {
        await service.ExcluirAsync(empresaId, documentoId, ct);
        return NoContent();
    }

    /// <summary>Documentos com vencimento no período (calendário de vencimentos). Padrão: mês atual.</summary>
    [HttpGet("vencimentos")]
    [ProducesResponseType(typeof(IReadOnlyList<VencimentoDto>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<VencimentoDto>> Vencimentos(
        Guid empresaId, [FromQuery] DateOnly? inicio, [FromQuery] DateOnly? fim, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var de = inicio ?? new DateOnly(hoje.Year, hoje.Month, 1);
        var ate = fim ?? de.AddMonths(1).AddDays(-1);
        return service.ListarVencimentosAsync(empresaId, de, ate, ct);
    }
}

public sealed class UploadDocumentosForm
{
    public Guid PastaId { get; set; }
    public string? Comentario { get; set; }
    public DateOnly? Competencia { get; set; }
    public DateOnly? DataVencimento { get; set; }
    public List<IFormFile>? Arquivos { get; set; }
}
