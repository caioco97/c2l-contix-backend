using Contix.Application.Features.Notificacoes;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence.Repositories;

/// <summary>
/// Consultas dos avisos automáticos. Rodam também fora de uma requisição (jobs), então ignoram o
/// filtro global e aplicam tenant e exclusão lógica explicitamente.
/// </summary>
public sealed class AvisoRepository(ContixDbContext context) : IAvisoRepository
{
    private IQueryable<Noticia> NoticiasPendentes(DateTime agora) =>
        from n in context.Noticias.IgnoreQueryFilters().Include(n => n.Empresas)
        join o in context.Escritorios on n.TenantId equals o.Id
        where !n.IsDeleted && o.Ativo && n.NotificadaEm == null && n.PublicarEm <= agora && (n.ExpiraEm == null || n.ExpiraEm > agora)
        select n;

    public async Task<IReadOnlyList<Noticia>> ListarNoticiasParaAvisarAsync(DateTime agora, int limite, CancellationToken ct = default) =>
        await NoticiasPendentes(agora).OrderBy(n => n.PublicarEm).ThenBy(n => n.Id).Take(limite).ToListAsync(ct);

    public Task<Noticia?> ObterNoticiaParaAvisarAsync(Guid noticiaId, CancellationToken ct = default) =>
        context.Noticias.IgnoreQueryFilters().Include(n => n.Empresas).FirstOrDefaultAsync(n => n.Id == noticiaId && !n.IsDeleted, ct);

    public async Task<IReadOnlyList<DestinatarioNoticia>> ListarDestinatariosNoticiaAsync(Noticia noticia, CancellationToken ct = default)
    {
        var tenantId = noticia.TenantId;
        var empresas = context.Empresas.IgnoreQueryFilters().Where(e => e.TenantId == tenantId && !e.IsDeleted);
        empresas = noticia.Publico switch
        {
            PublicoNoticia.EmpresasSelecionadas => empresas.Where(e => context.NoticiaEmpresas.Any(v => v.NoticiaId == noticia.Id && v.EmpresaId == e.Id)),
            PublicoNoticia.RegimeTributario => FiltrarRegimes(empresas, noticia.Regimes),
            _ => empresas
        };

        var linhas = await (
            from v in context.UsuarioEmpresas
            join e in empresas on v.EmpresaId equals e.Id
            join u in context.Usuarios.IgnoreQueryFilters() on v.UsuarioId equals u.Id
            where u.TenantId == tenantId && !u.IsDeleted && u.Ativo && u.Perfil == PerfilUsuario.Cliente
            orderby e.RazaoSocial
            select new { u.Id, u.Nome, u.Email, EmpresaId = e.Id })
            .AsNoTracking().ToListAsync(ct);

        // Um aviso por usuário, mesmo que ele acesse várias empresas do público.
        return linhas.DistinctBy(l => l.Id)
            .Select(l => new DestinatarioNoticia(new Destinatario(l.Id, l.Nome, l.Email, false), l.EmpresaId))
            .ToList();
    }

    private static IQueryable<Empresa> FiltrarRegimes(IQueryable<Empresa> q, List<RegimeTributario> regimes) =>
        q.Where(e => regimes.Contains(e.RegimeTributario));

    public async Task<IReadOnlyList<CertidaoParaAviso>> ListarCertidoesParaAvisoAsync(DateOnly hoje, int diasVencidaMax, int limite, CancellationToken ct = default)
    {
        var limiteVencida = hoje.AddDays(-diasVencidaMax);
        var em15 = hoje.AddDays(15);
        var em30 = hoje.AddDays(30);
        var certidoes = context.Certidoes.IgnoreQueryFilters();

        var q =
            from c in certidoes
            join e in context.Empresas.IgnoreQueryFilters() on c.EmpresaId equals e.Id
            join o in context.Escritorios on c.TenantId equals o.Id
            where !c.IsDeleted && !e.IsDeleted && o.Ativo
                  // Só a certidão atual do tipo: uma renovação cadastrada encerra os avisos da anterior.
                  && !certidoes.Any(n => !n.IsDeleted && n.EmpresaId == c.EmpresaId && n.Tipo == c.Tipo && n.Validade > c.Validade)
                  && ((c.Validade < hoje && c.Validade >= limiteVencida && c.AvisoVencidaEm == null)
                      || (c.Validade >= hoje && c.Validade <= em15 && c.AvisoVencimento15Em == null)
                      || (c.Validade > em15 && c.Validade <= em30 && c.AvisoVencimento30Em == null))
            orderby c.Validade, c.Id
            select new CertidaoParaAviso(c.Id, c.TenantId, c.EmpresaId, e.NomeFantasia ?? e.RazaoSocial, c.Tipo, c.Validade,
                c.AvisoVencimento30Em != null, c.AvisoVencimento15Em != null, c.AvisoVencidaEm != null);
        return await q.AsNoTracking().Take(limite).ToListAsync(ct);
    }

    public Task<Certidao?> ObterCertidaoAsync(Guid certidaoId, CancellationToken ct = default) =>
        context.Certidoes.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == certidaoId, ct);
}
