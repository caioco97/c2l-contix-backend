using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Models;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;

namespace Contix.Application.Features.NotasFiscais;

/// <param name="ManifestacaoEnviadaSefaz">false: manifestação registrada só no Contix (sem certificado ou integração).</param>
public sealed record NotaFiscalDto(
    Guid Id, Guid EmpresaId, string ChaveAcesso, int Modelo, int Serie, long Numero,
    string EmitenteDocumento, string EmitenteNome, string? EmitenteIe,
    decimal ValorTotal, DateTime DataEmissao, int? TipoOperacao, string? Protocolo, SituacaoNFe Situacao,
    OrigemNotaFiscal Origem, bool XmlCompleto, bool PossuiXml,
    ManifestacaoNFe Manifestacao, DateTime? ManifestadaEm, string? ManifestadaPorNome, bool ManifestacaoAutomatica,
    string? JustificativaManifestacao, string? ProtocoloManifestacao, bool ManifestacaoEnviadaSefaz,
    DateTime CreatedAt);

public sealed record NotaFiscalFiltro : PageQuery
{
    /// <summary>Data de emissão inicial (inclusive).</summary>
    public DateOnly? De { get; init; }
    /// <summary>Data de emissão final (inclusive).</summary>
    public DateOnly? Ate { get; init; }
    public ManifestacaoNFe? Manifestacao { get; init; }
    public SituacaoNFe? Situacao { get; init; }
    /// <summary>Emitente (nome ou CNPJ), número ou chave de acesso.</summary>
    public string? Busca { get; init; }
}

/// <summary>Totais para os filtros rápidos da tela: todas as notas e, por manifestação, as não canceladas.</summary>
public sealed record ResumoNotasFiscaisDto(int Total, int SemManifestacao, int Ciencia, int Confirmadas, int Desconhecidas, int NaoRealizadas);

public sealed record ManifestarNotaFiscalCommand(ManifestacaoNFe Tipo, string? Justificativa);

public sealed class ManifestarNotaFiscalCommandValidator : AbstractValidator<ManifestarNotaFiscalCommand>
{
    public ManifestarNotaFiscalCommandValidator()
    {
        RuleFor(x => x.Tipo).IsInEnum().NotEqual(ManifestacaoNFe.SemManifestacao).WithMessage("Escolha o tipo de manifestação.");
        RuleFor(x => x.Justificativa)
            .NotEmpty().WithMessage("Informe a justificativa.")
            .Must(j => j!.Trim().Length is >= NotaFiscal.JustificativaMinima and <= NotaFiscal.JustificativaMaxima)
            .WithMessage($"A justificativa deve ter de {NotaFiscal.JustificativaMinima} a {NotaFiscal.JustificativaMaxima} caracteres.")
            .When(x => x.Tipo == ManifestacaoNFe.NaoRealizada);
    }
}

public sealed record ArquivoImportacaoNFe(string NomeArquivo, long Tamanho, Func<Stream> AbrirLeitura);

public enum ResultadoArquivoNFe { Importada = 1, Atualizada = 2, Ignorada = 3, Erro = 4 }

public sealed record ArquivoImportadoDto(string Arquivo, ResultadoArquivoNFe Resultado, string? ChaveAcesso, string? Mensagem);

public sealed record ImportacaoNFeDto(int Importadas, int Atualizadas, int Ignoradas, int Erros, IReadOnlyList<ArquivoImportadoDto> Arquivos);

public interface INotaFiscalRepository : IRepository<NotaFiscal>
{
    Task<PagedResult<NotaFiscalDto>> ListarAsync(Guid empresaId, NotaFiscalFiltro filtro, CancellationToken ct = default);
    Task<ResumoNotasFiscaisDto> ResumoAsync(Guid empresaId, CancellationToken ct = default);
    Task<NotaFiscalDto?> ObterDtoAsync(Guid empresaId, Guid id, CancellationToken ct = default);
    /// <summary>Rastreada.</summary>
    Task<NotaFiscal?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);
    /// <summary>Rastreada. Com <paramref name="ignorarTenant"/> (job), busca fora do filtro de tenant.</summary>
    Task<NotaFiscal?> ObterPorChaveAsync(Guid empresaId, string chaveAcesso, bool ignorarTenant, CancellationToken ct = default);
}
