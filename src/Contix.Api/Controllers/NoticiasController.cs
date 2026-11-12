using Contix.Api.Infrastructure;
using Contix.Application.Common.Models;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Noticias;
using Contix.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Notícias e comunicados: publicação pelo escritório e leitura no portal da empresa.</summary>
[Route("api/v1")]
public sealed class NoticiasController(INoticiaService service) : ApiControllerBase
{
    /* ---------- Escritório ---------- */

    /// <summary>Notícias do escritório com alcance (empresas que leram / destinatárias). Filtros: busca, situação.</summary>
    [HttpGet("noticias")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(PagedResult<NoticiaResumoDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<NoticiaResumoDto>> Pesquisar([FromQuery] NoticiaFiltro filtro, CancellationToken ct) =>
        service.PesquisarAsync(filtro, ct);

    [HttpGet("noticias/{noticiaId:guid}")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(NoticiaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<NoticiaDto> Obter(Guid noticiaId, CancellationToken ct) => service.ObterAsync(noticiaId, ct);

    /// <summary>
    /// Publica (ou agenda) uma notícia. Público: <c>Todos</c>, <c>EmpresasSelecionadas</c> (com <c>empresaIds</c>)
    /// ou <c>RegimeTributario</c> (com <c>regimes</c>). Sem <c>publicarEm</c>, publica imediatamente.
    /// </summary>
    [HttpPost("noticias")]
    [Authorize(Policy = Policies.Escritorio)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(NoticiaDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<NoticiaDto>> Criar([FromForm] NoticiaForm form, CancellationToken ct)
    {
        var noticia = await service.CriarAsync(form.ToCommand(), ct);
        return CreatedAtAction(nameof(Obter), new { noticiaId = noticia.Id }, noticia);
    }

    /// <summary>Altera a notícia. Anexos enviados são somados; use <c>anexosRemovidos</c> e <c>removerImagem</c> para retirar.</summary>
    [HttpPut("noticias/{noticiaId:guid}")]
    [Authorize(Policy = Policies.Escritorio)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(NoticiaDto), StatusCodes.Status200OK)]
    public Task<NoticiaDto> Atualizar(Guid noticiaId, [FromForm] NoticiaForm form, CancellationToken ct) =>
        service.AtualizarAsync(noticiaId, form.ToCommand(), ct);

    [HttpDelete("noticias/{noticiaId:guid}")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Excluir(Guid noticiaId, CancellationToken ct)
    {
        await service.ExcluirAsync(noticiaId, ct);
        return NoContent();
    }

    /// <summary>Empresas destinatárias e se algum usuário do cliente já leu.</summary>
    [HttpGet("noticias/{noticiaId:guid}/leituras")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(IReadOnlyList<LeituraNoticiaDto>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<LeituraNoticiaDto>> Leituras(Guid noticiaId, CancellationToken ct) =>
        service.ListarLeiturasAsync(noticiaId, ct);

    [HttpGet("noticias/{noticiaId:guid}/imagem")]
    [Authorize(Policy = Policies.Escritorio)]
    [Produces("application/octet-stream", "application/json")]
    public async Task<IActionResult> Imagem(Guid noticiaId, CancellationToken ct) =>
        Arquivo(await service.BaixarImagemAsync(noticiaId, ct), inline: true);

    [HttpGet("noticias/{noticiaId:guid}/anexos/{anexoId:guid}/download")]
    [Authorize(Policy = Policies.Escritorio)]
    [Produces("application/octet-stream", "application/json")]
    public async Task<IActionResult> Anexo(Guid noticiaId, Guid anexoId, CancellationToken ct) =>
        Arquivo(await service.BaixarAnexoAsync(noticiaId, anexoId, ct));

    /* ---------- Portal da empresa ---------- */

    /// <summary>Notícias publicadas para a empresa. <c>status</c>: Todos, NaoLidos ou Lidos (do usuário atual).</summary>
    [HttpGet("empresas/{empresaId:guid}/noticias")]
    [ProducesResponseType(typeof(PagedResult<NoticiaPortalDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<NoticiaPortalDto>> ListarDaEmpresa(Guid empresaId, [FromQuery] NoticiaPortalFiltro filtro, CancellationToken ct) =>
        service.PesquisarDaEmpresaAsync(empresaId, filtro, ct);

    [HttpGet("empresas/{empresaId:guid}/noticias/{noticiaId:guid}")]
    [ProducesResponseType(typeof(NoticiaPortalDetalheDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<NoticiaPortalDetalheDto> ObterDaEmpresa(Guid empresaId, Guid noticiaId, CancellationToken ct) =>
        service.ObterDaEmpresaAsync(empresaId, noticiaId, ct);

    [HttpPost("empresas/{empresaId:guid}/noticias/{noticiaId:guid}/lida")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarcarComoLida(Guid empresaId, Guid noticiaId, CancellationToken ct)
    {
        await service.MarcarComoLidaAsync(empresaId, noticiaId, ct);
        return NoContent();
    }

    [HttpGet("empresas/{empresaId:guid}/noticias/{noticiaId:guid}/imagem")]
    [Produces("application/octet-stream", "application/json")]
    public async Task<IActionResult> ImagemDaEmpresa(Guid empresaId, Guid noticiaId, CancellationToken ct) =>
        Arquivo(await service.BaixarImagemDaEmpresaAsync(empresaId, noticiaId, ct), inline: true);

    [HttpGet("empresas/{empresaId:guid}/noticias/{noticiaId:guid}/anexos/{anexoId:guid}/download")]
    [Produces("application/octet-stream", "application/json")]
    public async Task<IActionResult> AnexoDaEmpresa(Guid empresaId, Guid noticiaId, Guid anexoId, CancellationToken ct) =>
        Arquivo(await service.BaixarAnexoDaEmpresaAsync(empresaId, noticiaId, anexoId, ct));

    private FileStreamResult Arquivo(ArquivoParaDownload arquivo, bool inline = false) =>
        inline ? File(arquivo.Conteudo, arquivo.ContentType) : File(arquivo.Conteudo, arquivo.ContentType, arquivo.NomeArquivo);
}

public sealed class NoticiaForm
{
    public string Titulo { get; set; } = "";
    public string? Resumo { get; set; }
    public string Conteudo { get; set; } = "";
    public DateTime? PublicarEm { get; set; }
    public DateTime? ExpiraEm { get; set; }
    public PublicoNoticia Publico { get; set; } = PublicoNoticia.Todos;
    public List<Guid>? EmpresaIds { get; set; }
    public List<RegimeTributario>? Regimes { get; set; }
    public IFormFile? Imagem { get; set; }
    public bool RemoverImagem { get; set; }
    public List<IFormFile>? Anexos { get; set; }
    public List<Guid>? AnexosRemovidos { get; set; }

    public SalvarNoticiaCommand ToCommand() => new(
        Titulo, Resumo, Conteudo, PublicarEm, ExpiraEm, Publico,
        EmpresaIds ?? [], Regimes ?? [],
        Imagem is null ? null : new ArquivoUpload(Imagem.FileName, Imagem.ContentType, Imagem.Length, Imagem.OpenReadStream),
        RemoverImagem,
        (Anexos ?? []).Select(f => new ArquivoUpload(f.FileName, f.ContentType, f.Length, f.OpenReadStream)).ToList(),
        AnexosRemovidos ?? []);
}
