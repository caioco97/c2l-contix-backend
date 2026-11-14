using Contix.Domain.Enums;

namespace Contix.Application.Features.Relatorios;

public enum TipoRelatorio
{
    Documentos = 1,
    Solicitacoes = 2,
    Tarefas = 3
}

public enum FormatoRelatorio
{
    /// <summary>Prévia na tela (primeiras linhas e o resumo).</summary>
    Json = 0,
    Csv = 1,
    Pdf = 2
}

/// <summary>
/// Filtros dos relatórios. O período é aplicado à data de publicação (documentos), de abertura (solicitações)
/// ou ao prazo interno (tarefas). Sem período, vale o mês atual.
/// </summary>
public sealed record RelatorioFiltro
{
    public DateOnly? De { get; init; }
    public DateOnly? Ate { get; init; }
    public Guid? EmpresaId { get; init; }
    public Guid? ResponsavelId { get; init; }
    public Departamento? Departamento { get; init; }
    /// <summary>Status da solicitação ou da tarefa (nome do enum).</summary>
    public string? Status { get; init; }
    public TipoSolicitacao? TipoSolicitacao { get; init; }
    public FormatoRelatorio Formato { get; init; } = FormatoRelatorio.Json;
}

public sealed record ItemResumo(string Rotulo, string Valor);

/// <summary>Relatório já montado para exibição ou exportação.</summary>
/// <param name="Truncado">Passou do limite de linhas: o arquivo traz só as primeiras.</param>
public sealed record RelatorioTabela(
    string Titulo, string Subtitulo, IReadOnlyList<string> Colunas, IReadOnlyList<IReadOnlyList<string>> Linhas,
    IReadOnlyList<ItemResumo> Resumo, int Total, bool Truncado, DateTime GeradoEm);

public sealed record LinhaDocumentoRelatorio(
    string Empresa, string Pasta, string Nome, OrigemDocumento Origem, DateOnly? Competencia, DateOnly? Vencimento,
    DateTime PublicadoEm, string AdicionadoPor, DateTime? PrimeiraLeituraCliente);

public sealed record LinhaSolicitacaoRelatorio(
    int Numero, string Empresa, TipoSolicitacao Tipo, string Assunto, Departamento Departamento, StatusSolicitacao Status,
    string Solicitante, string? Responsavel, DateTime AbertaEm, DateOnly? Prazo, DateTime? FinalizadaEm);

public sealed record LinhaTarefaRelatorio(
    int Numero, string Empresa, string Titulo, Departamento Departamento, StatusTarefa Status, string Responsavel,
    DateOnly PrazoInterno, DateOnly? PrazoLegal, DateTime? ConcluidaEm, DateTime CriadaEm);

/// <summary>Filtro já resolvido (período definido, status convertido).</summary>
public sealed record FiltroRelatorioResolvido(
    DateOnly De, DateOnly Ate, Guid? EmpresaId, Guid? ResponsavelId, Departamento? Departamento,
    StatusSolicitacao? StatusSolicitacao, StatusTarefa? StatusTarefa, TipoSolicitacao? TipoSolicitacao, int Limite);

public interface IRelatorioRepository
{
    Task<IReadOnlyList<LinhaDocumentoRelatorio>> DocumentosAsync(FiltroRelatorioResolvido f, CancellationToken ct = default);
    Task<IReadOnlyList<LinhaSolicitacaoRelatorio>> SolicitacoesAsync(FiltroRelatorioResolvido f, CancellationToken ct = default);
    Task<IReadOnlyList<LinhaTarefaRelatorio>> TarefasAsync(FiltroRelatorioResolvido f, CancellationToken ct = default);
}

/// <summary>Gera o PDF de um relatório tabular.</summary>
public interface IExportadorPdf
{
    byte[] Relatorio(RelatorioTabela tabela);
}
