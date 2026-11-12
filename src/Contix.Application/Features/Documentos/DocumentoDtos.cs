using Contix.Application.Common.Models;
using FluentValidation;

namespace Contix.Application.Features.Documentos;

public sealed record DocumentoDto(
    Guid Id, Guid EmpresaId, Guid PastaId, string Nome, string NomeArquivoOriginal,
    string ContentType, long TamanhoBytes, string? Comentario, DateOnly? Competencia,
    DateOnly? DataVencimento, DateTime PublicadoEm, Guid AdicionadoPorId, string AdicionadoPorNome,
    DateTime? UpdatedAt, bool Lido, DateTime? LidoEm);

public enum StatusLeitura { Todos = 0, NaoLidos = 1, Lidos = 2 }

public sealed record DocumentoFiltro : PageQuery
{
    public Guid? PastaId { get; init; }
    public string? Busca { get; init; }
    public StatusLeitura Status { get; init; } = StatusLeitura.Todos;
}

/// <summary>Documento com vencimento, para o calendário do portal.</summary>
public sealed record VencimentoDto(
    Guid DocumentoId, string Nome, DateOnly DataVencimento, Guid PastaId, string CaminhoPasta, bool Lido);

/// <summary>Arquivo recebido no upload (independente de ASP.NET).</summary>
public sealed record ArquivoUpload(string NomeArquivo, string ContentType, long Tamanho, Func<Stream> AbrirLeitura);

public sealed record UploadDocumentosCommand(
    Guid PastaId,
    string? Comentario,
    DateOnly? Competencia,
    DateOnly? DataVencimento,
    IReadOnlyList<ArquivoUpload> Arquivos);

public sealed record DownloadDocumentosRequest(IReadOnlyList<Guid> DocumentoIds);

public sealed record ArquivoParaDownload(Stream Conteudo, string ContentType, string NomeArquivo);

/// <summary>Regras de upload configuráveis (seção "Documentos" do appsettings).</summary>
public sealed class DocumentoOptions
{
    public const string Secao = "Documentos";
    public int TamanhoMaximoMb { get; set; } = 25;
    public int MaximoArquivosPorUpload { get; set; } = 20;
    public string[] ExtensoesPermitidas { get; set; } =
        [".pdf", ".xml", ".txt", ".csv", ".zip", ".png", ".jpg", ".jpeg", ".doc", ".docx", ".xls", ".xlsx", ".ofx", ".rem", ".ret"];
    public bool ClientePodeExcluirProprios { get; set; } = true;
}

public sealed class UploadDocumentosCommandValidator : AbstractValidator<UploadDocumentosCommand>
{
    public UploadDocumentosCommandValidator(Microsoft.Extensions.Options.IOptions<DocumentoOptions> options)
    {
        var o = options.Value;
        RuleFor(x => x.PastaId).NotEmpty();
        RuleFor(x => x.Comentario).MaximumLength(1000);
        RuleFor(x => x.Arquivos).NotEmpty().WithMessage("Envie ao menos um arquivo.")
            .Must(a => a.Count <= o.MaximoArquivosPorUpload)
            .WithMessage($"Envie no máximo {o.MaximoArquivosPorUpload} arquivos por vez.");
        RuleForEach(x => x.Arquivos).ChildRules(a =>
        {
            a.RuleFor(f => f.Tamanho).GreaterThan(0).WithMessage("Arquivo vazio.")
                .LessThanOrEqualTo(o.TamanhoMaximoMb * 1024L * 1024L)
                .WithMessage($"O arquivo excede o limite de {o.TamanhoMaximoMb} MB.");
            a.RuleFor(f => f.NomeArquivo).NotEmpty().MaximumLength(255)
                .Must(n => o.ExtensoesPermitidas.Contains(Path.GetExtension(n), StringComparer.OrdinalIgnoreCase))
                .WithMessage(f => $"Tipo de arquivo não permitido: {Path.GetExtension(f.NomeArquivo)}");
        });
    }
}

public sealed class DownloadDocumentosRequestValidator : AbstractValidator<DownloadDocumentosRequest>
{
    public DownloadDocumentosRequestValidator() =>
        RuleFor(x => x.DocumentoIds).NotEmpty().Must(i => i.Count <= 200).WithMessage("Selecione no máximo 200 documentos.");
}
