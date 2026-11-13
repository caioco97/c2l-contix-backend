using Contix.Application.Common.Models;
using Contix.Application.Features.Auth;
using Contix.Application.Features.Notificacoes;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence.Repositories;

public sealed class NotificacaoRepository(ContixDbContext context) : Repository<Notificacao>(context), INotificacaoRepository
{
    public Task<PagedResult<NotificacaoDto>> PesquisarAsync(Guid usuarioId, NotificacaoFiltro filtro, CancellationToken ct = default)
    {
        var q = Set.AsNoTracking().Where(n => n.UsuarioId == usuarioId);
        if (filtro.SomenteNaoLidas) q = q.Where(n => n.LidaEm == null);
        return q.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Select(n => new NotificacaoDto(n.Id, n.Tipo, n.Titulo, n.Mensagem, n.Link, n.EmpresaId, n.ReferenciaId,
                n.LidaEm != null, n.LidaEm, n.CreatedAt))
            .ToPagedAsync(filtro, ct);
    }

    public Task<int> ContarNaoLidasAsync(Guid usuarioId, CancellationToken ct = default) =>
        Set.CountAsync(n => n.UsuarioId == usuarioId && n.LidaEm == null, ct);

    public Task<int> MarcarTodasComoLidasAsync(Guid usuarioId, DateTime agora, CancellationToken ct = default) =>
        Set.Where(n => n.UsuarioId == usuarioId && n.LidaEm == null)
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.LidaEm, agora).SetProperty(n => n.UpdatedAt, agora), ct);

    private IQueryable<Usuario> UsuariosAtivos(Guid tenantId) =>
        Context.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.TenantId == tenantId && !u.IsDeleted && u.Ativo);

    private static async Task<IReadOnlyList<Destinatario>> Projetar(IQueryable<Usuario> q, CancellationToken ct) =>
        (await q.Select(u => new { u.Id, u.Nome, u.Email, u.Perfil }).ToListAsync(ct))
        .Select(u => new Destinatario(u.Id, u.Nome, u.Email, u.Perfil != PerfilUsuario.Cliente))
        .ToList();

    public Task<IReadOnlyList<Destinatario>> ListarClientesDaEmpresaAsync(Guid tenantId, Guid empresaId, ModuloPortal? modulo = null, CancellationToken ct = default) =>
        Projetar(UsuariosAtivos(tenantId).Where(u => u.Perfil == PerfilUsuario.Cliente
            && u.Empresas.Any(e => e.EmpresaId == empresaId && (modulo == null || e.Modulos.Contains(modulo.Value)))), ct);

    public async Task<IReadOnlyList<Destinatario>> ListarClientesComAcessoAPastaAsync(Guid tenantId, Guid empresaId, Guid pastaId, CancellationToken ct = default)
    {
        var pais = await Context.Pastas.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.EmpresaId == empresaId)
            .Select(p => new { p.Id, p.PastaPaiId })
            .ToDictionaryAsync(p => p.Id, p => p.PastaPaiId, ct);
        var raiz = pastaId;
        for (var i = 0; i < 32 && pais.TryGetValue(raiz, out var pai) && pai is { } idPai; i++) raiz = idPai;

        return await Projetar(UsuariosAtivos(tenantId).Where(u => u.Perfil == PerfilUsuario.Cliente
            && u.Empresas.Any(e => e.EmpresaId == empresaId && e.Modulos.Contains(ModuloPortal.Documentos)
                                   && (e.TodasAsPastas || e.PastaIds.Contains(raiz)))), ct);
    }

    public Task<IReadOnlyList<Destinatario>> ListarEquipeDoEscritorioAsync(Guid tenantId, CancellationToken ct = default) =>
        Projetar(UsuariosAtivos(tenantId).Where(u => u.Perfil != PerfilUsuario.Cliente), ct);

    public Task<IReadOnlyList<Destinatario>> ListarPorIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> usuarioIds, CancellationToken ct = default) =>
        Projetar(UsuariosAtivos(tenantId).Where(u => usuarioIds.Contains(u.Id)), ct);

    public async Task<IReadOnlyList<PreferenciaNotificacao>> ListarPreferenciasAsync(IReadOnlyCollection<Guid> usuarioIds, CancellationToken ct = default) =>
        await Context.PreferenciasNotificacao.AsNoTracking().Where(p => usuarioIds.Contains(p.UsuarioId)).ToListAsync(ct);

    public async Task SalvarPreferenciasAsync(Guid usuarioId, IReadOnlyList<PreferenciaItem> preferencias, CancellationToken ct = default)
    {
        var atuais = await Context.PreferenciasNotificacao.Where(p => p.UsuarioId == usuarioId).ToDictionaryAsync(p => p.Tipo, ct);
        foreach (var item in preferencias)
        {
            if (atuais.TryGetValue(item.Tipo, out var existente)) existente.ReceberEmail = item.ReceberEmail;
            else Context.PreferenciasNotificacao.Add(new PreferenciaNotificacao { UsuarioId = usuarioId, Tipo = item.Tipo, ReceberEmail = item.ReceberEmail });
        }
    }

    public async Task<IReadOnlyList<DocumentoParaLembrete>> ListarDocumentosParaLembreteAsync(DateOnly hoje, int limite, CancellationToken ct = default)
    {
        // Consulta entre tenants (job sem usuário autenticado): ignora o filtro global e aplica soft delete manualmente.
        var q =
            from d in Context.Documentos.IgnoreQueryFilters()
            join e in Context.Empresas.IgnoreQueryFilters() on d.EmpresaId equals e.Id
            join o in Context.Escritorios on d.TenantId equals o.Id
            where !d.IsDeleted && !e.IsDeleted && o.Ativo
                  && o.DiasLembreteVencimento > 0
                  && d.LembreteVencimentoEnviadoEm == null
                  && d.DataVencimento != null
                  && d.DataVencimento >= hoje
                  && d.DataVencimento <= hoje.AddDays(o.DiasLembreteVencimento)
            orderby d.DataVencimento, d.Id
            select new DocumentoParaLembrete(d.Id, d.TenantId, d.EmpresaId, e.NomeFantasia ?? e.RazaoSocial, d.PastaId,
                d.Nome, d.DataVencimento!.Value);
        return await q.AsNoTracking().Take(limite).ToListAsync(ct);
    }

    public async Task MarcarLembreteEnviadoAsync(IReadOnlyCollection<Guid> documentoIds, DateTime agora, CancellationToken ct = default)
    {
        // Rastreado (e não ExecuteUpdate) para gravar na mesma transação das notificações.
        var docs = await Context.Documentos.IgnoreQueryFilters().Where(d => documentoIds.Contains(d.Id)).ToListAsync(ct);
        foreach (var d in docs) d.LembreteVencimentoEnviadoEm = agora;
    }

    public Task<Escritorio?> ObterEscritorioAsync(Guid tenantId, CancellationToken ct = default) =>
        Context.Escritorios.AsNoTracking().FirstOrDefaultAsync(e => e.Id == tenantId, ct);
}

public sealed class TokenRedefinicaoSenhaRepository(ContixDbContext context)
    : Repository<TokenRedefinicaoSenha>(context), ITokenRedefinicaoSenhaRepository
{
    public Task<TokenRedefinicaoSenha?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<TokenRedefinicaoSenha>> ListarValidosDoUsuarioAsync(Guid usuarioId, DateTime agora, CancellationToken ct = default) =>
        await Set.Where(t => t.UsuarioId == usuarioId && t.UsadoEm == null && t.InvalidadoEm == null && t.ExpiraEm > agora).ToListAsync(ct);
}
