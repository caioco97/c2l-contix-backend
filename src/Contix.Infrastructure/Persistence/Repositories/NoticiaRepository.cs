using Contix.Application.Common.Models;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Noticias;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence.Repositories;

public sealed class NoticiaRepository(ContixDbContext context) : Repository<Noticia>(context), INoticiaRepository
{
    public Task<PagedResult<NoticiaResumoDto>> PesquisarAsync(NoticiaFiltro filtro, DateTime agora, CancellationToken ct = default)
    {
        var q = Set.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var like = QueryableExtensions.LikePattern(filtro.Busca);
            q = q.Where(n => EF.Functions.ILike(n.Titulo, like) || (n.Resumo != null && EF.Functions.ILike(n.Resumo, like)));
        }
        q = filtro.Situacao switch
        {
            SituacaoNoticia.Agendada => q.Where(n => n.PublicarEm > agora),
            SituacaoNoticia.Publicada => q.Where(n => n.PublicarEm <= agora && (n.ExpiraEm == null || n.ExpiraEm > agora)),
            SituacaoNoticia.Expirada => q.Where(n => n.ExpiraEm != null && n.ExpiraEm <= agora),
            _ => q
        };

        q = (filtro.OrderBy?.ToLowerInvariant(), filtro.Desc) switch
        {
            ("titulo", false) => q.OrderBy(n => n.Titulo),
            ("titulo", true) => q.OrderByDescending(n => n.Titulo),
            ("publicarem", false) => q.OrderBy(n => n.PublicarEm),
            _ => q.OrderByDescending(n => n.PublicarEm)
        };

