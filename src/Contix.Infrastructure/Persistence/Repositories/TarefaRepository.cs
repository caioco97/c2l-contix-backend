using Contix.Application.Common.Models;
using Contix.Application.Features.Tarefas;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence.Repositories;

public sealed class TarefaRepository(ContixDbContext context) : Repository<Tarefa>(context), ITarefaRepository
{
    public Task<PagedResult<TarefaResumoDto>> PesquisarAsync(
        TarefaFiltro filtro, Guid? somenteEmpresaId, DateOnly hoje, CancellationToken ct = default)
    {
        var q = Set.AsNoTracking();

        if (somenteEmpresaId is { } empresaId) q = q.Where(t => t.EmpresaId == empresaId);
        if (filtro.Status is { } status) q = q.Where(t => t.Status == status);
        if (filtro.EmAberto == true)
            q = q.Where(t => t.Status != StatusTarefa.Concluida && t.Status != StatusTarefa.Cancelada);
        if (filtro.Departamento is { } departamento) q = q.Where(t => t.Departamento == departamento);
        if (filtro.ResponsavelId is { } responsavelId) q = q.Where(t => t.ResponsavelId == responsavelId);
        if (filtro.Atrasadas == true)
            q = q.Where(t => t.PrazoInterno < hoje && t.Status != StatusTarefa.Concluida && t.Status != StatusTarefa.Cancelada);
        if (filtro.PrazoAte is { } ate) q = q.Where(t => t.PrazoInterno <= ate);
        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var termo = filtro.Busca.Trim().TrimStart('#');
            var numero = int.TryParse(termo, out var n) ? n : (int?)null;
            var like = QueryableExtensions.LikePattern(termo);
            q = q.Where(t => t.Numero == numero || EF.Functions.ILike(t.Titulo, like));
        }

        q = (filtro.OrderBy?.ToLowerInvariant(), filtro.Desc) switch
        {
            ("numero", false) => q.OrderBy(t => t.Numero),
            ("numero", true) => q.OrderByDescending(t => t.Numero),
            ("prazolegal", false) => q.OrderBy(t => t.PrazoLegal == null).ThenBy(t => t.PrazoLegal).ThenBy(t => t.PrazoInterno),
            ("prazolegal", true) => q.OrderByDescending(t => t.PrazoLegal).ThenByDescending(t => t.Numero),
            ("status", false) => q.OrderBy(t => t.Status).ThenBy(t => t.PrazoInterno),
            ("status", true) => q.OrderByDescending(t => t.Status).ThenBy(t => t.PrazoInterno),
            ("createdat", false) => q.OrderBy(t => t.CreatedAt),
            ("createdat", true) => q.OrderByDescending(t => t.CreatedAt),
            ("prazo", true) => q.OrderByDescending(t => t.PrazoInterno).ThenByDescending(t => t.Numero),
            // Padrão: o que vence primeiro aparece em cima.
            _ => q.OrderBy(t => t.PrazoInterno).ThenBy(t => t.PrazoLegal == null).ThenBy(t => t.PrazoLegal).ThenBy(t => t.Numero)
        };

        return q.Select(t => new TarefaResumoDto(
                t.Id, t.Numero, t.EmpresaId, t.Empresa!.NomeFantasia ?? t.Empresa.RazaoSocial, t.Titulo, t.Departamento,
                t.Status, t.ResponsavelId, t.Responsavel!.Nome, t.PrazoInterno, t.PrazoLegal,
                t.PrazoInterno < hoje && t.Status != StatusTarefa.Concluida && t.Status != StatusTarefa.Cancelada,
                t.PrazoLegal != null && t.PrazoLegal < hoje && t.Status != StatusTarefa.Concluida && t.Status != StatusTarefa.Cancelada,
                t.Interacoes.Count(i => i.Tipo == TipoInteracaoTarefa.Comentario),
                t.Anexos.Count,
                t.CreatedAt, t.UpdatedAt))
            .ToPagedAsync(filtro, ct);
    }

    public Task<Tarefa?> ObterCompletaAsync(Guid empresaId, Guid tarefaId, CancellationToken ct = default) =>
        Set.Include(t => t.Empresa)
            .Include(t => t.Responsavel)
            .Include(t => t.CriadoPor)
            .Include(t => t.Interacoes).ThenInclude(i => i.Autor)
            .Include(t => t.Anexos).ThenInclude(a => a.EnviadoPor)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.EmpresaId == empresaId && t.Id == tarefaId, ct);

    public Task<TarefaAnexo?> ObterAnexoAsync(Guid empresaId, Guid tarefaId, Guid anexoId, CancellationToken ct = default) =>
        Set.AsNoTracking()
            .Where(t => t.EmpresaId == empresaId && t.Id == tarefaId)
            .SelectMany(t => t.Anexos)
            .FirstOrDefaultAsync(a => a.Id == anexoId, ct);

    public async Task<int> ProximoNumeroAsync(Guid tenantId, CancellationToken ct = default)
    {
        // UPDATE ... RETURNING bloqueia a linha do escritório: duas criações simultâneas nunca recebem o mesmo número.
        var numeros = await Context.Database
            .SqlQuery<int>($"UPDATE escritorios SET ultimo_numero_tarefa = ultimo_numero_tarefa + 1 WHERE id = {tenantId} RETURNING ultimo_numero_tarefa AS \"Value\"")
            .ToListAsync(ct);
        return numeros.Count == 1 ? numeros[0] : throw new InvalidOperationException("Escritório não encontrado.");
    }
}
