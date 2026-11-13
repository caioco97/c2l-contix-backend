using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Certidoes;
using Contix.Application.Features.Solicitacoes;
using Contix.Domain.Enums;

namespace Contix.Application.Features.Empresas;

/// <summary>Painel e linha do tempo da empresa (somente escritório).</summary>
public interface IHistoricoEmpresaService
{
    Task<PainelEmpresaDto> ObterPainelAsync(Guid empresaId, CancellationToken ct = default);
    Task<HistoricoEmpresaDto> ListarHistoricoAsync(Guid empresaId, HistoricoEmpresaFiltro filtro, CancellationToken ct = default);
}

public sealed class HistoricoEmpresaService(
    IEmpresaPainelRepository repositorio,
    IAcessoEmpresaService acesso,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IHistoricoEmpresaService
{
    public const int LimiteMaximo = 100;

    public async Task<PainelEmpresaDto> ObterPainelAsync(Guid empresaId, CancellationToken ct = default)
    {
        await GarantirEscritorioAsync(empresaId, ct);
        return await repositorio.ObterPainelAsync(empresaId, DateOnly.FromDateTime(clock.UtcNow), ct);
    }

    public async Task<HistoricoEmpresaDto> ListarHistoricoAsync(Guid empresaId, HistoricoEmpresaFiltro filtro, CancellationToken ct = default)
    {
        await GarantirEscritorioAsync(empresaId, ct);
        var limite = Math.Clamp(filtro.Limite, 1, LimiteMaximo);
        var antesDe = filtro.AntesDe?.ToUniversalTime() ?? clock.UtcNow.AddSeconds(1);
        bool Incluir(CategoriaHistorico c) => filtro.Categoria is null || filtro.Categoria == c;

        // Cada fonte devolve até "limite + 1" itens; a junção ordenada decide a página.
        var itens = new List<ItemHistoricoEmpresaDto>();
        if (Incluir(CategoriaHistorico.Documento))
            itens.AddRange((await repositorio.ListarEventosDocumentosAsync(empresaId, antesDe, limite + 1, ct)).Select(De));
        if (Incluir(CategoriaHistorico.Solicitacao))
            itens.AddRange((await repositorio.ListarEventosSolicitacoesAsync(empresaId, antesDe, limite + 1, ct)).Select(De));
        if (Incluir(CategoriaHistorico.Certidao))
            itens.AddRange((await repositorio.ListarEventosCertidoesAsync(empresaId, antesDe, limite + 1, ct)).Select(De));
        if (Incluir(CategoriaHistorico.Usuario))
            itens.AddRange((await repositorio.ListarVinculosUsuariosAsync(empresaId, antesDe, limite + 1, ct)).Select(De));

        var ordenados = itens.OrderByDescending(i => i.OcorridoEm).ThenByDescending(i => i.Id).ToList();
        var pagina = ordenados.Take(limite).ToList();
        // Itens no mesmo instante do último da página ficariam de fora com o cursor "antes de":
        // inclui todos eles para não perder nenhum.
        if (ordenados.Count > limite)
            pagina.AddRange(ordenados.Skip(limite).TakeWhile(i => i.OcorridoEm == pagina[^1].OcorridoEm));
        var proximo = ordenados.Count > pagina.Count ? pagina[^1].OcorridoEm : (DateTime?)null;
        return new HistoricoEmpresaDto(pagina, proximo);
    }

    private async Task GarantirEscritorioAsync(Guid empresaId, CancellationToken ct)
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
        await acesso.GarantirAcessoAsync(empresaId, ct);
    }

    private static ItemHistoricoEmpresaDto De(EventoDocumentoEmpresa e) => new(
        e.Id, CategoriaHistorico.Documento, e.Tipo.ToString(), e.OcorridoEm, e.UsuarioNome, e.EhDoEscritorio,
        e.Tipo switch
        {
            TipoEventoDocumento.Upload => e.EhDoEscritorio ? $"Documento publicado: {e.DocumentoNome}" : $"Documento enviado pelo cliente: {e.DocumentoNome}",
            TipoEventoDocumento.Visualizacao => $"Documento visualizado: {e.DocumentoNome}",
            TipoEventoDocumento.Download => $"Documento baixado: {e.DocumentoNome}",
            TipoEventoDocumento.Envio => $"Documento enviado ao cliente: {e.DocumentoNome}",
            TipoEventoDocumento.Exclusao => $"Documento excluído: {e.DocumentoNome}",
            _ => e.DocumentoNome
        },
        e.Detalhe, e.DocumentoId, e.PastaId);

    private static ItemHistoricoEmpresaDto De(EventoSolicitacaoEmpresa e) => new(
        e.Id, CategoriaHistorico.Solicitacao, e.Tipo.ToString(), e.OcorridoEm, e.UsuarioNome, e.EhDoEscritorio,
        e.Tipo switch
        {
            TipoInteracao.Abertura => $"Solicitação #{e.Numero} aberta: {e.Assunto}",
            TipoInteracao.Comentario => $"Mensagem na solicitação #{e.Numero}: {e.Assunto}",
            TipoInteracao.MudancaStatus => $"Solicitação #{e.Numero} {(e.StatusNovo is { } s ? $"passou para {SolicitacaoService.NomeStatus(s)}" : "mudou de status")}",
            TipoInteracao.Atribuicao => $"Solicitação #{e.Numero} atribuída",
            _ => $"Solicitação #{e.Numero}"
        },
        Resumir(e.Mensagem), e.SolicitacaoId);

    private static ItemHistoricoEmpresaDto De(EventoCertidaoEmpresa e) => new(
        e.Id, CategoriaHistorico.Certidao, "Cadastro", e.OcorridoEm, e.UsuarioNome, true,
        $"Certidão {CatalogoCertidoes.Nome(e.Tipo)} cadastrada",
        $"Validade {e.Validade:dd/MM/yyyy}{(e.Situacao == SituacaoCertidao.Positiva ? " (positiva)" : "")}", e.Id);

    private static ItemHistoricoEmpresaDto De(EventoUsuarioEmpresa e) => new(
        e.UsuarioId, CategoriaHistorico.Usuario, "Vinculo", e.OcorridoEm, null, true,
        $"Acesso ao portal liberado para {e.Nome}", e.Email, e.UsuarioId);

    private static string? Resumir(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var limpo = string.Join(' ', texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return limpo.Length <= 160 ? limpo : limpo[..157] + "…";
    }
}
