using Contix.Application.Features.Certidoes;
using Contix.Application.Features.Certidoes.Emissao;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence.Repositories;

public sealed class EmissaoCertidaoRepository(ContixDbContext context) : Repository<EmissaoCertidao>(context), IEmissaoCertidaoRepository
{
    public Task<ConfiguracaoEmissaoCertidao?> ObterConfiguracaoAsync(Guid empresaId, CancellationToken ct = default) =>
        Context.ConfiguracoesEmissaoCertidao.FirstOrDefaultAsync(c => c.EmpresaId == empresaId, ct);

    public async Task AdicionarConfiguracaoAsync(ConfiguracaoEmissaoCertidao configuracao, CancellationToken ct = default) =>
        await Context.ConfiguracoesEmissaoCertidao.AddAsync(configuracao, ct);

    public async Task<IReadOnlyList<EmissaoCertidaoDto>> ListarAsync(Guid empresaId, int limite, CancellationToken ct = default)
    {
        var itens = await Set.AsNoTracking()
            .Where(e => e.EmpresaId == empresaId)
            .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            .Take(limite)
            .Select(e => new
            {
                e.Id, e.EmpresaId, e.Tipo, e.Origem, e.Status, e.Tentativas, e.ProximaTentativaEm, e.CreatedAt, e.ConcluidaEm,
                e.Provedor, e.Mensagem, e.CertidaoId, Solicitante = e.SolicitadoPor != null ? e.SolicitadoPor.Nome : null
            })
            .ToListAsync(ct);

        return itens.Select(e => new EmissaoCertidaoDto(
            e.Id, e.EmpresaId, e.Tipo, CatalogoCertidoes.Nome(e.Tipo), e.Origem, e.Status, e.Tentativas,
            e.Status == StatusEmissaoCertidao.Pendente ? e.ProximaTentativaEm : null,
            e.CreatedAt, e.ConcluidaEm, e.Provedor, e.Mensagem, e.CertidaoId, e.Solicitante)).ToList();
    }

    public async Task<IReadOnlySet<TipoCertidao>> TiposEmAbertoAsync(Guid empresaId, CancellationToken ct = default) =>
        (await Set.Where(e => e.EmpresaId == empresaId && e.Status == StatusEmissaoCertidao.Pendente)
            .Select(e => e.Tipo).Distinct().ToListAsync(ct)).ToHashSet();

    public async Task<IReadOnlyList<AgendamentoEmissao>> ListarAgendamentosAsync(DateTime recenteDesde, CancellationToken ct = default)
    {
        var configs = await (
                from c in Context.ConfiguracoesEmissaoCertidao.IgnoreQueryFilters()
                join e in Context.Empresas.IgnoreQueryFilters() on c.EmpresaId equals e.Id
                join o in Context.Escritorios on c.TenantId equals o.Id
                where c.Ativa && !c.IsDeleted && !e.IsDeleted && e.Situacao == SituacaoEmpresa.Ativa && o.Ativo
                select c)
            .AsNoTracking().ToListAsync(ct);
        if (configs.Count == 0) return [];

        var empresaIds = configs.Select(c => c.EmpresaId).ToList();
        var certidoes = await Context.Certidoes.IgnoreQueryFilters().AsNoTracking()
            .Where(c => !c.IsDeleted && empresaIds.Contains(c.EmpresaId))
            .Select(c => new { c.EmpresaId, c.Tipo, c.Validade, c.Emissao, c.CreatedAt, c.Situacao })
            .ToListAsync(ct);
        var recentes = await Set.IgnoreQueryFilters().AsNoTracking()
            .Where(e => !e.IsDeleted && empresaIds.Contains(e.EmpresaId)
                        && (e.Status == StatusEmissaoCertidao.Pendente || e.CreatedAt >= recenteDesde))
            .Select(e => new { e.EmpresaId, e.Tipo })
            .ToListAsync(ct);

        return configs.Select(c => new AgendamentoEmissao(
            c.TenantId, c.EmpresaId, c,
            certidoes.Where(x => x.EmpresaId == c.EmpresaId)
                .GroupBy(x => x.Tipo)
                // A atual é a de validade mais recente (mesma regra da listagem).
                .Select(g => g.OrderByDescending(x => x.Validade).ThenByDescending(x => x.Emissao).ThenByDescending(x => x.CreatedAt).First())
                .ToDictionary(x => x.Tipo, x => (x.Validade, x.Situacao)),
            recentes.Where(r => r.EmpresaId == c.EmpresaId).Select(r => r.Tipo).ToHashSet())).ToList();
    }

    public async Task<IReadOnlyList<EmissaoCertidao>> ListarProntasAsync(DateTime agora, int limite, CancellationToken ct = default) =>
        await Set.IgnoreQueryFilters()
            .Include(e => e.Empresa)
            .Where(e => !e.IsDeleted && e.Status == StatusEmissaoCertidao.Pendente && e.ProximaTentativaEm <= agora)
            .OrderBy(e => e.ProximaTentativaEm).ThenBy(e => e.Id)
            .Take(limite)
            .ToListAsync(ct);

    public Task<ConfiguracaoEmissaoCertidao?> ObterConfiguracaoIgnorandoTenantAsync(Guid empresaId, CancellationToken ct = default) =>
        Context.ConfiguracoesEmissaoCertidao.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId && !c.IsDeleted, ct);
}