        var empresas = Context.Empresas;
        return q.Select(n => new NoticiaResumoDto(
                n.Id, n.Titulo, n.Resumo ?? n.Conteudo, n.PublicarEm, n.ExpiraEm,
                n.PublicarEm > agora ? SituacaoNoticia.Agendada
                    : n.ExpiraEm != null && n.ExpiraEm <= agora ? SituacaoNoticia.Expirada
                    : SituacaoNoticia.Publicada,
                n.Publico, n.ImagemStorageKey != null, n.Anexos.Count, n.Autor!.Nome,
                n.Publico == PublicoNoticia.Todos ? empresas.Count()
                    : n.Publico == PublicoNoticia.EmpresasSelecionadas ? empresas.Count(e => n.Empresas.Any(v => v.EmpresaId == e.Id))
                    : empresas.Count(e => n.Regimes.Contains(e.RegimeTributario)),
                n.Leituras.Where(l => l.Usuario!.Perfil == PerfilUsuario.Cliente).Select(l => l.EmpresaId).Distinct().Count(),
                n.CreatedAt, n.UpdatedAt))
            .ToPagedAsync(filtro, ct);
    }

    public Task<Noticia?> ObterCompletaAsync(Guid noticiaId, CancellationToken ct = default) =>
        Set.Include(n => n.Autor)
            .Include(n => n.Empresas).ThenInclude(v => v.Empresa)
            .Include(n => n.Anexos)
            .AsSplitQuery()
            .FirstOrDefaultAsync(n => n.Id == noticiaId, ct);

    public Task<PagedResult<NoticiaPortalDto>> PesquisarDaEmpresaAsync(
        Empresa empresa, NoticiaPortalFiltro filtro, Guid usuarioId, DateTime agora, CancellationToken ct = default)
    {
        var empresaId = empresa.Id;
        var q = Visiveis(empresa, agora).AsNoTracking();

        q = filtro.Status switch
        {
            StatusLeitura.Lidos => q.Where(n => n.Leituras.Any(l => l.UsuarioId == usuarioId && l.EmpresaId == empresaId)),
            StatusLeitura.NaoLidos => q.Where(n => !n.Leituras.Any(l => l.UsuarioId == usuarioId && l.EmpresaId == empresaId)),
            _ => q
        };

        return q.OrderByDescending(n => n.PublicarEm)
            .Select(n => new NoticiaPortalDto(
                n.Id, n.Titulo, n.Resumo ?? n.Conteudo, n.PublicarEm, n.ImagemStorageKey != null, n.Anexos.Count,
                n.Leituras.Any(l => l.UsuarioId == usuarioId && l.EmpresaId == empresaId),
                n.Leituras.Where(l => l.UsuarioId == usuarioId && l.EmpresaId == empresaId).Select(l => (DateTime?)l.LidoEm).FirstOrDefault()))
            .ToPagedAsync(filtro, ct);
    }

    public Task<Noticia?> ObterVisivelAsync(Empresa empresa, Guid noticiaId, DateTime agora, CancellationToken ct = default) =>
        Visiveis(empresa, agora).AsNoTracking().Include(n => n.Anexos).FirstOrDefaultAsync(n => n.Id == noticiaId, ct);

    public Task<DateTime?> ObterLeituraAsync(Guid noticiaId, Guid empresaId, Guid usuarioId, CancellationToken ct = default) =>
        Context.NoticiaLeituras
            .Where(l => l.NoticiaId == noticiaId && l.EmpresaId == empresaId && l.UsuarioId == usuarioId)
            .Select(l => (DateTime?)l.LidoEm)
            .FirstOrDefaultAsync(ct);

    public async Task MarcarComoLidaAsync(Guid noticiaId, Guid empresaId, Guid usuarioId, DateTime agora, CancellationToken ct = default)
    {
        var jaLida = await Context.NoticiaLeituras
            .AnyAsync(l => l.NoticiaId == noticiaId && l.EmpresaId == empresaId && l.UsuarioId == usuarioId, ct);
        if (!jaLida)
            await Context.NoticiaLeituras.AddAsync(
                new NoticiaLeitura { NoticiaId = noticiaId, EmpresaId = empresaId, UsuarioId = usuarioId, LidoEm = agora }, ct);
    }

    public async Task<IReadOnlyList<LeituraNoticiaDto>> ListarLeiturasAsync(Noticia noticia, CancellationToken ct = default)
    {
        var selecionadas = noticia.Empresas.Select(v => v.EmpresaId).ToList();
        var regimes = noticia.Regimes;
        var destinatarias = noticia.Publico switch
        {
            PublicoNoticia.EmpresasSelecionadas => Context.Empresas.Where(e => selecionadas.Contains(e.Id)),
            PublicoNoticia.RegimeTributario => Context.Empresas.Where(e => regimes.Contains(e.RegimeTributario)),
            _ => Context.Empresas
        };

        var noticiaId = noticia.Id;
        // Leituras feitas pelo escritório (pré-visualização do portal) não contam como alcance.
        var leituras = Context.NoticiaLeituras.Where(l => l.NoticiaId == noticiaId && l.Usuario!.Perfil == PerfilUsuario.Cliente);

        return await destinatarias.AsNoTracking()
            .OrderBy(e => e.NomeFantasia ?? e.RazaoSocial)
            .Select(e => new LeituraNoticiaDto(
                e.Id, e.NomeFantasia ?? e.RazaoSocial,
                leituras.Any(l => l.EmpresaId == e.Id),
                leituras.Where(l => l.EmpresaId == e.Id).Min(l => (DateTime?)l.LidoEm),
                leituras.Count(l => l.EmpresaId == e.Id)))
            .ToListAsync(ct);
    }

    /// <summary>Publicadas, não expiradas e destinadas à empresa (todas, selecionada ou pelo regime tributário).</summary>
    private IQueryable<Noticia> Visiveis(Empresa empresa, DateTime agora)
    {
        var empresaId = empresa.Id;
        var regime = empresa.RegimeTributario;
        return Set.Where(n => n.PublicarEm <= agora && (n.ExpiraEm == null || n.ExpiraEm > agora)
                              && (n.Publico == PublicoNoticia.Todos
                                  || (n.Publico == PublicoNoticia.EmpresasSelecionadas && n.Empresas.Any(v => v.EmpresaId == empresaId))
                                  || (n.Publico == PublicoNoticia.RegimeTributario && n.Regimes.Contains(regime))));
    }
}
