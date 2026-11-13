using Contix.Application.Common.Models;
using Contix.Domain.Enums;
using FluentValidation;

namespace Contix.Application.Features.Documentos;

public sealed record DocumentoDto(
    Guid Id, Guid EmpresaId, Guid PastaId, string Nome, string NomeArquivoOriginal,
    string ContentType, long TamanhoBytes, string? Comentario, DateOnly? Competencia,
    DateOnly? DataVencimento, DateTime PublicadoEm, Guid AdicionadoPorId, string AdicionadoPorNome,
    DateTime? UpdatedAt, bool Lido, DateTime? LidoEm,
    OrigemDocumento Origem,
    // Quantos destinatários (cliente, se o escritório publicou; escritório, se o cliente enviou) já abriram ou baixaram.
    int VisualizacoesDestinatarios);

public enum StatusLeitura { Todos = 0, NaoLidos = 1, Lidos = 2 }

public sealed record DocumentoFiltro : PageQuery
{
    public Guid? PastaId { get; init; }
    public string? Busca { get; init; }
    public StatusLeitura Status { get; init; } = StatusLeitura.Todos;
    /// <summary>Mês de competência (qualquer dia do mês; ex.: 2026-10-01).</summary>
    public DateOnly? Competencia { get; init; }
    /// <summary>Somente publicados pelo escritório ou somente enviados pelo cliente.</summary>
    public OrigemDocumento? Origem { get; init; }
}

/// <summary>Situação de acesso de um usuário a um documento (quem visualizou ou baixou).</summary>
public sealed record AcessoDocumentoDto(
    Guid UsuarioId, string Nome, string Email, bool EhDoEscritorio,
    DateTime? VisualizadoEm, // primeira vez que abriu ou baixou
    DateTime? UltimoDownloadEm,
    int Downloads,
    bool Destinatario); // usuário do lado oposto a quem publicou (quem deveria ler)

public sealed record AcessosDocumentoDto(
    Guid DocumentoId, OrigemDocumento Origem, int TotalDestinatarios, int DestinatariosQueVisualizaram,
    IReadOnlyList<AcessoDocumentoDto> Usuarios);

/// <summary>Item do histórico (auditoria) de um documento.</summary>
public sealed record EventoDocumentoDto(
    Guid Id, TipoEventoDocumento Tipo, DateTime OcorridoEm, Guid UsuarioId, string UsuarioNome, bool EhDoEscritorio, string? Detalhe);

/// <summary>Resumo bruto das interações por usuário, calculado no banco.</summary>
public sealed record InteracaoUsuarioDocumento(
    Guid UsuarioId, string Nome, string Email, PerfilUsuario Perfil, DateTime? LidoEm, DateTime? UltimoDownloadEm, int Downloads);

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

/// <summary>Envio de documentos ao cliente: os usuários da empresa recebem e-mail com link para o portal (sem anexo).</summary>
public sealed record EnviarDocumentosRequest(IReadOnlyList<Guid> DocumentoIds, string? Mensagem);

public sealed class EnviarDocumentosRequestValidator : AbstractValidator<EnviarDocumentosRequest>
{
    public EnviarDocumentosRequestValidator()
    {
        RuleFor(x => x.DocumentoIds).NotEmpty().WithMessage("Selecione ao menos um documento.")
            .Must(i => i.Count <= 100).WithMessage("Selecione no máximo 100 documentos.");
        RuleFor(x => x.Mensagem).MaximumLength(2000);
    }
}
