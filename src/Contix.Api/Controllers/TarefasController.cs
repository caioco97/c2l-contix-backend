using Contix.Api.Infrastructure;
using Contix.Application.Common.Models;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Tarefas;
using Contix.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Tarefas internas do escritório por empresa: prazos, responsável, comentários e anexos. Exclusivo do escritório.</summary>
[Route("api/v1")]
[Authorize(Policy = Policies.Escritorio)]
public sealed class TarefasController(ITarefaService service) : ApiControllerBase
{
    /// <summary>Tarefas de todas as empresas. Filtros: empresa, status, em aberto, departamento, responsável, atrasadas, prazo até.</summary>
    [HttpGet("tarefas")]
    [ProducesResponseType(typeof(PagedResult<TarefaResumoDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<TarefaResumoDto>> Pesquisar([FromQuery] TarefaFiltro filtro, CancellationToken ct) =>
        service.PesquisarAsync(filtro, ct);

    /// <summary>Tarefas de uma empresa.</summary>
    [HttpGet("empresas/{empresaId:guid}/tarefas")]
    [ProducesResponseType(typeof(PagedResult<TarefaResumoDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<TarefaResumoDto>> Listar(Guid empresaId, [FromQuery] TarefaFiltro filtro, CancellationToken ct) =>
        service.PesquisarDaEmpresaAsync(empresaId, filtro, ct);

    [HttpGet("empresas/{empresaId:guid}/tarefas/{tarefaId:guid}")]
    [ProducesResponseType(typeof(TarefaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<TarefaDto> Obter(Guid empresaId, Guid tarefaId, CancellationToken ct) =>
        service.ObterAsync(empresaId, tarefaId, ct);

    /// <summary>Cria uma tarefa (com anexos opcionais). O responsável é avisado por notificação.</summary>
    [HttpPost("empresas/{empresaId:guid}/tarefas")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(TarefaDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<TarefaDto>> Criar(Guid empresaId, [FromForm] CriarTarefaForm form, CancellationToken ct)
    {
        var tarefa = await service.CriarAsync(empresaId, new CriarTarefaCommand(
            form.Titulo ?? "", form.Descricao, form.Departamento, form.ResponsavelId, form.PrazoInterno, form.PrazoLegal,
            Arquivos(form.Anexos), form.SolicitacaoId), ct);
        return CreatedAtAction(nameof(Obter), new { empresaId, tarefaId = tarefa.Id }, tarefa);
    }

    /// <summary>Altera título, descrição, departamento, responsável e prazos. As mudanças ficam no histórico.</summary>
    [HttpPut("empresas/{empresaId:guid}/tarefas/{tarefaId:guid}")]
    [ProducesResponseType(typeof(TarefaDto), StatusCodes.Status200OK)]
    public Task<TarefaDto> Editar(Guid empresaId, Guid tarefaId, EditarTarefaRequest request, CancellationToken ct) =>
        service.EditarAsync(empresaId, tarefaId, request, ct);

    /// <summary>Adiciona um comentário (com anexos opcionais).</summary>
    [HttpPost("empresas/{empresaId:guid}/tarefas/{tarefaId:guid}/comentarios")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(TarefaDto), StatusCodes.Status200OK)]
    public Task<TarefaDto> Comentar(Guid empresaId, Guid tarefaId, [FromForm] ComentarioForm form, CancellationToken ct) =>
        service.ComentarAsync(empresaId, tarefaId, new ComentarTarefaCommand(form.Mensagem, Arquivos(form.Anexos)), ct);

    /// <summary>Muda o status. As transições permitidas vêm em <c>transicoesPermitidas</c>.</summary>
    [HttpPost("empresas/{empresaId:guid}/tarefas/{tarefaId:guid}/status")]
    [ProducesResponseType(typeof(TarefaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<TarefaDto> AlterarStatus(Guid empresaId, Guid tarefaId, AlterarStatusTarefaRequest request, CancellationToken ct) =>
        service.AlterarStatusAsync(empresaId, tarefaId, request, ct);

    /// <summary>
    /// Conclui a tarefa. Com arquivos, publica-os na pasta escolhida da empresa (com competência e vencimento opcionais)
    /// e o cliente é avisado como em qualquer documento novo.
    /// </summary>
    [HttpPost("empresas/{empresaId:guid}/tarefas/{tarefaId:guid}/conclusao")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(TarefaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<TarefaDto> Concluir(Guid empresaId, Guid tarefaId, [FromForm] ConcluirTarefaForm form, CancellationToken ct) =>
        service.ConcluirAsync(empresaId, tarefaId, new ConcluirTarefaCommand(
            form.Mensagem, form.PastaId, form.Competencia, form.DataVencimento, Arquivos(form.Arquivos)), ct);

    /// <summary>Tarefas criadas a partir de uma solicitação do cliente.</summary>
    [HttpGet("empresas/{empresaId:guid}/solicitacoes/{solicitacaoId:guid}/tarefas")]
    [ProducesResponseType(typeof(IReadOnlyList<TarefaResumoDto>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<TarefaResumoDto>> ListarDaSolicitacao(Guid empresaId, Guid solicitacaoId, CancellationToken ct) =>
        service.ListarDaSolicitacaoAsync(empresaId, solicitacaoId, ct);

    [HttpGet("empresas/{empresaId:guid}/tarefas/{tarefaId:guid}/anexos/{anexoId:guid}/download")]
    [Produces("application/octet-stream", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> BaixarAnexo(Guid empresaId, Guid tarefaId, Guid anexoId, CancellationToken ct)
    {
        var arquivo = await service.BaixarAnexoAsync(empresaId, tarefaId, anexoId, ct);
        return File(arquivo.Conteudo, arquivo.ContentType, arquivo.NomeArquivo);
    }

    private static List<ArquivoUpload> Arquivos(List<IFormFile>? arquivos) =>
        (arquivos ?? []).Select(f => new ArquivoUpload(f.FileName, f.ContentType, f.Length, f.OpenReadStream)).ToList();
}

public sealed class CriarTarefaForm
{
    public string? Titulo { get; set; }
    public string? Descricao { get; set; }
    public Departamento Departamento { get; set; }
    public Guid ResponsavelId { get; set; }
    public DateOnly? PrazoInterno { get; set; }
    public DateOnly? PrazoLegal { get; set; }
    public List<IFormFile>? Anexos { get; set; }
    public Guid? SolicitacaoId { get; set; }
}

public sealed class ConcluirTarefaForm
{
    public string? Mensagem { get; set; }
    public Guid? PastaId { get; set; }
    public DateOnly? Competencia { get; set; }
    public DateOnly? DataVencimento { get; set; }
    public List<IFormFile>? Arquivos { get; set; }
}
