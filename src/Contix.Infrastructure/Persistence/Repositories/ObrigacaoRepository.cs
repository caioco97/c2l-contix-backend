using Contix.Application.Features.Obrigacoes;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence.Repositories;

public sealed class ObrigacaoRepository(ContixDbContext context) : Repository<ObrigacaoRecorrente>(context), IObrigacaoRepository
{
    public async Task<IReadOnlyList<(ObrigacaoRecorrente Obrigacao, int Tarefas)>> ListarAsync(CancellationToken ct = default)
    {
        var lista = await Set.AsNoTracking().Include(o => o.ResponsavelPadrao)
            .OrderBy(o => o.Titulo)
            .Select(o => new { Obrigacao = o, Tarefas = Context.Tarefas.Count(t => t.ObrigacaoId == o.Id) })
            .ToListAsync(ct);
        return lista.Select(x => (x.Obrigacao, x.Tarefas)).ToList();
    }

    public Task<ObrigacaoRecorrente?> ObterComResponsavelAsync(Guid id, CancellationToken ct = default) =>
        Set.Include(o => o.ResponsavelPadrao).FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<int> ContarTarefasAsync(Guid obrigacaoId, CancellationToken ct = default) =>
        Context.Tarefas.CountAsync(t => t.ObrigacaoId == obrigacaoId, ct);

    public async Task<IReadOnlyList<Guid>> ListarAtivasIgnorandoTenantAsync(CancellationToken ct = default) =>
        await Set.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.Ativa && !o.IsDeleted)
            .Select(o => o.Id)
            .ToListAsync(ct);

    public Task<ObrigacaoRecorrente?> ObterIgnorandoTenantAsync(Guid id, CancellationToken ct = default) =>
        Set.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted, ct);

    public async Task<IReadOnlyList<EmpresaParaObrigacao>> ListarEmpresasAplicaveisAsync(
        Guid tenantId, IReadOnlyCollection<RegimeTributario> regimes, CancellationToken ct = default)
    {
        var q = Context.Empresas.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TenantId == tenantId && !e.IsDeleted && e.Situacao == SituacaoEmpresa.Ativa);
        if (regimes.Count > 0) q = q.Where(e => regimes.Contains(e.RegimeTributario));
        return await q.Select(e => new EmpresaParaObrigacao(e.Id, e.NomeFantasia ?? e.RazaoSocial, e.RegimeTributario)).ToListAsync(ct);
    }

    public async Task<IReadOnlySet<Guid>> EmpresasComTarefaAsync(Guid tenantId, Guid obrigacaoId, DateOnly competencia, CancellationToken ct = default) =>
        (await Context.Tarefas.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.ObrigacaoId == obrigacaoId && t.Competencia == competencia && !t.IsDeleted)
            .Select(t => t.EmpresaId)
            .ToListAsync(ct)).ToHashSet();

    public async Task<IReadOnlyDictionary<Guid, Guid>> ResponsaveisDoDepartamentoAsync(Guid tenantId, Departamento departamento, CancellationToken ct = default) =>
        await Context.ResponsaveisEmpresa.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Departamento == departamento && !r.IsDeleted && r.Usuario!.Ativo && !r.Usuario.IsDeleted)
            .ToDictionaryAsync(r => r.EmpresaId, r => r.UsuarioId, ct);

    public Task<bool> UsuarioAtivoDoEscritorioAsync(Guid tenantId, Guid usuarioId, CancellationToken ct = default) =>
        Context.Usuarios.IgnoreQueryFilters().AnyAsync(u =>
            u.Id == usuarioId && u.TenantId == tenantId && !u.IsDeleted && u.Ativo && u.Perfil != PerfilUsuario.Cliente, ct);
}
