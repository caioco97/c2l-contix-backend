using Contix.Application.Common.Models;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Solicitacoes;
using Contix.Domain.Enums;
using FluentValidation;

namespace Contix.Application.Features.Tarefas;

public sealed record TarefaResumoDto(
    Guid Id, int Numero, Guid EmpresaId, string EmpresaNome, string Titulo, Departamento Departamento,
    StatusTarefa Status, Guid ResponsavelId, string ResponsavelNome,
    DateOnly PrazoInterno, DateOnly? PrazoLegal, bool Atrasada, bool PrazoLegalVencido,
    int Comentarios, int Anexos, DateTime CreatedAt, DateTime? UpdatedAt,
    DateOnly? Competencia, Guid? ObrigacaoId, Guid? SolicitacaoId);

public sealed record DocumentoEntregueDto(Guid Id, string Nome, Guid PastaId, DateOnly? DataVencimento);

/// <summary>Solicitação do cliente que originou a tarefa.</summary>
public sealed record SolicitacaoVinculadaDto(Guid Id, int Numero, string Assunto);

public sealed record AnexoTarefaDto(Guid Id, string NomeArquivo, string ContentType, long TamanhoBytes, string EnviadoPorNome, DateTime CreatedAt);

public sealed record InteracaoTarefaDto(
    Guid Id, TipoInteracaoTarefa Tipo, string? Mensagem, StatusTarefa? StatusAnterior, StatusTarefa? StatusNovo,
    Guid AutorId, string AutorNome, DateTime CreatedAt, IReadOnlyList<AnexoTarefaDto> Anexos);

public sealed record TarefaDto(
    Guid Id, int Numero, Guid EmpresaId, string EmpresaNome, string Titulo, string? Descricao, Departamento Departamento,
    StatusTarefa Status, Guid ResponsavelId, string ResponsavelNome, Guid CriadoPorId, string CriadoPorNome,
    DateOnly PrazoInterno, DateOnly? PrazoLegal, bool Atrasada, bool PrazoLegalVencido,
    DateTime? ConcluidaEm, DateTime CreatedAt, DateTime? UpdatedAt,
    IReadOnlyList<StatusTarefa> TransicoesPermitidas,
    IReadOnlyList<InteracaoTarefaDto> Historico,
    SolicitacaoVinculadaDto? Solicitacao,
    Guid? ObrigacaoId, string? ObrigacaoTitulo, DateOnly? Competencia,
    IReadOnlyList<DocumentoEntregueDto> DocumentosEntregues);

/// <summary>Filtros da listagem. Na rota da empresa, <see cref="EmpresaId"/> é ignorado.</summary>
public sealed record TarefaFiltro : PageQuery
{
    public Guid? EmpresaId { get; init; }
    public StatusTarefa? Status { get; init; }
    /// <summary>true: só Pendente e Em andamento.</summary>
    public bool? EmAberto { get; init; }
    public Departamento? Departamento { get; init; }
    public Guid? ResponsavelId { get; init; }
    /// <summary>true: prazo interno vencido e tarefa não finalizada.</summary>
    public bool? Atrasadas { get; init; }
    /// <summary>Prazo interno até esta data (inclusive), ex.: "vencem nesta semana".</summary>
    public DateOnly? PrazoAte { get; init; }
    /// <summary>Prazo interno a partir desta data (inclusive).</summary>
    public DateOnly? PrazoDe { get; init; }
    public Guid? ObrigacaoId { get; init; }
    public Guid? SolicitacaoId { get; init; }
    /// <summary>Busca por número ou título.</summary>
    public string? Busca { get; init; }
}

/// <param name="SolicitacaoId">Solicitação do cliente que originou a tarefa (mantém o vínculo).</param>
public sealed record CriarTarefaCommand(
    string Titulo,
    string? Descricao,
    Departamento Departamento,
    Guid ResponsavelId,
    DateOnly? PrazoInterno,
    DateOnly? PrazoLegal,
    IReadOnlyList<ArquivoUpload> Anexos,
    Guid? SolicitacaoId = null) : IDadosTarefa;

/// <summary>
/// Conclui a tarefa. Com arquivos, publica-os como documentos na pasta da empresa (entrega ao cliente),
/// com competência e vencimento opcionais, e registra a entrega na tarefa.
/// </summary>
public sealed record ConcluirTarefaCommand(
    string? Mensagem,
    Guid? PastaId,
    DateOnly? Competencia,
    DateOnly? DataVencimento,
    IReadOnlyList<ArquivoUpload> Arquivos);

