using Contix.Api.Infrastructure;
using Contix.Application.Common.Models;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Solicitacoes;
using Contix.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Solicitações de serviço: catálogo, fila do escritório e atendimento por empresa.</summary>
[Route("api/v1")]
public sealed class SolicitacoesController(ISolicitacaoService service) : ApiControllerBase
{
    /// <summary>Catálogo de tipos de solicitação.</summary>
    [HttpGet("solicitacoes/tipos")]
    [ProducesResponseType(typeof(IReadOnlyList<TipoSolicitacaoDto>), StatusCodes.Status200OK)]
    public IReadOnlyList<TipoSolicitacaoDto> Tipos() => service.ListarTipos();

    /// <summary>Fila de atendimento do escritório (todas as empresas). Filtros: empresa, tipo, status, departamento, responsável, atrasadas.</summary>
    [HttpGet("solicitacoes")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(PagedResult<SolicitacaoResumoDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<SolicitacaoResumoDto>> Fila([FromQuery] SolicitacaoFiltro filtro, CancellationToken ct) =>
        service.PesquisarAsync(filtro, ct);

    /// <summary>Solicitações de uma empresa (portal do cliente).</summary>
    [HttpGet("empresas/{empresaId:guid}/solicitacoes")]
    [ProducesResponseType(typeof(PagedResult<SolicitacaoResumoDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<SolicitacaoResumoDto>> Listar(Guid empresaId, [FromQuery] SolicitacaoFiltro filtro, CancellationToken ct) =>
        service.PesquisarDaEmpresaAsync(empresaId, filtro, ct);

    [HttpGet("empresas/{empresaId:guid}/solicitacoes/{solicitacaoId:guid}")]
    [ProducesResponseType(typeof(SolicitacaoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<SolicitacaoDto> Obter(Guid empresaId, Guid solicitacaoId, CancellationToken ct) =>
        service.ObterAsync(empresaId, solicitacaoId, ct);

    /// <summary>
    /// Abre uma solicitação. Os campos específicos do tipo (férias, rescisão, cadastro...) vão em <c>dados</c>, como JSON.
    /// Só o usuário do cliente abre; o escritório atende pela fila.
    /// </summary>
    [HttpPost("empresas/{empresaId:guid}/solicitacoes")]
    [Authorize(Policy = Policies.Cliente)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(SolicitacaoDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<SolicitacaoDto>> Abrir(Guid empresaId, [FromForm] AbrirSolicitacaoForm form, CancellationToken ct)
    {
        var solicitacao = await service.AbrirAsync(empresaId,
            new AbrirSolicitacaoCommand(form.Tipo, form.Assunto, form.Departamento, form.Descricao, form.Dados, Arquivos(form.Anexos)), ct);
        return CreatedAtAction(nameof(Obter), new { empresaId, solicitacaoId = solicitacao.Id }, solicitacao);
    }

    /// <summary>Adiciona um comentário (com anexos opcionais). Resposta do cliente a uma solicitação "Aguardando cliente" a devolve para "Em análise".</summary>
    [HttpPost("empresas/{empresaId:guid}/solicitacoes/{solicitacaoId:guid}/comentarios")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(SolicitacaoDto), StatusCodes.Status200OK)]
    public Task<SolicitacaoDto> Comentar(Guid empresaId, Guid solicitacaoId, [FromForm] ComentarioForm form, CancellationToken ct) =>
        service.ComentarAsync(empresaId, solicitacaoId, new ComentarSolicitacaoCommand(form.Mensagem, Arquivos(form.Anexos)), ct);

    /// <summary>Muda o status. O cliente só pode cancelar; as transições permitidas vêm em <c>transicoesPermitidas</c>.</summary>
    [HttpPost("empresas/{empresaId:guid}/solicitacoes/{solicitacaoId:guid}/status")]
    [ProducesResponseType(typeof(SolicitacaoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<SolicitacaoDto> AlterarStatus(Guid empresaId, Guid solicitacaoId, AlterarStatusRequest request, CancellationToken ct) =>
        service.AlterarStatusAsync(empresaId, solicitacaoId, request, ct);

    /// <summary>Define o responsável e o prazo (SLA).</summary>
    [HttpPut("empresas/{empresaId:guid}/solicitacoes/{solicitacaoId:guid}/atribuicao")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(SolicitacaoDto), StatusCodes.Status200OK)]
    public Task<SolicitacaoDto> Atribuir(Guid empresaId, Guid solicitacaoId, AtribuirSolicitacaoRequest request, CancellationToken ct) =>
        service.AtribuirAsync(empresaId, solicitacaoId, request, ct);

    [HttpGet("empresas/{empresaId:guid}/solicitacoes/{solicitacaoId:guid}/anexos/{anexoId:guid}/download")]
    [Produces("application/octet-stream", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> BaixarAnexo(Guid empresaId, Guid solicitacaoId, Guid anexoId, CancellationToken ct)
    {
        var arquivo = await service.BaixarAnexoAsync(empresaId, solicitacaoId, anexoId, ct);
        return File(arquivo.Conteudo, arquivo.ContentType, arquivo.NomeArquivo);
    }

    private static List<ArquivoUpload> Arquivos(List<IFormFile>? arquivos) =>
        (arquivos ?? []).Select(f => new ArquivoUpload(f.FileName, f.ContentType, f.Length, f.OpenReadStream)).ToList();
}

public sealed class AbrirSolicitacaoForm
{
    public TipoSolicitacao Tipo { get; set; }
    public string? Assunto { get; set; }
    public Departamento? Departamento { get; set; }
    public string? Descricao { get; set; }
    /// <summary>JSON com os campos específicos do tipo.</summary>
    public string? Dados { get; set; }
    public List<IFormFile>? Anexos { get; set; }
}

public sealed class ComentarioForm
{
    public string? Mensagem { get; set; }
    public List<IFormFile>? Anexos { get; set; }
}
