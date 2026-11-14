using Contix.Application.Features.Equipe;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence.Repositories;

public sealed class EquipeRepository(ContixDbContext context) : IEquipeRepository
{
    public Task<List<DepartamentoEscritorio>> ListarDepartamentosAsync(CancellationToken ct = default) =>
        context.Departamentos.ToListAsync(ct);

    public async Task AdicionarDepartamentoAsync(DepartamentoEscritorio departamento, CancellationToken ct = default) =>
        await context.Departamentos.AddAsync(departamento, ct);

    public async Task<IReadOnlyDictionary<Departamento, int>> ContarColaboradoresAsync(CancellationToken ct = default)
    {
        // Poucos usuários por escritório: agrega em memória só a coluna de departamentos.
        var listas = await context.Usuarios.AsNoTracking()
            .Where(u => u.Ativo && u.Perfil != PerfilUsuario.Cliente)
            .Select(u => u.Departamentos)
            .ToListAsync(ct);
        return listas.SelectMany(l => l.Distinct()).GroupBy(d => d).ToDictionary(g => g.Key, g => g.Count());
    }

    public Task<List<ResponsavelEmpresa>> ListarResponsaveisAsync(Guid empresaId, CancellationToken ct = default) =>
        context.ResponsaveisEmpresa.Include(r => r.Usuario).Where(r => r.EmpresaId == empresaId).ToListAsync(ct);

    public async Task AdicionarResponsavelAsync(ResponsavelEmpresa responsavel, CancellationToken ct = default) =>
        await context.ResponsaveisEmpresa.AddAsync(responsavel, ct);

    // Soft delete: o índice único por empresa e departamento só considera os vínculos não excluídos.
    public void RemoverResponsavel(ResponsavelEmpresa responsavel) => context.ResponsaveisEmpresa.Remove(responsavel);

    public async Task<(Guid Id, string Nome)?> ObterResponsavelAtivoAsync(Guid empresaId, Departamento departamento, CancellationToken ct = default)
    {
        var r = await context.ResponsaveisEmpresa.AsNoTracking()
            .Where(x => x.EmpresaId == empresaId && x.Departamento == departamento && x.Usuario!.Ativo)
            .Select(x => new { x.UsuarioId, x.Usuario!.Nome })
            .FirstOrDefaultAsync(ct);
        return r is null ? null : (r.UsuarioId, r.Nome);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ListarColaboradoresAtivosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        return await context.Usuarios.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.Ativo && u.Perfil != PerfilUsuario.Cliente)
            .ToDictionaryAsync(u => u.Id, u => u.Nome, ct);
    }
}