public sealed record EditarTarefaRequest(
    string Titulo,
    string? Descricao,
    Departamento Departamento,
    Guid ResponsavelId,
    DateOnly? PrazoInterno,
    DateOnly? PrazoLegal) : IDadosTarefa;

public sealed record ComentarTarefaCommand(string? Mensagem, IReadOnlyList<ArquivoUpload> Anexos);

public sealed record AlterarStatusTarefaRequest(StatusTarefa Status, string? Mensagem);

/// <summary>Campos comuns à criação e à edição.</summary>
public interface IDadosTarefa
{
    string Titulo { get; }
    string? Descricao { get; }
    Departamento Departamento { get; }
    Guid ResponsavelId { get; }
    DateOnly? PrazoInterno { get; }
    DateOnly? PrazoLegal { get; }
}

internal sealed class DadosTarefaValidator : AbstractValidator<IDadosTarefa>
{
    public DadosTarefaValidator()
    {
        RuleFor(x => x.Titulo).NotEmpty().MaximumLength(200).WithName("Título");
        RuleFor(x => x.Descricao).MaximumLength(5000).WithName("Descrição");
        RuleFor(x => x.Departamento).IsInEnum().WithName("Departamento");
        RuleFor(x => x.ResponsavelId).NotEmpty().WithName("Responsável");
        RuleFor(x => x.PrazoInterno).NotNull().WithName("Prazo interno");
        RuleFor(x => x.PrazoInterno)
            .Must((x, interno) => x.PrazoLegal is not { } legal || interno is not { } i || i <= legal)
            .WithMessage("O prazo interno não pode ser posterior ao prazo legal.");
    }
}

public sealed class CriarTarefaCommandValidator : AbstractValidator<CriarTarefaCommand>
{
    public CriarTarefaCommandValidator(Microsoft.Extensions.Options.IOptions<DocumentoOptions> options)
    {
        Include(new DadosTarefaValidator());
        RuleFor(x => x.Anexos).Must(a => a.Count <= options.Value.MaximoArquivosPorUpload)
            .WithMessage($"Envie no máximo {options.Value.MaximoArquivosPorUpload} arquivos por vez.");
        RuleForEach(x => x.Anexos).SetValidator(new ArquivoUploadValidator(options));
    }
}

public sealed class EditarTarefaRequestValidator : AbstractValidator<EditarTarefaRequest>
{
    public EditarTarefaRequestValidator() => Include(new DadosTarefaValidator());
}

public sealed class ComentarTarefaCommandValidator : AbstractValidator<ComentarTarefaCommand>
{
    public ComentarTarefaCommandValidator(Microsoft.Extensions.Options.IOptions<DocumentoOptions> options)
    {
        RuleFor(x => x.Mensagem).MaximumLength(5000);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Mensagem) || x.Anexos.Count > 0)
            .OverridePropertyName("mensagem").WithMessage("Escreva um comentário ou anexe um arquivo.");
        RuleFor(x => x.Anexos).Must(a => a.Count <= options.Value.MaximoArquivosPorUpload)
            .WithMessage($"Envie no máximo {options.Value.MaximoArquivosPorUpload} arquivos por vez.");
        RuleForEach(x => x.Anexos).SetValidator(new ArquivoUploadValidator(options));
    }
}

public sealed class ConcluirTarefaCommandValidator : AbstractValidator<ConcluirTarefaCommand>
{
    public ConcluirTarefaCommandValidator(Microsoft.Extensions.Options.IOptions<DocumentoOptions> options)
    {
        RuleFor(x => x.Mensagem).MaximumLength(5000);
        RuleFor(x => x.PastaId).NotEmpty().When(x => x.Arquivos.Count > 0)
            .WithMessage("Escolha a pasta da empresa onde publicar os documentos.");
        RuleFor(x => x.Arquivos).Must(a => a.Count <= options.Value.MaximoArquivosPorUpload)
            .WithMessage($"Envie no máximo {options.Value.MaximoArquivosPorUpload} arquivos por vez.");
        RuleForEach(x => x.Arquivos).SetValidator(new ArquivoUploadValidator(options));
    }
}

public sealed class AlterarStatusTarefaRequestValidator : AbstractValidator<AlterarStatusTarefaRequest>
{
    public AlterarStatusTarefaRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Mensagem).MaximumLength(5000);
    }
}
