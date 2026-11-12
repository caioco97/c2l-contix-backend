using Contix.Application.Common.Models;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Solicitacoes;
using Contix.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Contix.Application.Features.Noticias;

public sealed record NoticiaAnexoDto(Guid Id, string NomeArquivo, string ContentType, long TamanhoBytes);

public sealed record EmpresaRefDto(Guid Id, string Nome);

/// <summary>Item da lista de notícias do escritório, com o alcance (empresas que leram / destinatárias).</summary>
public sealed record NoticiaResumoDto(
    Guid Id, string Titulo, string Resumo, DateTime PublicarEm, DateTime? ExpiraEm, SituacaoNoticia Situacao,
    PublicoNoticia Publico, bool PossuiImagem, int QuantidadeAnexos, string AutorNome,
    int EmpresasDestinatarias, int EmpresasLeram, DateTime CreatedAt, DateTime? UpdatedAt);

/// <summary>Notícia completa, para edição pelo escritório.</summary>
public sealed record NoticiaDto(
    Guid Id, string Titulo, string? Resumo, string Conteudo, DateTime PublicarEm, DateTime? ExpiraEm,
    SituacaoNoticia Situacao, PublicoNoticia Publico, IReadOnlyList<RegimeTributario> Regimes,
    IReadOnlyList<EmpresaRefDto> Empresas, bool PossuiImagem, IReadOnlyList<NoticiaAnexoDto> Anexos,
    Guid AutorId, string AutorNome, DateTime CreatedAt, DateTime? UpdatedAt);

/// <summary>Notícia como o cliente vê no portal.</summary>
public sealed record NoticiaPortalDto(
    Guid Id, string Titulo, string Resumo, DateTime PublicadaEm, bool PossuiImagem, int QuantidadeAnexos,
    bool Lida, DateTime? LidaEm);

public sealed record NoticiaPortalDetalheDto(
    Guid Id, string Titulo, string Resumo, string Conteudo, DateTime PublicadaEm, bool PossuiImagem,
    IReadOnlyList<NoticiaAnexoDto> Anexos, bool Lida, DateTime? LidaEm);

/// <summary>Leitura de uma notícia por empresa destinatária (acompanhamento do alcance).</summary>
public sealed record LeituraNoticiaDto(Guid EmpresaId, string EmpresaNome, bool Lida, DateTime? PrimeiraLeituraEm, int Leitores);

public sealed record NoticiaFiltro : PageQuery
{
    /// <summary>Busca no título e no resumo.</summary>
    public string? Busca { get; init; }
    public SituacaoNoticia? Situacao { get; init; }
}

public sealed record NoticiaPortalFiltro : PageQuery
{
    public StatusLeitura Status { get; init; } = StatusLeitura.Todos;
}

/// <summary>Criação ou edição de notícia. Na edição, anexos novos são somados aos existentes.</summary>
public sealed record SalvarNoticiaCommand(
    string Titulo,
    string? Resumo,
    string Conteudo,
    DateTime? PublicarEm,
    DateTime? ExpiraEm,
    PublicoNoticia Publico,
    IReadOnlyList<Guid> EmpresaIds,
    IReadOnlyList<RegimeTributario> Regimes,
    ArquivoUpload? Imagem,
    bool RemoverImagem,
    IReadOnlyList<ArquivoUpload> Anexos,
    IReadOnlyList<Guid> AnexosRemovidos);

public sealed class SalvarNoticiaCommandValidator : AbstractValidator<SalvarNoticiaCommand>
{
    public static readonly string[] ExtensoesImagem = [".png", ".jpg", ".jpeg", ".webp", ".gif"];
    public const int TamanhoMaximoImagemMb = 5;

    public SalvarNoticiaCommandValidator(IOptions<DocumentoOptions> options)
    {
        RuleFor(x => x.Titulo).NotEmpty().WithName("Título").MaximumLength(200);
        RuleFor(x => x.Resumo).MaximumLength(500);
        RuleFor(x => x.Conteudo).NotEmpty().WithName("Conteúdo").MaximumLength(20_000);
        RuleFor(x => x.Publico).IsInEnum();
        RuleForEach(x => x.Regimes).IsInEnum();
        RuleFor(x => x.EmpresaIds).Must(e => e.Count <= 1000).WithMessage("Selecione no máximo 1000 empresas.");
        RuleFor(x => x.EmpresaIds).NotEmpty().When(x => x.Publico == PublicoNoticia.EmpresasSelecionadas)
            .WithMessage("Selecione ao menos uma empresa.");
        RuleFor(x => x.Regimes).NotEmpty().When(x => x.Publico == PublicoNoticia.RegimeTributario)
            .WithMessage("Selecione ao menos um regime tributário.");
        RuleFor(x => x.ExpiraEm).GreaterThan(x => x.PublicarEm).When(x => x.ExpiraEm is not null && x.PublicarEm is not null)
            .WithMessage("A expiração deve ser posterior à publicação.");

        When(x => x.Imagem is not null, () =>
        {
            RuleFor(x => x.Imagem!.Tamanho).GreaterThan(0).WithMessage("Imagem vazia.")
                .LessThanOrEqualTo(TamanhoMaximoImagemMb * 1024L * 1024L)
                .WithMessage($"A imagem excede o limite de {TamanhoMaximoImagemMb} MB.")
                .OverridePropertyName("imagem");
            RuleFor(x => x.Imagem!.NomeArquivo)
                .Must(n => ExtensoesImagem.Contains(Path.GetExtension(n), StringComparer.OrdinalIgnoreCase))
                .WithMessage("Envie uma imagem PNG, JPG, WEBP ou GIF.")
                .OverridePropertyName("imagem");
        });

        RuleFor(x => x.Anexos).Must(a => a.Count <= options.Value.MaximoArquivosPorUpload)
            .WithMessage($"Envie no máximo {options.Value.MaximoArquivosPorUpload} arquivos por vez.");
        RuleForEach(x => x.Anexos).SetValidator(new ArquivoUploadValidator(options));
    }
}
