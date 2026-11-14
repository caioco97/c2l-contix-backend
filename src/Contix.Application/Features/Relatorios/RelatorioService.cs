using System.Globalization;
using System.Text;
using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Empresas;
using Contix.Application.Features.Equipe;
using Contix.Application.Features.Solicitacoes;
using Contix.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;

namespace Contix.Application.Features.Relatorios;

/// <summary>Relatórios de documentos, solicitações e tarefas (CTX-73) e de solicitações no portal do cliente (CTX-59).</summary>
public interface IRelatorioService
{
    /// <summary>Relatório do escritório (todas as empresas ou uma).</summary>
    Task<RelatorioTabela> GerarAsync(TipoRelatorio tipo, RelatorioFiltro filtro, CancellationToken ct = default);
    /// <summary>Relatório de solicitações de uma empresa (portal do cliente).</summary>
    Task<RelatorioTabela> GerarDaEmpresaAsync(Guid empresaId, RelatorioFiltro filtro, CancellationToken ct = default);
    /// <summary>Converte o relatório no arquivo do formato pedido (CSV ou PDF).</summary>
    ArquivoParaDownload Exportar(RelatorioTabela tabela, FormatoRelatorio formato, string nomeBase);
}

public sealed class RelatorioService(
    IRelatorioRepository repositorio,
    IAcessoEmpresaService acesso,
    IEquipeService equipe,
    IExportadorPdf pdf,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IRelatorioService
{
    public const int LimiteExportacao = 20_000;
    public const int LimitePrevia = 200;
    private static readonly CultureInfo Br = new("pt-BR");

    public async Task<RelatorioTabela> GerarAsync(TipoRelatorio tipo, RelatorioFiltro filtro, CancellationToken ct = default)
    {
        if (currentUser.EhCliente) throw new ForbiddenException();
        if (filtro.EmpresaId is { } empresaId) await acesso.GarantirAcessoAsync(empresaId, ct);
        return await MontarAsync(tipo, filtro, ct);
    }

    public async Task<RelatorioTabela> GerarDaEmpresaAsync(Guid empresaId, RelatorioFiltro filtro, CancellationToken ct = default)
    {
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.Solicitacoes, ct);
        // No portal, o relatório é sempre da empresa da rota e sem filtro por colaborador.
        return await MontarAsync(TipoRelatorio.Solicitacoes, filtro with { EmpresaId = empresaId, ResponsavelId = null }, ct);
    }

    public ArquivoParaDownload Exportar(RelatorioTabela tabela, FormatoRelatorio formato, string nomeBase)
    {
        var nome = $"{nomeBase}-{tabela.GeradoEm:yyyyMMdd-HHmm}";
        return formato switch
        {
            FormatoRelatorio.Csv => new ArquivoParaDownload(new MemoryStream(Csv(tabela)), "text/csv; charset=utf-8", nome + ".csv"),
            FormatoRelatorio.Pdf => new ArquivoParaDownload(new MemoryStream(pdf.Relatorio(tabela)), "application/pdf", nome + ".pdf"),
            _ => throw new ArgumentOutOfRangeException(nameof(formato))
        };
    }

    private async Task<RelatorioTabela> MontarAsync(TipoRelatorio tipo, RelatorioFiltro filtro, CancellationToken ct)
    {
        var f = Resolver(tipo, filtro);
        var nomes = await equipe.ObterNomesAsync(ct);
        var periodo = $"Período: {f.De:dd/MM/yyyy} a {f.Ate:dd/MM/yyyy}";
        var tabela = tipo switch
        {
            TipoRelatorio.Documentos => Documentos(await repositorio.DocumentosAsync(f, ct), periodo, f.Limite),
            TipoRelatorio.Solicitacoes => Solicitacoes(await repositorio.SolicitacoesAsync(f, ct), periodo, f.Limite, nomes),
            TipoRelatorio.Tarefas => Tarefas(await repositorio.TarefasAsync(f, ct), periodo, f.Limite, nomes),
            _ => throw new NotFoundException("Relatório", tipo)
        };
        return filtro.Formato == FormatoRelatorio.Json
            ? tabela with { Linhas = tabela.Linhas.Take(LimitePrevia).ToList() }
            : tabela;
    }

    private FiltroRelatorioResolvido Resolver(TipoRelatorio tipo, RelatorioFiltro filtro)
    {
        var hoje = DateOnly.FromDateTime(clock.UtcNow);
        var de = filtro.De ?? new DateOnly(hoje.Year, hoje.Month, 1);
        var ate = filtro.Ate ?? new DateOnly(de.Year, de.Month, 1).AddMonths(1).AddDays(-1);
        if (ate < de) throw Erro("ate", "A data final deve ser posterior à inicial.");
        if (ate.DayNumber - de.DayNumber > 3 * 366) throw Erro("ate", "O período máximo é de 3 anos.");

        StatusSolicitacao? statusSolicitacao = null;
        StatusTarefa? statusTarefa = null;
        if (!string.IsNullOrWhiteSpace(filtro.Status))
        {
            var ok = tipo switch
            {
                TipoRelatorio.Solicitacoes => Enum.TryParse(filtro.Status, true, out StatusSolicitacao s) && (statusSolicitacao = s) is not null,
                TipoRelatorio.Tarefas => Enum.TryParse(filtro.Status, true, out StatusTarefa t) && (statusTarefa = t) is not null,
                _ => false
            };
            if (!ok) throw Erro("status", "Status inválido para este relatório.");
        }

        // Um a mais que o limite, para saber se o resultado foi cortado.
        var limite = (filtro.Formato == FormatoRelatorio.Json ? LimitePrevia : LimiteExportacao) + 1;
        return new FiltroRelatorioResolvido(de, ate, filtro.EmpresaId, filtro.ResponsavelId, filtro.Departamento,
            statusSolicitacao, statusTarefa, filtro.TipoSolicitacao, limite);
    }

    private RelatorioTabela Documentos(IReadOnlyList<LinhaDocumentoRelatorio> linhas, string periodo, int limite)
    {
        var lista = linhas.Take(limite - 1).ToList();
        var doEscritorio = lista.Where(l => l.Origem == OrigemDocumento.Escritorio).ToList();
        var lidos = doEscritorio.Count(l => l.PrimeiraLeituraCliente is not null);
        return new RelatorioTabela("Relatório de documentos", periodo,
            ["Empresa", "Pasta", "Documento", "Enviado por", "Competência", "Vencimento", "Publicado em", "Lido pelo cliente em"],
            lista.Select(l => (IReadOnlyList<string>)[
                l.Empresa, l.Pasta, l.Nome,
                l.Origem == OrigemDocumento.Escritorio ? $"Escritório ({l.AdicionadoPor})" : $"Cliente ({l.AdicionadoPor})",
                Mes(l.Competencia), Data(l.Vencimento), DataHora(l.PublicadoEm),
                l.Origem == OrigemDocumento.Cliente ? "—" : l.PrimeiraLeituraCliente is { } d ? DataHora(d) : "Não lido"]).ToList(),
            [
                new("Documentos", lista.Count.ToString(Br)),
                new("Publicados pelo escritório", doEscritorio.Count.ToString(Br)),
                new("Enviados pelo cliente", (lista.Count - doEscritorio.Count).ToString(Br)),
                new("Lidos pelo cliente", Percentual(lidos, doEscritorio.Count)),
                new("Não lidos pelo cliente", (doEscritorio.Count - lidos).ToString(Br))
            ], lista.Count, linhas.Count >= limite, clock.UtcNow);
    }

    private RelatorioTabela Solicitacoes(IReadOnlyList<LinhaSolicitacaoRelatorio> linhas, string periodo, int limite, NomesDepartamentos nomes)
    {
        var lista = linhas.Take(limite - 1).ToList();
        var hoje = DateOnly.FromDateTime(clock.UtcNow);
        var finalizadas = lista.Where(l => l.Status == StatusSolicitacao.Concluida && l.FinalizadaEm is not null).ToList();
        var noPrazo = finalizadas.Count(l => l.Prazo is null || DateOnly.FromDateTime(l.FinalizadaEm!.Value) <= l.Prazo);
        var atrasadas = lista.Count(l => l.Status is not (StatusSolicitacao.Concluida or StatusSolicitacao.Cancelada) && l.Prazo < hoje);
        var mediaDias = finalizadas.Count == 0 ? "—"
            : finalizadas.Average(l => (l.FinalizadaEm!.Value - l.AbertaEm).TotalDays).ToString("0.0", Br) + " dias";

        var resumo = new List<ItemResumo> { new("Solicitações", lista.Count.ToString(Br)) };
        resumo.AddRange(lista.GroupBy(l => l.Status).OrderBy(g => g.Key)
            .Select(g => new ItemResumo(SolicitacaoService.NomeStatus(g.Key), g.Count().ToString(Br))));
        resumo.Add(new("Atrasadas (em aberto)", atrasadas.ToString(Br)));
        resumo.Add(new("Tempo médio de atendimento", mediaDias));
        resumo.Add(new("Concluídas no prazo", Percentual(noPrazo, finalizadas.Count)));

        return new RelatorioTabela("Relatório de solicitações", periodo,
            ["Nº", "Empresa", "Tipo", "Assunto", "Departamento", "Status", "Solicitante", "Responsável", "Aberta em", "Prazo", "Finalizada em"],
            lista.Select(l => (IReadOnlyList<string>)[
                "#" + l.Numero, l.Empresa, CatalogoSolicitacoes.Obter(l.Tipo).Nome, l.Assunto, nomes.Nome(l.Departamento),
                SolicitacaoService.NomeStatus(l.Status), l.Solicitante, l.Responsavel ?? "—", DataHora(l.AbertaEm),
                Data(l.Prazo), l.FinalizadaEm is { } f ? DataHora(f) : "—"]).ToList(),
            resumo, lista.Count, linhas.Count >= limite, clock.UtcNow);
    }

    private RelatorioTabela Tarefas(IReadOnlyList<LinhaTarefaRelatorio> linhas, string periodo, int limite, NomesDepartamentos nomes)
    {
        var lista = linhas.Take(limite - 1).ToList();
        var hoje = DateOnly.FromDateTime(clock.UtcNow);
        var concluidas = lista.Where(l => l.Status == StatusTarefa.Concluida && l.ConcluidaEm is not null).ToList();
        var noPrazo = concluidas.Count(l => DateOnly.FromDateTime(l.ConcluidaEm!.Value) <= l.PrazoInterno);
        var atrasadas = lista.Count(l => l.Status is StatusTarefa.Pendente or StatusTarefa.EmAndamento && l.PrazoInterno < hoje);

        return new RelatorioTabela("Relatório de tarefas", periodo,
            ["Nº", "Empresa", "Tarefa", "Departamento", "Status", "Responsável", "Prazo interno", "Prazo legal", "Concluída em"],
            lista.Select(l => (IReadOnlyList<string>)[
                "#" + l.Numero, l.Empresa, l.Titulo, nomes.Nome(l.Departamento), NomeStatus(l.Status, l.PrazoInterno, hoje),
                l.Responsavel, Data(l.PrazoInterno), Data(l.PrazoLegal), l.ConcluidaEm is { } c ? DataHora(c) : "—"]).ToList(),
            [
                new("Tarefas", lista.Count.ToString(Br)),
                new("Concluídas", concluidas.Count.ToString(Br)),
                new("Em aberto", lista.Count(l => l.Status is StatusTarefa.Pendente or StatusTarefa.EmAndamento).ToString(Br)),
                new("Atrasadas", atrasadas.ToString(Br)),
                new("Concluídas no prazo interno", Percentual(noPrazo, concluidas.Count))
            ], lista.Count, linhas.Count >= limite, clock.UtcNow);
    }

    /// <summary>CSV com BOM e ponto e vírgula, que o Excel em português abre direto.</summary>
    internal static byte[] Csv(RelatorioTabela t)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', t.Colunas.Select(Escapar)));
        foreach (var linha in t.Linhas) sb.AppendLine(string.Join(';', linha.Select(Escapar)));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];

        static string Escapar(string valor)
        {
            // Evita injeção de fórmulas ao abrir no Excel.
            if (valor.Length > 0 && valor[0] is '=' or '+' or '-' or '@') valor = "'" + valor;
            return valor.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{valor.Replace("\"", "\"\"")}\"" : valor;
        }
    }

    private static string NomeStatus(StatusTarefa s, DateOnly prazo, DateOnly hoje) => s switch
    {
        StatusTarefa.Pendente or StatusTarefa.EmAndamento when prazo < hoje => "Atrasada",
        StatusTarefa.Pendente => "A fazer",
        StatusTarefa.EmAndamento => "Em andamento",
        StatusTarefa.Concluida => "Concluída",
        StatusTarefa.Cancelada => "Cancelada",
        _ => s.ToString()
    };

    private static string Percentual(int parte, int total) =>
        total == 0 ? "—" : $"{parte} de {total} ({100.0 * parte / total:0}%)";

    private static string Data(DateOnly? d) => d is { } v ? v.ToString("dd/MM/yyyy", Br) : "—";
    private static string Mes(DateOnly? d) => d is { } v ? v.ToString("MM/yyyy", Br) : "—";
    // Datas gravadas em UTC; exibidas no horário de Brasília.
    private static string DataHora(DateTime utc) => Brasilia(utc).ToString("dd/MM/yyyy HH:mm", Br);

    private static DateTime Brasilia(DateTime utc)
    {
        try { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")); }
        catch (TimeZoneNotFoundException) { return utc.AddHours(-3); }
    }

    private static ValidationException Erro(string campo, string msg) => new([new ValidationFailure(campo, msg)]);
}
