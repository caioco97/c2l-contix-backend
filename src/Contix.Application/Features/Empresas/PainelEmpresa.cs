using Contix.Domain.Enums;

namespace Contix.Application.Features.Empresas;

/// <summary>Indicadores da empresa para a página de detalhe do escritório.</summary>
public sealed record PainelEmpresaDto(
    int UsuariosAtivos,
    int ConvitesPendentes,
    // Publicados pelo escritório e que nenhum usuário do cliente abriu ainda.
    int DocumentosNaoVistosPeloCliente,
    // Enviados pelo cliente e que ninguém do escritório abriu ainda.
    int DocumentosRecebidosNaoLidos,
    int SolicitacoesEmAberto,
    int SolicitacoesAtrasadas,
    int CertidoesVencidas,
    int CertidoesAVencer,
    // Tarefas internas do escritório não finalizadas, e as que passaram do prazo interno.
    int TarefasEmAberto,
    int TarefasAtrasadas);

public enum CategoriaHistorico { Documento = 1, Solicitacao = 2, Certidao = 3, Usuario = 4 }

/// <summary>Item da linha do tempo da empresa.</summary>
/// <param name="Tipo">Código do evento dentro da categoria (ex.: Upload, MudancaStatus).</param>
/// <param name="Titulo">Frase pronta para exibir (ex.: "Documento publicado: Folha 10/2026").</param>
/// <param name="ReferenciaId">Documento, solicitação, certidão ou usuário relacionado.</param>
/// <param name="PastaId">Para documentos: pasta, para montar o link.</param>
public sealed record ItemHistoricoEmpresaDto(
    Guid Id, CategoriaHistorico Categoria, string Tipo, DateTime OcorridoEm,
    string? UsuarioNome, bool? EhDoEscritorio, string Titulo, string? Detalhe, Guid? ReferenciaId, Guid? PastaId = null);

/// <param name="ProximoAntesDe">Valor para pedir a próxima página (null quando não há mais itens).</param>
public sealed record HistoricoEmpresaDto(IReadOnlyList<ItemHistoricoEmpresaDto> Itens, DateTime? ProximoAntesDe);

public sealed record HistoricoEmpresaFiltro
{
    /// <summary>Somente eventos anteriores a este instante (paginação por cursor).</summary>
    public DateTime? AntesDe { get; init; }
    public int Limite { get; init; } = 30;
    public CategoriaHistorico? Categoria { get; init; }
}

/* Linhas brutas lidas do banco; o texto é montado no serviço. */
public sealed record EventoDocumentoEmpresa(Guid Id, TipoEventoDocumento Tipo, DateTime OcorridoEm, string UsuarioNome,
    bool EhDoEscritorio, string? Detalhe, Guid DocumentoId, string DocumentoNome, Guid PastaId);

public sealed record EventoSolicitacaoEmpresa(Guid Id, TipoInteracao Tipo, DateTime OcorridoEm, string UsuarioNome,
    bool EhDoEscritorio, string? Mensagem, StatusSolicitacao? StatusAnterior, StatusSolicitacao? StatusNovo,
    Guid SolicitacaoId, int Numero, string Assunto);

public sealed record EventoCertidaoEmpresa(Guid Id, DateTime OcorridoEm, string UsuarioNome, TipoCertidao Tipo,
    SituacaoCertidao Situacao, DateOnly Validade);

public sealed record EventoUsuarioEmpresa(Guid UsuarioId, DateTime OcorridoEm, string Nome, string Email);

public interface IEmpresaPainelRepository
{
    Task<PainelEmpresaDto> ObterPainelAsync(Guid empresaId, DateOnly hoje, CancellationToken ct = default);
    Task<IReadOnlyList<EventoDocumentoEmpresa>> ListarEventosDocumentosAsync(Guid empresaId, DateTime antesDe, int limite, CancellationToken ct = default);
    Task<IReadOnlyList<EventoSolicitacaoEmpresa>> ListarEventosSolicitacoesAsync(Guid empresaId, DateTime antesDe, int limite, CancellationToken ct = default);
    Task<IReadOnlyList<EventoCertidaoEmpresa>> ListarEventosCertidoesAsync(Guid empresaId, DateTime antesDe, int limite, CancellationToken ct = default);
    Task<IReadOnlyList<EventoUsuarioEmpresa>> ListarVinculosUsuariosAsync(Guid empresaId, DateTime antesDe, int limite, CancellationToken ct = default);
}
