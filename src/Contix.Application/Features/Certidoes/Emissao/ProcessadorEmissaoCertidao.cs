using Contix.Application.Common.Abstractions;
using Contix.Application.Features.Notificacoes;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.Certidoes.Emissao;

/// <summary>Trabalho do job de emissão: agenda as renovações devidas e processa a fila.</summary>
public interface IProcessadorEmissaoCertidao
{
    /// <summary>Coloca na fila as certidões ausentes ou dentro da antecedência das empresas com emissão ativa.</summary>
    /// <returns>Quantidade de emissões criadas.</returns>
    Task<int> AgendarAsync(CancellationToken ct = default);

    /// <summary>Emite as certidões da fila cuja hora chegou.</summary>
    /// <returns>Quantidade de emissões processadas (com sucesso ou não).</returns>
    Task<int> ProcessarFilaAsync(CancellationToken ct = default);
}

public sealed class ProcessadorEmissaoCertidao(
    IEmissaoCertidaoRepository emissoes,
    ICertidaoRepository certidoes,
    INotificacaoRepository destinatarios,
    INotificador notificador,
    IEmissorCertidao emissor,
    IFileStorage storage,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<ProcessadorEmissaoCertidao> logger) : IProcessadorEmissaoCertidao
{
    private const int Lote = 20;
    /// <summary>Depois de uma emissão (mesmo com falha definitiva), o agendamento espera isso para tentar o tipo de novo.</summary>
    public static readonly TimeSpan IntervaloEntreAgendamentos = TimeSpan.FromHours(20);

    public async Task<int> AgendarAsync(CancellationToken ct = default)
    {
        if (!emissor.Habilitado) return 0;
        var agora = clock.UtcNow;
        var hoje = DateOnly.FromDateTime(agora);
        var total = 0;

        foreach (var a in await emissoes.ListarAgendamentosAsync(agora - IntervaloEntreAgendamentos, ct))
        {
            foreach (var tipo in a.Configuracao.Tipos.Where(emissor.TiposSuportados.Contains).Distinct())
            {
                if (a.ComEmissaoRecente.Contains(tipo)) continue;
                var atual = a.Atuais.TryGetValue(tipo, out var c) ? c : ((DateOnly, SituacaoCertidao)?)null;
                if (!a.Configuracao.PrecisaEmitir(atual?.Item1, atual?.Item2, hoje)) continue;

                await emissoes.AddAsync(new EmissaoCertidao
                {
                    TenantId = a.TenantId, EmpresaId = a.EmpresaId, Tipo = tipo,
                    Origem = OrigemEmissaoCertidao.Agendada, ProximaTentativaEm = agora
                }, ct);
                total++;
            }
        }

        if (total > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("Emissões automáticas de CND agendadas: {Quantidade}", total);
        }
        return total;
    }

    public async Task<int> ProcessarFilaAsync(CancellationToken ct = default)
    {
        if (!emissor.Habilitado) return 0;
        var total = 0;
        while (!ct.IsCancellationRequested)
        {
            var prontas = await emissoes.ListarProntasAsync(clock.UtcNow, Lote, ct);
            foreach (var emissao in prontas)
            {
                await ProcessarAsync(emissao, ct);
                total++;
            }
            if (prontas.Count < Lote) break;
        }
        return total;
    }

    private async Task ProcessarAsync(EmissaoCertidao emissao, CancellationToken ct)
    {
        var empresa = emissao.Empresa!;
        ResultadoEmissao resultado;
        if (!emissor.TiposSuportados.Contains(emissao.Tipo))
            resultado = ResultadoEmissao.Falha($"O provedor {emissor.Nome} não emite certidão {CatalogoCertidoes.Nome(emissao.Tipo)}.", false);
        else
        {
            try
            {
                resultado = await emissor.EmitirAsync(emissao.Tipo, new DadosEmpresaEmissao(
                    empresa.Documento, empresa.RazaoSocial, empresa.Endereco?.Uf, empresa.Endereco?.Cidade,
                    empresa.InscricaoEstadual, empresa.InscricaoMunicipal), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Erro ao chamar o emissor de CND para a emissão {EmissaoId}", emissao.Id);
                resultado = ResultadoEmissao.Falha("Não foi possível falar com o serviço de emissão.", true);
            }
        }

        var agora = clock.UtcNow;
        string? gravado = null;
        try
        {
            if (resultado.Certidao is { } emitida)
            {
                var certidao = new Certidao
                {
                    TenantId = emissao.TenantId, EmpresaId = emissao.EmpresaId, Tipo = emissao.Tipo,
                    Situacao = emitida.Situacao, Numero = Limitar(emitida.Numero, 100), Observacao = Limitar(emitida.Observacao, 1000),
                    EmitidaAutomaticamente = true, CadastradoPorId = null
                };
                certidao.DefinirDatas(emitida.Emissao, emitida.Validade);

                gravado = $"{certidao.TenantId:N}/{certidao.EmpresaId:N}/certidoes/{certidao.Id:N}-{Guid.CreateVersion7():N}.pdf";
                using (var conteudo = new MemoryStream(emitida.Pdf, writable: false))
                    await storage.SaveAsync(gravado, conteudo, "application/pdf", ct);
                certidao.StorageKey = gravado;
                certidao.NomeArquivo = emitida.NomeArquivo;
                certidao.ContentType = "application/pdf";
                certidao.TamanhoBytes = emitida.Pdf.LongLength;

                await certidoes.AddAsync(certidao, ct);
                emissao.RegistrarSucesso(certidao.Id, emissor.Nome, emitida.Observacao, agora);
                logger.LogInformation("CND {Tipo} emitida automaticamente para a empresa {EmpresaId} (validade {Validade})",
                    emissao.Tipo, emissao.EmpresaId, certidao.Validade);
            }
            else
            {
                var definitiva = emissao.RegistrarFalha(emissor.Nome, resultado.Erro ?? "Falha não informada pelo provedor.", resultado.Retentavel, agora);
                logger.LogWarning("Falha na emissão de CND {Tipo} da empresa {EmpresaId} (tentativa {Tentativa}, definitiva: {Definitiva}): {Erro}",
                    emissao.Tipo, emissao.EmpresaId, emissao.Tentativas, definitiva, resultado.Erro);
                if (definitiva) await NotificarFalhaAsync(emissao, ct);
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (gravado is not null)
            {
                try { await storage.DeleteAsync(gravado, CancellationToken.None); }
                catch (Exception e) { logger.LogWarning(e, "Falha ao remover arquivo de certidão {StorageKey}", gravado); }
            }
            throw;
        }
    }

    private async Task NotificarFalhaAsync(EmissaoCertidao emissao, CancellationToken ct)
    {
        var config = await emissoes.ObterConfiguracaoIgnorandoTenantAsync(emissao.EmpresaId, ct);
        var alvo = new List<Destinatario>();
        if (emissao.SolicitadoPorId is { } solicitante)
            alvo.AddRange(await destinatarios.ListarPorIdsAsync(emissao.TenantId, [solicitante], ct));
        if (config?.ResponsavelId is { } responsavel)
            alvo.AddRange(await destinatarios.ListarPorIdsAsync(emissao.TenantId, [responsavel], ct));
        if (alvo.Count == 0) alvo.AddRange(await destinatarios.ListarEquipeDoEscritorioAsync(emissao.TenantId, ct));

        var empresa = emissao.Empresa!.NomeFantasia ?? emissao.Empresa.RazaoSocial;
        var tipo = CatalogoCertidoes.Nome(emissao.Tipo);
        await notificador.NotificarAsync(emissao.TenantId, alvo, new EventoNotificacao(
            TipoNotificacao.EmissaoCertidaoFalha,
            $"Falha ao emitir CND: {tipo} — {empresa}",
            $"Não foi possível emitir a certidão {tipo} da empresa {empresa}: {emissao.Mensagem}",
            emissao.EmpresaId, emissao.Id,
            _ => $"/empresas/{emissao.EmpresaId}?aba=cnd",
            "Ver certidões"), ct);
    }

    private static string? Limitar(string? texto, int max) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim() is var t && t.Length > max ? t[..max] : texto.Trim();
}
