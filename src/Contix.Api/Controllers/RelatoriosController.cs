using Contix.Api.Infrastructure;
using Contix.Application.Features.Painel;
using Contix.Application.Features.Relatorios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Painel gerencial e relatórios exportáveis (CSV e PDF).</summary>
[Route("api/v1")]
public sealed class RelatoriosController(IRelatorioService relatorios, IPainelService painel) : ApiControllerBase
{
    /// <summary>Indicadores do escritório no período (padrão: mês atual), com filtro opcional por departamento.</summary>
    [HttpGet("painel")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(PainelDto), StatusCodes.Status200OK)]
    public Task<PainelDto> Painel([FromQuery] PainelFiltro filtro, CancellationToken ct) => painel.ObterAsync(filtro, ct);

    /// <summary>
    /// Relatório de documentos, solicitações ou tarefas. <c>formato=Json</c> devolve a prévia (até 200 linhas e o resumo);
    /// <c>Csv</c> e <c>Pdf</c> devolvem o arquivo (até 20 mil linhas).
    /// </summary>
    [HttpGet("relatorios/{tipo}")]
    [Authorize(Policy = Policies.Escritorio)]
    [Produces("application/json", "text/csv", "application/pdf")]
    [ProducesResponseType(typeof(RelatorioTabela), StatusCodes.Status200OK)]
    public async Task<IActionResult> Gerar(TipoRelatorio tipo, [FromQuery] RelatorioFiltro filtro, CancellationToken ct)
    {
        var tabela = await relatorios.GerarAsync(tipo, filtro, ct);
        return Responder(tabela, filtro.Formato, $"relatorio-{tipo.ToString().ToLowerInvariant()}");
    }

    /// <summary>Relatório de solicitações da empresa (portal do cliente e escritório).</summary>
    [HttpGet("empresas/{empresaId:guid}/relatorios/solicitacoes")]
    [Authorize]
    [Produces("application/json", "text/csv", "application/pdf")]
    [ProducesResponseType(typeof(RelatorioTabela), StatusCodes.Status200OK)]
    public async Task<IActionResult> GerarDaEmpresa(Guid empresaId, [FromQuery] RelatorioFiltro filtro, CancellationToken ct)
    {
        var tabela = await relatorios.GerarDaEmpresaAsync(empresaId, filtro, ct);
        return Responder(tabela, filtro.Formato, "relatorio-solicitacoes");
    }

    private IActionResult Responder(RelatorioTabela tabela, FormatoRelatorio formato, string nome)
    {
        if (formato == FormatoRelatorio.Json) return Ok(tabela);
        var arquivo = relatorios.Exportar(tabela, formato, nome);
        return File(arquivo.Conteudo, arquivo.ContentType, arquivo.NomeArquivo);
    }
}
