using Contix.Application.Common.Models;
using Contix.Application.Features.Solicitacoes;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence.Repositories;

public sealed class SolicitacaoRepository(ContixDbContext context) : Repository<Solicitacao>(context), ISolicitacaoRepository
{
    public Task<PagedResult<SolicitacaoResumoDto>> PesquisarAsync(
        SolicitacaoFiltro filtro, Guid? somenteEmpresaId, DateOnly hoje, CancellationToken ct = default)
    {
        var q = Set.AsNoTracking();

        if (somenteEmpresaId is { } empresaId) q = q.Where(s => s.EmpresaId == empresaId);
        if (filtro.Tipo is { } tipo) q = q.Where(s => s.Tipo == tipo);
        if (filtro.Status is { } status) q = q.Where(s => s.Status == status);
        if (filtro.EmAndamento == true)
            q = q.Where(s => s.Status != StatusSolicitacao.Concluida && s.Status != StatusSolicitacao.Cancelada);
        if (filtro.Departamento is { } departamento) q = q.Where(s => s.Departamento == departamento);
        if (filtro.ResponsavelId is { } responsavelId) q = q.Where(s => s.ResponsavelId == responsavelId);
        if (filtro.SemResponsavel == true) q = q.Where(s => s.ResponsavelId == null);
        if (filtro.Atrasadas == true)
            q = q.Where(s => s.Prazo != null && s.Prazo < hoje
                             && s.Status != StatusSolicitacao.Concluida && s.Status != StatusSolicitacao.Cancelada);
        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var termo = filtro.Busca.Trim().TrimStart('#');
            var numero = int.TryParse(termo, out var n) ? n : (int?)null;
            var like = QueryableExtensions.LikePattern(termo);
            q = q.Where(s => s.Numero == numero || EF.Functions.ILike(s.Assunto, like));
        }

        q = (filtro.OrderBy?.ToLowerInvariant(), filtro.Desc) switch
        {
            ("numero", false) => q.OrderBy(s => s.Numero),
            ("numero", true) => q.OrderByDescending(s => s.Numero),
            ("prazo", false) => q.OrderBy(s => s.Prazo == null).ThenBy(s => s.Prazo).ThenBy(s => s.Numero),
            ("prazo", true) => q.OrderByDescending(s => s.Prazo).ThenByDescending(s => s.Numero),
            ("status", false) => q.OrderBy(s => s.Status).ThenByDescending(s => s.Numero),
            ("status", true) => q.OrderByDescending(s => s.Status).ThenByDescending(s => s.Numero),
            ("atualizado", false) => q.OrderBy(s => s.UpdatedAt ?? s.CreatedAt),
            ("atualizado", true) => q.OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt),
            ("createdat", false) => q.OrderBy(s => s.CreatedAt),
            _ => q.OrderByDescending(s => s.CreatedAt)
        };

        return q.Select(s => new SolicitacaoResumoDto(
                s.Id, s.Numero, s.EmpresaId, s.Empresa!.NomeFantasia ?? s.Empresa.RazaoSocial, s.Tipo, string.Empty,
                s.Assunto, s.Departamento, s.Status, s.SolicitanteId, s.Solicitante!.Nome,
                s.ResponsavelId, s.Responsavel != null ? s.Responsavel.Nome : null, s.Prazo,
                s.Prazo != null && s.Prazo < hoje
                    && s.Status != StatusSolicitacao.Concluida && s.Status != StatusSolicitacao.Cancelada,
                s.CreatedAt, s.UpdatedAt))
            .ToPagedAsync(filtro, ct)
            .ContinueWith(t => t.Result with
            {
                Items = t.Result.Items.Select(i => i with { TipoNome = CatalogoSolicitacoes.Obter(i.Tipo).Nome }).ToList()
            }, ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    public Task<Solicitacao?> ObterCompletaAsync(Guid empresaId, Guid solicitacaoId, CancellationToken ct = default) =>
        Set.Include(s => s.Empresa)
            .Include(s => s.Solicitante)
            .Include(s => s.Responsavel)
            .Include(s => s.Interacoes).ThenInclude(i => i.Autor)
            .Include(s => s.Anexos).ThenInclude(a => a.EnviadoPor)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.EmpresaId == empresaId && s.Id == solicitacaoId, ct);

    public Task<SolicitacaoAnexo?> ObterAnexoAsync(Guid empresaId, Guid solicitacaoId, Guid anexoId, CancellationToken ct = default) =>
        Set.AsNoTracking()
            .Where(s => s.EmpresaId == empresaId && s.Id == solicitacaoId)
            .SelectMany(s => s.Anexos)
            .FirstOrDefaultAsync(a => a.Id == anexoId, ct);

    public async Task<int> ProximoNumeroAsync(Guid tenantId, CancellationToken ct = default)
    {
        // UPDATE ... RETURNING bloqueia a linha do escritório: duas aberturas simultâneas nunca recebem o mesmo número.
        var numeros = await Context.Database
            .SqlQuery<int>($"UPDATE escritorios SET ultimo_numero_solicitacao = ultimo_numero_solicitacao + 1 WHERE id = {tenantId} RETURNING ultimo_numero_solicitacao AS \"Value\"")
            .ToListAsync(ct);
        return numeros.Count == 1 ? numeros[0] : throw new InvalidOperationException("Escritório não encontrado.");
    }
}
