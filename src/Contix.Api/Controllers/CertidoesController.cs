using Contix.Api.Infrastructure;
using Contix.Application.Features.Certidoes;
using Contix.Application.Features.Certidoes.Emissao;
using Contix.Application.Features.Documentos;
using Contix.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Certidões negativas (CND): cadastro pelo escritório, consulta e download pelo cliente, painel de vencimentos.</summary>
[Route("api/v1")]
public sealed class CertidoesController(ICertidaoService service, IEmissaoCertidaoService emissao) : ApiControllerBase
{
    /// <summary>Configuração da emissão automática da empresa e o que a integração deste ambiente suporta.</summary>
    [HttpGet("empresas/{empresaId:guid}/certidoes/emissao-automatica")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(ConfiguracaoEmissaoDto), StatusCodes.Status200OK)]
    public Task<ConfiguracaoEmissaoDto> ObterEmissaoAutomatica(Guid empresaId, CancellationToken ct) =>
        emissao.ObterConfiguracaoAsync(empresaId, ct);

    /// <summary>Liga/desliga a emissão automática, escolhe os tipos, a antecedência da renovação e o responsável pelas falhas.</summary>
    [HttpPut("empresas/{empresaId:guid}/certidoes/emissao-automatica")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(ConfiguracaoEmissaoDto), StatusCodes.Status200OK)]
    public Task<ConfiguracaoEmissaoDto> SalvarEmissaoAutomatica(Guid empresaId, SalvarConfiguracaoEmissaoCommand command, CancellationToken ct) =>
        emissao.SalvarConfiguracaoAsync(empresaId, command, ct);

    /// <summary>Histórico de emissões automáticas da empresa (mais recentes primeiro).</summary>
    [HttpGet("empresas/{empresaId:guid}/certidoes/emissoes")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(IReadOnlyList<EmissaoCertidaoDto>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<EmissaoCertidaoDto>> ListarEmissoes(Guid empresaId, [FromQuery] int limite = 30, CancellationToken ct = default) =>
        emissao.ListarAsync(empresaId, limite, ct);

    /// <summary>"Emitir agora": coloca na fila a emissão dos tipos informados (vazio: os configurados). Responde 202.</summary>
    [HttpPost("empresas/{empresaId:guid}/certidoes/emissoes")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(IReadOnlyList<EmissaoCertidaoDto>), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<IReadOnlyList<EmissaoCertidaoDto>>> Emitir(Guid empresaId, EmitirCertidoesCommand command, CancellationToken ct) =>
        Accepted(await emissao.EmitirAgoraAsync(empresaId, command, ct));

    /// <summary>
    /// Painel do escritório: certidões atuais vencidas, a vencer em até <c>dias</c> (padrão 30) e positivas,
    /// de todas as empresas, com os totais de vencidas e a vencer em 15/30 dias.
    /// </summary>
    [HttpGet("certidoes/painel")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(typeof(PainelCertidoesDto), StatusCodes.Status200OK)]
    public Task<PainelCertidoesDto> Painel([FromQuery] int dias = 30, CancellationToken ct = default) =>
        service.PainelAsync(dias, ct);

    /// <summary>Certidões da empresa (histórico completo, ou só a atual de cada tipo com <c>somenteAtuais=true</c>).</summary>
    [HttpGet("empresas/{empresaId:guid}/certidoes")]
    [ProducesResponseType(typeof(IReadOnlyList<CertidaoDto>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<CertidaoDto>> Listar(Guid empresaId, [FromQuery] CertidaoFiltro filtro, CancellationToken ct) =>
        service.ListarAsync(empresaId, filtro, ct);

    [HttpGet("empresas/{empresaId:guid}/certidoes/{certidaoId:guid}")]
    [ProducesResponseType(typeof(CertidaoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<CertidaoDto> Obter(Guid empresaId, Guid certidaoId, CancellationToken ct) =>
        service.ObterAsync(empresaId, certidaoId, ct);

    [HttpPost("empresas/{empresaId:guid}/certidoes")]
    [Authorize(Policy = Policies.Escritorio)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CertidaoDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CertidaoDto>> Cadastrar(Guid empresaId, [FromForm] CertidaoForm form, CancellationToken ct)
    {
        var certidao = await service.CadastrarAsync(empresaId, form.ToCommand(), ct);
        return CreatedAtAction(nameof(Obter), new { empresaId, certidaoId = certidao.Id }, certidao);
    }

    /// <summary>Altera a certidão. Um novo <c>arquivo</c> substitui o anterior; <c>removerArquivo</c> o retira.</summary>
    [HttpPut("empresas/{empresaId:guid}/certidoes/{certidaoId:guid}")]
    [Authorize(Policy = Policies.Escritorio)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CertidaoDto), StatusCodes.Status200OK)]
    public Task<CertidaoDto> Atualizar(Guid empresaId, Guid certidaoId, [FromForm] CertidaoForm form, CancellationToken ct) =>
        service.AtualizarAsync(empresaId, certidaoId, form.ToCommand(), ct);

    [HttpDelete("empresas/{empresaId:guid}/certidoes/{certidaoId:guid}")]
    [Authorize(Policy = Policies.Escritorio)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Excluir(Guid empresaId, Guid certidaoId, CancellationToken ct)
    {
        await service.ExcluirAsync(empresaId, certidaoId, ct);
        return NoContent();
    }

    [HttpGet("empresas/{empresaId:guid}/certidoes/{certidaoId:guid}/download")]
    [Produces("application/octet-stream", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Baixar(Guid empresaId, Guid certidaoId, CancellationToken ct)
    {
        var arquivo = await service.BaixarAsync(empresaId, certidaoId, ct);
        return File(arquivo.Conteudo, arquivo.ContentType, arquivo.NomeArquivo);
    }
}

public sealed class CertidaoForm
{
    public TipoCertidao Tipo { get; set; }
    public SituacaoCertidao Situacao { get; set; } = SituacaoCertidao.Negativa;
    public string? Numero { get; set; }
    public DateOnly Emissao { get; set; }
    public DateOnly Validade { get; set; }
    public string? Observacao { get; set; }
    public IFormFile? Arquivo { get; set; }
    public bool RemoverArquivo { get; set; }

    public SalvarCertidaoCommand ToCommand() => new(
        Tipo, Situacao, Numero, Emissao, Validade, Observacao,
        Arquivo is null ? null : new ArquivoUpload(Arquivo.FileName, Arquivo.ContentType, Arquivo.Length, Arquivo.OpenReadStream),
        RemoverArquivo);
}
