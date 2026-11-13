using System.Text.Json;
using Contix.Application.Common.Models;
using Contix.Application.Features.Documentos;
using Contix.Domain.Enums;
using FluentValidation;

namespace Contix.Application.Features.Solicitacoes;

public sealed record SolicitacaoResumoDto(
    Guid Id, int Numero, Guid EmpresaId, string EmpresaNome, TipoSolicitacao Tipo, string TipoNome,
    string Assunto, Departamento Departamento, StatusSolicitacao Status,
    Guid SolicitanteId, string SolicitanteNome, Guid? ResponsavelId, string? ResponsavelNome,
    DateOnly? Prazo, bool Atrasada, DateTime CreatedAt, DateTime? UpdatedAt,
    Guid? EmpregadoId = null, string? EmpregadoNome = null);

public sealed record AnexoDto(Guid Id, string NomeArquivo, string ContentType, long TamanhoBytes, string EnviadoPorNome, DateTime CreatedAt);

public sealed record InteracaoDto(
    Guid Id, TipoInteracao Tipo, string? Mensagem, StatusSolicitacao? StatusAnterior, StatusSolicitacao? StatusNovo,
    Guid AutorId, string AutorNome, bool AutorEhEscritorio, DateTime CreatedAt, IReadOnlyList<AnexoDto> Anexos);

public sealed record SolicitacaoDto(
    Guid Id, int Numero, Guid EmpresaId, string EmpresaNome, TipoSolicitacao Tipo, string TipoNome,
    string Assunto, Departamento Departamento, string? Descricao, JsonElement? Dados,
    Guid? EmpregadoId, string? EmpregadoNome, StatusSolicitacao Status,
    Guid SolicitanteId, string SolicitanteNome, Guid? ResponsavelId, string? ResponsavelNome,
    DateOnly? Prazo, bool Atrasada, DateTime? FinalizadaEm, DateTime CreatedAt, DateTime? UpdatedAt,
    IReadOnlyList<StatusSolicitacao> TransicoesPermitidas,
    IReadOnlyList<InteracaoDto> Historico);

/// <summary>Filtros da listagem. Na rota da empresa, <see cref="EmpresaId"/> é ignorado.</summary>
public sealed record SolicitacaoFiltro : PageQuery
{
    public Guid? EmpresaId { get; init; }
    public TipoSolicitacao? Tipo { get; init; }
    public StatusSolicitacao? Status { get; init; }
    /// <summary>true: só não finalizadas (Aberta, Em análise, Aguardando cliente).</summary>
    public bool? EmAndamento { get; init; }
    public Departamento? Departamento { get; init; }
    public Guid? ResponsavelId { get; init; }
    public bool? SemResponsavel { get; init; }
    public bool? Atrasadas { get; init; }
    /// <summary>Solicitações do empregado (inclusive lançamentos de rubricas em que ele aparece).</summary>
    public Guid? EmpregadoId { get; init; }
    /// <summary>Busca por número ou assunto.</summary>
    public string? Busca { get; init; }
}

public sealed record AbrirSolicitacaoCommand(
    TipoSolicitacao Tipo,
    string? Assunto,
    Departamento? Departamento,
    string? Descricao,
    string? Dados,
    IReadOnlyList<ArquivoUpload> Anexos);

public sealed record ComentarSolicitacaoCommand(string? Mensagem, IReadOnlyList<ArquivoUpload> Anexos);

public sealed record AlterarStatusRequest(StatusSolicitacao Status, string? Mensagem);

public sealed record AtribuirSolicitacaoRequest(Guid? ResponsavelId, DateOnly? Prazo);

public sealed class AbrirSolicitacaoCommandValidator : AbstractValidator<AbrirSolicitacaoCommand>
{
    public AbrirSolicitacaoCommandValidator(Microsoft.Extensions.Options.IOptions<DocumentoOptions> options)
    {
        RuleFor(x => x.Tipo).IsInEnum();
        RuleFor(x => x.Departamento).IsInEnum();
        RuleFor(x => x.Assunto).MaximumLength(200);
        RuleFor(x => x.Descricao).MaximumLength(5000);
        RuleFor(x => x.Dados).MaximumLength(200_000);

        When(x => x.Tipo == TipoSolicitacao.Geral, () =>
        {
            RuleFor(x => x.Assunto).NotEmpty().WithName("Assunto");
            RuleFor(x => x.Departamento).NotNull().WithName("Departamento");
            RuleFor(x => x.Descricao).NotEmpty().WithName("Descrição");
        });

        RuleFor(x => x.Anexos)
            .Must((cmd, anexos) => anexos.Count > 0 || !Enum.IsDefined(cmd.Tipo) || !CatalogoSolicitacoes.Obter(cmd.Tipo).ExigeAnexo)
            .WithMessage(cmd => Enum.IsDefined(cmd.Tipo)
                ? CatalogoSolicitacoes.Obter(cmd.Tipo).OrientacaoAnexo ?? "Anexe ao menos um arquivo."
                : "Anexe ao menos um arquivo.");
        RuleFor(x => x.Anexos).Must(a => a.Count <= options.Value.MaximoArquivosPorUpload)
            .WithMessage($"Envie no máximo {options.Value.MaximoArquivosPorUpload} arquivos por vez.");
        RuleForEach(x => x.Anexos).SetValidator(new ArquivoUploadValidator(options));
    }
}

public sealed class ComentarSolicitacaoCommandValidator : AbstractValidator<ComentarSolicitacaoCommand>
{
    public ComentarSolicitacaoCommandValidator(Microsoft.Extensions.Options.IOptions<DocumentoOptions> options)
    {
        RuleFor(x => x.Mensagem).MaximumLength(5000);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Mensagem) || x.Anexos.Count > 0)
            .OverridePropertyName("mensagem").WithMessage("Escreva uma mensagem ou anexe um arquivo.");
        RuleFor(x => x.Anexos).Must(a => a.Count <= options.Value.MaximoArquivosPorUpload)
            .WithMessage($"Envie no máximo {options.Value.MaximoArquivosPorUpload} arquivos por vez.");
        RuleForEach(x => x.Anexos).SetValidator(new ArquivoUploadValidator(options));
    }
}

public sealed class AlterarStatusRequestValidator : AbstractValidator<AlterarStatusRequest>
{
    public AlterarStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Mensagem).MaximumLength(5000);
    }
}

/// <summary>Mesmas regras de tamanho e extensão do upload de documentos.</summary>
internal sealed class ArquivoUploadValidator : AbstractValidator<ArquivoUpload>
{
    public ArquivoUploadValidator(Microsoft.Extensions.Options.IOptions<DocumentoOptions> options)
    {
        var o = options.Value;
        RuleFor(f => f.Tamanho).GreaterThan(0).WithMessage("Arquivo vazio.")
            .LessThanOrEqualTo(o.TamanhoMaximoMb * 1024L * 1024L)
            .WithMessage($"O arquivo excede o limite de {o.TamanhoMaximoMb} MB.");
        RuleFor(f => f.NomeArquivo).NotEmpty().MaximumLength(255)
            .Must(n => o.ExtensoesPermitidas.Contains(Path.GetExtension(n), StringComparer.OrdinalIgnoreCase))
            .WithMessage(f => $"Tipo de arquivo não permitido: {Path.GetExtension(f.NomeArquivo)}");
    }
}
