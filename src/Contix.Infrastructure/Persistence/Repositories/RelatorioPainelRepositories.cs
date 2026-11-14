using Contix.Application.Features.Painel;
using Contix.Application.Features.Relatorios;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Contix.Infrastructure.Persistence.Repositories;

/// <summary>Consultas dos relatórios: projeções sem rastreamento, filtradas e limitadas no banco.</summary>
public sealed class RelatorioRepository(ContixDbContext context) : IRelatorioRepository
{
    public async Task<IReadOnlyList<LinhaDocumentoRelatorio>> DocumentosAsync(FiltroRelatorioResolvido f, CancellationToken ct = default)
    {
        var (inicio, fim) = Intervalo(f.De, f.Ate);
        var q = context.Documentos.AsNoTracking().Where(d => d.PublicadoEm >= inicio && d.PublicadoEm < fim);
        if (f.EmpresaId is { } empresaId) q = q.Where(d => d.EmpresaId == empresaId);
        if (f.ResponsavelId is { } usuarioId) q = q.Where(d => d.AdicionadoPorId == usuarioId);

        return await q.OrderByDescending(d => d.PublicadoEm)
            .Take(f.Limite)
            .Select(d => new LinhaDocumentoRelatorio(
                d.Empresa!.NomeFantasia ?? d.Empresa.RazaoSocial, d.Pasta!.Nome, d.Nome, d.Origem, d.Competencia, d.DataVencimento,
                d.PublicadoEm, d.AdicionadoPor!.Nome,
                d.Leituras.Where(l => l.Usuario!.Perfil == PerfilUsuario.Cliente).Min(l => (DateTime?)l.LidoEm)))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LinhaSolicitacaoRelatorio>> SolicitacoesAsync(FiltroRelatorioResolvido f, CancellationToken ct = default)
    {
        var (inicio, fim) = Intervalo(f.De, f.Ate);
        var q = context.Solicitacoes.AsNoTracking().Where(s => s.CreatedAt >= inicio && s.CreatedAt < fim);
        if (f.EmpresaId is { } empresaId) q = q.Where(s => s.EmpresaId == empresaId);
        if (f.ResponsavelId is { } responsavelId) q = q.Where(s => s.ResponsavelId == responsavelId);
        if (f.Departamento is { } departamento) q = q.Where(s => s.Departamento == departamento);
        if (f.StatusSolicitacao is { } status) q = q.Where(s => s.Status == status);
        if (f.TipoSolicitacao is { } tipo) q = q.Where(s => s.Tipo == tipo);

        return await q.OrderByDescending(s => s.Numero)
            .Take(f.Limite)
            .Select(s => new LinhaSolicitacaoRelatorio(
                s.Numero, s.Empresa!.NomeFantasia ?? s.Empresa.RazaoSocial, s.Tipo, s.Assunto, s.Departamento, s.Status,
                s.Solicitante!.Nome, s.Responsavel != null ? s.Responsavel.Nome : null, s.CreatedAt, s.Prazo, s.FinalizadaEm))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LinhaTarefaRelatorio>> TarefasAsync(FiltroRelatorioResolvido f, CancellationToken ct = default)
    {
        var q = context.Tarefas.AsNoTracking().Where(t => t.PrazoInterno >= f.De && t.PrazoInterno <= f.Ate);
        if (f.EmpresaId is { } empresaId) q = q.Where(t => t.EmpresaId == empresaId);
        if (f.ResponsavelId is { } responsavelId) q = q.Where(t => t.ResponsavelId == responsavelId);
        if (f.Departamento is { } departamento) q = q.Where(t => t.Departamento == departamento);
        if (f.StatusTarefa is { } status) q = q.Where(t => t.Status == status);

        return await q.OrderBy(t => t.PrazoInterno).ThenBy(t => t.Numero)
            .Take(f.Limite)
            .Select(t => new LinhaTarefaRelatorio(
                t.Numero, t.Empresa!.NomeFantasia ?? t.Empresa.RazaoSocial, t.Titulo, t.Departamento, t.Status,
                t.Responsavel!.Nome, t.PrazoInterno, t.PrazoLegal, t.ConcluidaEm, t.CreatedAt))
            .ToListAsync(ct);
    }

    internal static (DateTime Inicio, DateTime Fim) Intervalo(DateOnly de, DateOnly ate) =>
        (de.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
}

public sealed class PainelRepository(ContixDbContext context) : IPainelRepository
{
    public async Task<PainelDto> CalcularAsync(DateOnly de, DateOnly ate, Departamento? departamento, DateOnly hoje, DateTime agora, CancellationToken ct = default)
    {
        var (inicio, fim) = RelatorioRepository.Intervalo(de, ate);

        // As consultas rodam em sequência no mesmo DbContext (não é thread-safe); cada uma é um COUNT/GROUP BY por índice.
        var clientesAtivos = await context.Empresas.CountAsync(e => e.Situacao == SituacaoEmpresa.Ativa, ct);

        var solicitacoes = context.Solicitacoes.AsNoTracking();
        if (departamento is { } d1) solicitacoes = solicitacoes.Where(s => s.Departamento == d1);
        var porStatus = await solicitacoes.Where(s => s.CreatedAt >= inicio && s.CreatedAt < fim)
            .GroupBy(s => s.Status)
            .Select(g => new ContagemPorStatus(g.Key, g.Count()))
            .ToListAsync(ct);
        var emAberto = solicitacoes.Where(s => s.Status != StatusSolicitacao.Concluida && s.Status != StatusSolicitacao.Cancelada);
        var solicitacoesEmAberto = await emAberto.CountAsync(ct);
        var solicitacoesAtrasadas = await emAberto.CountAsync(s => s.Prazo != null && s.Prazo < hoje, ct);

        var documentosPeriodo = context.Documentos.AsNoTracking()
            .Where(x => x.Origem == OrigemDocumento.Escritorio && x.PublicadoEm >= inicio && x.PublicadoEm < fim);
        var documentosPublicados = await documentosPeriodo.CountAsync(ct);
        var documentosNaoLidos = await documentosPeriodo
            .CountAsync(x => !x.Leituras.Any(l => l.Usuario!.Perfil == PerfilUsuario.Cliente), ct);

        var tarefas = context.Tarefas.AsNoTracking();
        if (departamento is { } d2) tarefas = tarefas.Where(t => t.Departamento == d2);
        var tarefasAbertas = tarefas.Where(t => t.Status == StatusTarefa.Pendente || t.Status == StatusTarefa.EmAndamento);
        var tarefasEmAberto = await tarefasAbertas.CountAsync(ct);
        var tarefasAtrasadas = await tarefasAbertas.CountAsync(t => t.PrazoInterno < hoje, ct);
        var tarefasPeriodo = tarefas.Where(t => t.PrazoInterno >= de && t.PrazoInterno <= ate);
        var tarefasNoPeriodo = await tarefasPeriodo.CountAsync(ct);
        var tarefasConcluidas = await tarefasPeriodo.CountAsync(t => t.Status == StatusTarefa.Concluida, ct);

        // Só a certidão mais recente de cada tipo por empresa conta (as anteriores ficam no histórico).
        var limiteAlerta = hoje.AddDays(Certidao.DiasAlertaVencimento);
        var validades = context.Certidoes.AsNoTracking()
            .Where(c => c.Empresa!.Situacao == SituacaoEmpresa.Ativa)
            .GroupBy(c => new { c.EmpresaId, c.Tipo })
            .Select(g => g.Max(c => c.Validade));
        var cndsAVencer = await validades.CountAsync(v => v >= hoje && v <= limiteAlerta, ct);
        var cndsVencidas = await validades.CountAsync(v => v < hoje, ct);

        return new PainelDto(de, ate, departamento, clientesAtivos,
            porStatus.OrderBy(p => p.Status).ToList(), porStatus.Sum(p => p.Total), solicitacoesEmAberto, solicitacoesAtrasadas,
            documentosPublicados, documentosNaoLidos,
            tarefasNoPeriodo, tarefasConcluidas, tarefasEmAberto, tarefasAtrasadas,
            cndsAVencer, cndsVencidas, agora);
    }
}
