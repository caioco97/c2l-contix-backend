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
        if (filtro.PrazoDe is { } de) q = q.Where(t => t.PrazoInterno >= de);
        if (filtro.ObrigacaoId is { } obrigacaoId) q = q.Where(t => t.ObrigacaoId == obrigacaoId);
        if (filtro.SolicitacaoId is { } solicitacaoId) q = q.Where(t => t.SolicitacaoId == solicitacaoId);
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

        return Projetar(q, hoje)
            .ToPagedAsync(filtro, ct);
    }

    private static IQueryable<TarefaResumoDto> Projetar(IQueryable<Tarefa> q, DateOnly hoje) =>
        q.Select(t => new TarefaResumoDto(
            t.Id, t.Numero, t.EmpresaId, t.Empresa!.NomeFantasia ?? t.Empresa.RazaoSocial, t.Titulo, t.Departamento,
            t.Status, t.ResponsavelId, t.Responsavel!.Nome, t.PrazoInterno, t.PrazoLegal,
            t.PrazoInterno < hoje && t.Status != StatusTarefa.Concluida && t.Status != StatusTarefa.Cancelada,
            t.PrazoLegal != null && t.PrazoLegal < hoje && t.Status != StatusTarefa.Concluida && t.Status != StatusTarefa.Cancelada,
            t.Interacoes.Count(i => i.Tipo == TipoInteracaoTarefa.Comentario),
            t.Anexos.Count,
            t.CreatedAt, t.UpdatedAt, t.Competencia, t.ObrigacaoId, t.SolicitacaoId));

    public async Task<IReadOnlyList<TarefaResumoDto>> ListarDaSolicitacaoAsync(Guid empresaId, Guid solicitacaoId, DateOnly hoje, CancellationToken ct = default) =>
        await Projetar(Set.AsNoTracking().Where(t => t.EmpresaId == empresaId && t.SolicitacaoId == solicitacaoId)
            .OrderBy(t => t.Numero), hoje).ToListAsync(ct);

    public Task<Tarefa?> ObterCompletaAsync(Guid empresaId, Guid tarefaId, CancellationToken ct = default) =>
        Set.Include(t => t.Empresa)
            .Include(t => t.Responsavel)
            .Include(t => t.CriadoPor)
            .Include(t => t.Solicitacao)
            .Include(t => t.Obrigacao)
            .Include(t => t.Interacoes).ThenInclude(i => i.Autor)
            .Include(t => t.Anexos).ThenInclude(a => a.EnviadoPor)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.EmpresaId == empresaId && t.Id == tarefaId, ct);

    public Task<TarefaAnexo?> ObterAnexoAsync(Guid empresaId, Guid tarefaId, Guid anexoId, CancellationToken ct = default) =>
        Set.AsNoTracking()
            .Where(t => t.EmpresaId == empresaId && t.Id == tarefaId)
            .SelectMany(t => t.Anexos)
            .FirstOrDefaultAsync(a => a.Id == anexoId, ct);

    public Task<int> ProximoNumeroAsync(Guid tenantId, CancellationToken ct = default) => ReservarNumerosAsync(tenantId, 1, ct);

    public async Task<int> ReservarNumerosAsync(Guid tenantId, int quantidade, CancellationToken ct = default)
    {
        if (quantidade < 1) throw new ArgumentOutOfRangeException(nameof(quantidade));
        // UPDATE ... RETURNING bloqueia a linha do escritório: duas criações simultâneas nunca recebem o mesmo número.
        var numeros = await Context.Database
            .SqlQuery<int>($"UPDATE escritorios SET ultimo_numero_tarefa = ultimo_numero_tarefa + {quantidade} WHERE id = {tenantId} RETURNING ultimo_numero_tarefa AS \"Value\"")
            .ToListAsync(ct);
        return numeros.Count == 1 ? numeros[0] - quantidade + 1 : throw new InvalidOperationException("Escritório não encontrado.");
    }
}
