using Contix.Application.Features.Documentos;
using Contix.Application.Features.Solicitacoes;
using Contix.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Contix.Application.Features.Certidoes;

/// <summary>Nomes exibidos dos tipos de certidão.</summary>
public static class CatalogoCertidoes
{
    public static readonly IReadOnlyDictionary<TipoCertidao, string> Nomes = new Dictionary<TipoCertidao, string>
    {
        [TipoCertidao.FederalPgfn] = "Federal (RFB/PGFN)",
        [TipoCertidao.Estadual] = "Estadual",
        [TipoCertidao.Municipal] = "Municipal",
        [TipoCertidao.Fgts] = "FGTS (CRF)",
        [TipoCertidao.Trabalhista] = "Trabalhista (CNDT)",
    };

    public static string Nome(TipoCertidao tipo) => Nomes.GetValueOrDefault(tipo, tipo.ToString());
}

public sealed record CertidaoDto(
    Guid Id, Guid EmpresaId, string EmpresaNome, TipoCertidao Tipo, string TipoNome, SituacaoCertidao Situacao,
    string? Numero, DateOnly Emissao, DateOnly Validade, ValidadeCertidao StatusValidade, int DiasParaVencer,
    bool Regular, bool Atual, string? Observacao, bool PossuiArquivo, string? NomeArquivo, long? TamanhoBytes,
    string CadastradoPorNome, bool EmitidaAutomaticamente, DateTime CreatedAt, DateTime? UpdatedAt);

/// <summary>Painel do escritório: certidões atuais vencidas, a vencer e irregulares de todas as empresas.</summary>
public sealed record PainelCertidoesDto(
    int Vencidas, int AVencer15Dias, int AVencer30Dias, int Irregulares, IReadOnlyList<CertidaoDto> Itens);

public sealed record CertidaoFiltro
{
    /// <summary>true: só a certidão mais recente de cada tipo.</summary>
    public bool SomenteAtuais { get; init; }
    public TipoCertidao? Tipo { get; init; }
}

public sealed record SalvarCertidaoCommand(
    TipoCertidao Tipo,
    SituacaoCertidao Situacao,
    string? Numero,
    DateOnly Emissao,
    DateOnly Validade,
    string? Observacao,
    ArquivoUpload? Arquivo,
    bool RemoverArquivo);

public sealed class SalvarCertidaoCommandValidator : AbstractValidator<SalvarCertidaoCommand>
{
    public SalvarCertidaoCommandValidator(IOptions<DocumentoOptions> options)
    {
        RuleFor(x => x.Tipo).IsInEnum();
        RuleFor(x => x.Situacao).IsInEnum();
        RuleFor(x => x.Numero).MaximumLength(100);
        RuleFor(x => x.Observacao).MaximumLength(1000);
        RuleFor(x => x.Emissao).NotEmpty().WithName("Emissão");
        RuleFor(x => x.Validade).NotEmpty().WithName("Validade")
            .GreaterThanOrEqualTo(x => x.Emissao).WithMessage("A validade não pode ser anterior à emissão.");
        RuleFor(x => x.Arquivo!).SetValidator(new ArquivoUploadValidator(options)).When(x => x.Arquivo is not null)
            .OverridePropertyName("arquivo");
    }
}
