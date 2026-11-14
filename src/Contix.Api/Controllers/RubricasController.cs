using Contix.Application.Features.Solicitacoes;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>
/// Lançamento de rubricas da folha por planilha: baixe o modelo (já com os empregados ativos),
/// preencha e envie. A resposta é uma prévia validada; a solicitação é aberta pelo fluxo normal.
/// </summary>
[Route("api/v1/empresas/{empresaId:guid}/rubricas")]
public sealed class RubricasController(IImportacaoRubricasService service) : ApiControllerBase
{
    [HttpGet("modelo")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "text/csv", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Modelo(Guid empresaId, [FromQuery] FormatoPlanilha formato = FormatoPlanilha.Xlsx, CancellationToken ct = default)
    {
        var arquivo = await service.ModeloAsync(empresaId, formato, ct);
        return File(arquivo.Conteudo, arquivo.ContentType, arquivo.NomeArquivo);
    }

    /// <summary>Lê a planilha (.xlsx ou .csv, até 500 linhas e 2 MB) e devolve os lançamentos válidos e os erros por linha.</summary>
    [HttpPost("importacao")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    [ProducesResponseType(typeof(ImportacaoRubricasDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<ImportacaoRubricasDto> Importar(Guid empresaId, IFormFile arquivo, CancellationToken ct) =>
        service.LerAsync(empresaId, new Application.Features.Documentos.ArquivoUpload(arquivo.FileName, arquivo.ContentType, arquivo.Length, arquivo.OpenReadStream), ct);
}
