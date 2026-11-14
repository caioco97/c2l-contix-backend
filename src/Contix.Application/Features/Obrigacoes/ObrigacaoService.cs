using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Notificacoes;
using Contix.Application.Features.Tarefas;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.Obrigacoes;

/// <summary>Modelos de obrigações recorrentes do escritório (CTX-67).</summary>
public interface IObrigacaoService
{
    Task<IReadOnlyList<ObrigacaoDto>> ListarAsync(CancellationToken ct = default);
    Task<ObrigacaoDto> ObterAsync(Guid id, CancellationToken ct = default);
    Task<ObrigacaoDto> CriarAsync(SalvarObrigacaoRequest request, CancellationToken ct = default);
    Task<ObrigacaoDto> AtualizarAsync(Guid id, SalvarObrigacaoRequest request, CancellationToken ct = default);
    /// <summary>Exclui o modelo. As tarefas já geradas são mantidas.</summary>
    Task ExcluirAsync(Guid id, CancellationToken ct = default);
    /// <summary>Gera agora as tarefas da competência (idempotente: empresas que já têm a tarefa são ignoradas).</summary>
    Task<GeracaoTarefasDto> GerarAsync(Guid id, GerarTarefasRequest request, CancellationToken ct = default);
}

public sealed class ObrigacaoService(
    IObrigacaoRepository obrigacoes,
    IGeradorTarefasObrigacao gerador,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IValidator<SalvarObrigacaoRequest> validator) : IObrigacaoService
{
    private DateOnly Hoje => DateOnly.FromDateTime(clock.UtcNow);

    public async Task<IReadOnlyList<ObrigacaoDto>> ListarAsync(CancellationToken ct = default)
    {
        GarantirEscritorio();
        return (await obrigacoes.ListarAsync(ct)).Select(x => ToDto(x.Obrigacao, x.Tarefas, Hoje)).ToList();
    }

    public async Task<ObrigacaoDto> ObterAsync(Guid id, CancellationToken ct = default)
    {
        GarantirEscritorio();
        var o = await CarregarAsync(id, ct);
        return ToDto(o, await obrigacoes.ContarTarefasAsync(id, ct), Hoje);
    }

    public async Task<ObrigacaoDto> CriarAsync(SalvarObrigacaoRequest request, CancellationToken ct = default)
    {
        GarantirAdministrador();
        await validator.ValidateAndThrowAsync(request, ct);
        await ValidarResponsavelAsync(request.ResponsavelPadraoId, ct);

        var o = ObrigacaoRecorrente.Criar(request.Titulo, request.Descricao, request.Departamento, request.Periodicidade,
            request.DiaVencimento, request.MesesAposCompetencia, request.MesReferencia, request.DiasAntecedencia,
            request.Regimes ?? [], request.ResponsavelPadraoId);
        o.TenantId = currentUser.RequireTenantId();
        o.CriadaPorId = currentUser.RequireUserId();
        if (!request.Ativa) Aplicar(o, request);

        await obrigacoes.AddAsync(o, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return await ObterAsync(o.Id, ct);
    }

    public async Task<ObrigacaoDto> AtualizarAsync(Guid id, SalvarObrigacaoRequest request, CancellationToken ct = default)
    {
        GarantirAdministrador();
        await validator.ValidateAndThrowAsync(request, ct);
        var o = await CarregarAsync(id, ct);
        if (request.ResponsavelPadraoId != o.ResponsavelPadraoId) await ValidarResponsavelAsync(request.ResponsavelPadraoId, ct);

        Aplicar(o, request);
        await unitOfWork.SaveChangesAsync(ct);
        return await ObterAsync(id, ct);
    }

    public async Task ExcluirAsync(Guid id, CancellationToken ct = default)
    {
        GarantirAdministrador();
        obrigacoes.Remove(await CarregarAsync(id, ct));
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<GeracaoTarefasDto> GerarAsync(Guid id, GerarTarefasRequest request, CancellationToken ct = default)
    {
        GarantirAdministrador();
        var o = await CarregarAsync(id, ct);
        var competencia = new DateOnly(request.Competencia.Year, request.Competencia.Month, 1);
        if (!o.AplicaNaCompetencia(competencia))
            throw new ValidationException([new ValidationFailure("competencia",
                $"A obrigação não se aplica à competência {competencia:MM/yyyy} pela periodicidade definida.")]);
        return await gerador.GerarAsync(o, competencia, ct);
    }

    private static void Aplicar(ObrigacaoRecorrente o, SalvarObrigacaoRequest r) =>
        o.Definir(r.Titulo, r.Descricao, r.Departamento, r.Periodicidade, r.DiaVencimento, r.MesesAposCompetencia,
            r.MesReferencia, r.DiasAntecedencia, r.Regimes ?? [], r.ResponsavelPadraoId, r.Ativa);

    private async Task ValidarResponsavelAsync(Guid? responsavelId, CancellationToken ct)
    {
        if (responsavelId is { } id && !await obrigacoes.UsuarioAtivoDoEscritorioAsync(currentUser.RequireTenantId(), id, ct))
            throw new ValidationException([new ValidationFailure("responsavelPadraoId", "Selecione um colaborador ativo do escritório.")]);
    }

    private async Task<ObrigacaoRecorrente> CarregarAsync(Guid id, CancellationToken ct) =>
        await obrigacoes.ObterComResponsavelAsync(id, ct) ?? throw new NotFoundException("Obrigação", id);

    private void GarantirEscritorio()
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
    }

    private void GarantirAdministrador()
    {
        if (!currentUser.EhAdministrador) throw new ForbiddenException("Apenas administradores podem gerenciar as obrigações recorrentes.");
    }

    internal static ObrigacaoDto ToDto(ObrigacaoRecorrente o, int tarefas, DateOnly hoje)
    {
        // Próximo vencimento a partir de hoje (até ~13 meses à frente cobre também a periodicidade anual).
        var proxima = o.CompetenciasComVencimentoEntre(hoje, hoje.AddMonths(13)).Cast<DateOnly?>().FirstOrDefault();
        return new ObrigacaoDto(o.Id, o.Titulo, o.Descricao, o.Departamento, o.Periodicidade, o.DiaVencimento,
            o.MesesAposCompetencia, o.MesReferencia, o.DiasAntecedencia, o.Regimes, o.ResponsavelPadraoId,
            o.ResponsavelPadrao?.Nome, o.Ativa, proxima, proxima is { } c ? o.Vencimento(c) : null, tarefas, o.CreatedAt);
    }
}

/// <summary>Gera as tarefas das obrigações recorrentes (chamado pelo job e pela geração manual).</summary>
public interface IGeradorTarefasObrigacao
{
    /// <summary>Gera as tarefas de todas as competências cujo vencimento cai na janela configurada. Retorna quantas criou.</summary>
    Task<int> ProcessarAsync(Guid obrigacaoId, CancellationToken ct = default);
    Task<GeracaoTarefasDto> GerarAsync(ObrigacaoRecorrente obrigacao, DateOnly competencia, CancellationToken ct = default);
}

public sealed class ObrigacoesOptions
{
    public const string Secao = "Obrigacoes";
    public bool Habilitado { get; set; } = true;
    public int IntervaloMinutos { get; set; } = 60;
    /// <summary>Gera as tarefas das competências que vencem nos próximos N dias.</summary>
    public int JanelaGeracaoDias { get; set; } = 45;
    /// <summary>Tarefas gravadas por lote (cada lote reserva a numeração de uma vez).</summary>
    public int TamanhoLote { get; set; } = 500;
}

public sealed class GeradorTarefasObrigacao(
    IObrigacaoRepository obrigacoes,
    ITarefaRepository tarefas,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    INotificador notificador,
    INotificacaoRepository destinatarios,
    Microsoft.Extensions.Options.IOptions<ObrigacoesOptions> options,
    ILogger<GeradorTarefasObrigacao> logger) : IGeradorTarefasObrigacao
{
    public async Task<int> ProcessarAsync(Guid obrigacaoId, CancellationToken ct = default)
    {
        var o = await obrigacoes.ObterIgnorandoTenantAsync(obrigacaoId, ct);
        if (o is null || !o.Ativa) return 0;

        var hoje = DateOnly.FromDateTime(clock.UtcNow);
        var total = 0;
        foreach (var competencia in o.CompetenciasComVencimentoEntre(hoje, hoje.AddDays(Math.Max(1, options.Value.JanelaGeracaoDias))))
            total += (await GerarAsync(o, competencia, ct)).Geradas;
        return total;
    }

    public async Task<GeracaoTarefasDto> GerarAsync(ObrigacaoRecorrente o, DateOnly competencia, CancellationToken ct = default)
    {
        var agora = clock.UtcNow;
        var hoje = DateOnly.FromDateTime(agora);
        var vencimento = o.Vencimento(competencia);

        var empresas = await obrigacoes.ListarEmpresasAplicaveisAsync(o.TenantId, o.Regimes, ct);
        var existentes = await obrigacoes.EmpresasComTarefaAsync(o.TenantId, o.Id, competencia, ct);
        var pendentes = empresas.Where(e => !existentes.Contains(e.Id)).ToList();
        if (pendentes.Count == 0)
            return new GeracaoTarefasDto(competencia, vencimento, empresas.Count, 0, existentes.Count, 0);

        var responsaveis = await obrigacoes.ResponsaveisDoDepartamentoAsync(o.TenantId, o.Departamento, ct);
        Guid? padrao = o.ResponsavelPadraoId is { } p && await obrigacoes.UsuarioAtivoDoEscritorioAsync(o.TenantId, p, ct) ? p : null;

        var comResponsavel = pendentes
            .Select(e => (Empresa: e, Responsavel: responsaveis.TryGetValue(e.Id, out var r) ? r : padrao))
            .Where(x => x.Responsavel is not null)
            .ToList();
        var semResponsavel = pendentes.Count - comResponsavel.Count;

        var titulo = $"{o.Titulo} — {competencia:MM/yyyy}";
        var descricao = (o.Descricao is null ? "" : o.Descricao + "\n\n") +
                        $"Gerada automaticamente pela obrigação recorrente \"{o.Titulo}\" (competência {competencia:MM/yyyy}).";
        var prazoInterno = o.PrazoInterno(competencia, hoje);
        var prazoLegal = vencimento < prazoInterno ? prazoInterno : vencimento;

        var geradasPorResponsavel = new Dictionary<Guid, int>();
        foreach (var lote in comResponsavel.Chunk(Math.Max(1, options.Value.TamanhoLote)))
        {
            // Uma única atualização reserva a numeração do lote inteiro.
            var primeiro = await tarefas.ReservarNumerosAsync(o.TenantId, lote.Length, ct);
            var novas = lote.Select((x, i) =>
            {
                var responsavel = x.Responsavel!.Value;
                var t = Tarefa.Criar(o.TenantId, x.Empresa.Id, primeiro + i, titulo, descricao, o.Departamento, responsavel,
                    prazoInterno, prazoLegal, o.CriadaPorId == Guid.Empty ? responsavel : o.CriadaPorId, agora);
                t.VincularObrigacao(o.Id, competencia);
                geradasPorResponsavel[responsavel] = geradasPorResponsavel.GetValueOrDefault(responsavel) + 1;
                return t;
            }).ToList();
            await tarefas.AddRangeAsync(novas, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }

        // Um aviso por responsável (e não um por tarefa), para não inundar a caixa de notificações.
        if (geradasPorResponsavel.Count > 0)
        {
            var alvo = await destinatarios.ListarPorIdsAsync(o.TenantId, geradasPorResponsavel.Keys.ToList(), ct);
            foreach (var d in alvo)
            {
                var qtd = geradasPorResponsavel[d.Id];
                await notificador.NotificarAsync(o.TenantId, [d], new EventoNotificacao(
                    TipoNotificacao.TarefaAtribuida,
                    qtd == 1 ? $"Nova tarefa: {titulo}" : $"{qtd} tarefas geradas: {titulo}",
                    $"Vencimento legal em {vencimento:dd/MM/yyyy}. Prazo interno: {prazoInterno:dd/MM/yyyy}.",
                    null, o.Id, _ => $"/tarefas?responsavelId={d.Id}&obrigacaoId={o.Id}", "Ver tarefas"), ct);
            }
            await unitOfWork.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Obrigação {ObrigacaoId} competência {Competencia:yyyy-MM}: {Geradas} tarefas geradas, {Existentes} já existentes, {SemResponsavel} sem responsável",
            o.Id, competencia, comResponsavel.Count, existentes.Count, semResponsavel);
        return new GeracaoTarefasDto(competencia, vencimento, empresas.Count, comResponsavel.Count, existentes.Count, semResponsavel);
    }
}
