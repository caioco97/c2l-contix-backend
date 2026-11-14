using Contix.Api.Infrastructure;
using Contix.Application.Common.Models;
using Contix.Application.Features.NotasFiscais;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>
/// NF-e destinadas à empresa (módulo NF-e do portal): lista, manifestação do destinatário, XML e importação manual;
/// e a integração com a SEFAZ (certificado A1 e busca automática pela Distribuição DF-e), só para o escritório.
/// </summary>
[Route("api/v1/empresas/{empresaId:guid}")]
public sealed class NotasFiscaisController(INotaFiscalService notas, IIntegracaoSefazService sefaz) : ApiControllerBase
{
    /// <summary>NF-e da empresa, com filtros por período de emissão, manifestação, situação e busca.</summary>
    [HttpGet("notas-fiscais")]
    [ProducesResponseType(typeof(PagedResult<NotaFiscalDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<NotaFiscalDto>> Listar(Guid empresaId, [FromQuery] NotaFiscalFiltro filtro, CancellationToken ct) =>
        notas.ListarAsync(empresaId, filtro, ct);

    /// <summary>Total de notas e totais por manifestação (sem as canceladas).</summary>
    [HttpGet("notas-fiscais/resumo")]
    [ProducesResponseType(typeof(ResumoNotasFiscaisDto), StatusCodes.Status200OK)]
    public Task<ResumoNotasFiscaisDto> Resumo(Guid empresaId, CancellationToken ct) => notas.ResumoAsync(empresaId, ct);

    [HttpGet("notas-fiscais/{notaId:guid}")]
    [ProducesResponseType(typeof(NotaFiscalDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<NotaFiscalDto> Obter(Guid empresaId, Guid notaId, CancellationToken ct) => notas.ObterAsync(empresaId, notaId, ct);

    /// <summary>
    /// Manifestação do destinatário: Ciencia, Confirmada, Desconhecida ou NaoRealizada (justificativa de 15 a 255 caracteres).
    /// Com certificado A1 e integração ativa, o evento é enviado à SEFAZ; a recusa da SEFAZ responde 422 com o motivo.
    /// </summary>
    [HttpPost("notas-fiscais/{notaId:guid}/manifestacao")]
    [ProducesResponseType(typeof(NotaFiscalDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<NotaFiscalDto> Manifestar(Guid empresaId, Guid notaId, ManifestarNotaFiscalCommand command, CancellationToken ct) =>
        notas.ManifestarAsync(empresaId, notaId, command, ct);

    /// <summary>XML da nota (completo, ou o resumo da SEFAZ enquanto a ciência não liberou o completo).</summary>
    [HttpGet("notas-fiscais/{notaId:guid}/xml")]
    [Produces("application/xml", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> BaixarXml(Guid empresaId, Guid notaId, CancellationToken ct)
    {
        var arquivo = await notas.BaixarXmlAsync(empresaId, notaId, ct);
        return File(arquivo.Conteudo, arquivo.ContentType, arquivo.NomeArquivo);
    }

    /// <summary>DANFE em PDF, gerado a partir do XML completo da nota.</summary>
    [HttpGet("notas-fiscais/{notaId:guid}/danfe")]
    [Produces("application/pdf", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> BaixarDanfe(Guid empresaId, Guid notaId, CancellationToken ct)
    {
        var arquivo = await notas.BaixarDanfeAsync(empresaId, notaId, ct);
        return File(arquivo.Conteudo, arquivo.ContentType, arquivo.NomeArquivo);
    }

    /// <summary>XML e/ou DANFE das notas selecionadas em um .zip (até 500 notas). Conteudo: Xml, Danfe ou XmlEDanfe.</summary>
    [HttpPost("notas-fiscais/download")]
    [Produces("application/zip", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> BaixarLote(Guid empresaId, DownloadLoteNFeRequest request, CancellationToken ct)
    {
        var arquivo = await notas.BaixarLoteAsync(empresaId, request, ct);
        return File(arquivo.Conteudo, arquivo.ContentType, arquivo.NomeArquivo);
    }

    /// <summary>Importação manual de XMLs de NF-e (nfeProc). Responde o resultado de cada arquivo.</summary>
    [HttpPost("notas-fiscais/importacao")]
    [Authorize(Policy = Policies.Escritorio)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(100 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 100 * 1024 * 1024)]
    [ProducesResponseType(typeof(ImportacaoNFeDto), StatusCodes.Status200OK)]
    public Task<ImportacaoNFeDto> Importar(Guid empresaId, [FromForm] ImportacaoNFeForm form, CancellationToken ct) =>
        notas.ImportarAsync(empresaId, (form.Arquivos ?? [])
            .Select(a => new ArquivoImportacaoNFe(a.FileName, a.Length, a.OpenReadStream)).ToList(), ct);

    /// <summary>Certificado, busca automática e situação da última consulta à SEFAZ.</summary>
    [HttpGet("integracao-sefaz")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(IntegracaoSefazDto), StatusCodes.Status200OK)]
    public Task<IntegracaoSefazDto> ObterIntegracao(Guid empresaId, CancellationToken ct) => sefaz.ObterAsync(empresaId, ct);

    /// <summary>Liga/desliga a busca automática e a ciência automática das notas novas.</summary>
    [HttpPut("integracao-sefaz")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(IntegracaoSefazDto), StatusCodes.Status200OK)]
    public Task<IntegracaoSefazDto> SalvarIntegracao(Guid empresaId, SalvarIntegracaoSefazCommand command, CancellationToken ct) =>
        sefaz.SalvarAsync(empresaId, command, ct);

    /// <summary>Envia (ou substitui) o certificado A1 da empresa (.pfx + senha). O arquivo e a senha são guardados criptografados.</summary>
    [HttpPost("integracao-sefaz/certificado")]
    [Authorize(Policy = Policies.Escritorio)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(1024 * 1024)]
    [ProducesResponseType(typeof(IntegracaoSefazDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<IntegracaoSefazDto> EnviarCertificado(Guid empresaId, [FromForm] CertificadoForm form, CancellationToken ct)
    {
        if (form.Arquivo is null)
            throw new FluentValidation.ValidationException([new FluentValidation.Results.ValidationFailure("arquivo", "Envie o arquivo do certificado.")]);
        return sefaz.EnviarCertificadoAsync(empresaId,
            new EnviarCertificadoCommand(form.Arquivo.FileName, form.Arquivo.Length, form.Arquivo.OpenReadStream, form.Senha), ct);
    }

    [HttpDelete("integracao-sefaz/certificado")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(IntegracaoSefazDto), StatusCodes.Status200OK)]
    public Task<IntegracaoSefazDto> RemoverCertificado(Guid empresaId, CancellationToken ct) => sefaz.RemoverCertificadoAsync(empresaId, ct);

    /// <summary>"Buscar agora": antecipa a próxima busca na SEFAZ (o job processa em seguida). Responde 202.</summary>
    [HttpPost("integracao-sefaz/sincronizacoes")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(IntegracaoSefazDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IntegracaoSefazDto>> Sincronizar(Guid empresaId, CancellationToken ct) =>
        Accepted(await sefaz.SincronizarAgoraAsync(empresaId, ct));
}

public sealed class ImportacaoNFeForm
{
    public List<IFormFile>? Arquivos { get; set; }
}

public sealed class CertificadoForm
{
    public IFormFile? Arquivo { get; set; }
    public string? Senha { get; set; }
}
