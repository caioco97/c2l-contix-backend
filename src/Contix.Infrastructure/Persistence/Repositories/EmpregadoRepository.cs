using Contix.Application.Features.Empregados;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence.Repositories;

public sealed class EmpregadoRepository(ContixDbContext context) : Repository<Empregado>(context), IEmpregadoRepository
{
    public async Task<IReadOnlyList<Empregado>> ListarAsync(Guid empresaId, EmpregadoFiltro filtro, CancellationToken ct = default)
    {
        var q = Set.AsNoTracking().Where(e => e.EmpresaId == empresaId);
        if (filtro.Situacao is { } situacao) q = q.Where(e => e.Situacao == situacao);
        if (filtro.Vinculo is { } vinculo) q = q.Where(e => e.Vinculo == vinculo);
        if (filtro.Ativos == true) q = q.Where(e => e.Situacao != SituacaoEmpregado.Desligado);
        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var like = QueryableExtensions.LikePattern(filtro.Busca);
            var digitos = new string(filtro.Busca.Where(char.IsDigit).ToArray());
            q = q.Where(e => EF.Functions.ILike(e.Nome, like)
                             || (e.Matricula != null && EF.Functions.ILike(e.Matricula, like))
                             || (digitos.Length >= 3 && e.Cpf != null && e.Cpf.Contains(digitos)));
        }
        return await q.OrderBy(e => e.Nome).Take(2000).ToListAsync(ct);
    }

    public Task<Empregado?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(e => e.EmpresaId == empresaId && e.Id == id, ct);

    public async Task<IReadOnlyList<Empregado>> ListarPorIdsAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(e => e.EmpresaId == empresaId && ids.Contains(e.Id)).ToListAsync(ct);

    public async Task<IReadOnlyList<Empregado>> ListarAtivosAsync(Guid empresaId, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(e => e.EmpresaId == empresaId && e.Situacao != SituacaoEmpregado.Desligado)
            .OrderBy(e => e.Nome)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Empregado>> ListarPorCpfsOuMatriculasAsync(
        Guid empresaId, IReadOnlyCollection<string> cpfs, IReadOnlyCollection<string> matriculas, CancellationToken ct = default)
    {
        if (cpfs.Count == 0 && matriculas.Count == 0) return [];
        var minusculas = matriculas.Select(m => m.Trim().ToLowerInvariant()).ToList();
        return await Set.AsNoTracking()
            .Where(e => e.EmpresaId == empresaId
                        && ((e.Cpf != null && cpfs.Contains(e.Cpf))
                            || (e.Matricula != null && minusculas.Contains(e.Matricula.Trim().ToLower()))))
            .ToListAsync(ct);
    }

    public Task<Empregado?> ObterPorCpfAsync(Guid empresaId, string cpf, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(e => e.EmpresaId == empresaId && e.Cpf == cpf, ct);
}
