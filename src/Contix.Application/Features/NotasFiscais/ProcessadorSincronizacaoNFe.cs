using System.Security.Cryptography;
using Contix.Application.Common.Abstractions;
using Contix.Application.Features.NotasFiscais.Sefaz;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.NotasFiscais;

/// <summary>Trabalho do job de busca automática: lê a Distribuição DF-e de cada empresa e alimenta a lista de NF-e.</summary>
public interface IProcessadorSincronizacaoNFe
{
    /// <summary>Ids das buscas cuja hora chegou.</summary>
    Task<IReadOnlyList<Guid>> ListarProntasAsync(CancellationToken ct = default);

    /// <summary>Busca as NF-e de uma empresa (até esvaziar a fila da SEFAZ ou atingir o limite de consultas).</summary>
    /// <returns>Quantidade de documentos recebidos.</returns>
    Task<int> ProcessarAsync(Guid sincronizacaoId, CancellationToken ct = default);
}

public sealed class ProcessadorSincronizacaoNFe(
    IIntegracaoSefazRepository repositorio,
    RegistroNotasFiscais registro,
    ISefazNFeGateway sefaz,
    ICriptografiaSegredos cripto,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    ILogger<ProcessadorSincronizacaoNFe> logger) : IProcessadorSincronizacaoNFe
{
    /// <summary>Consultas seguidas por empresa em uma execução (cada uma traz até 50 documentos).</summary>
    public const int MaximoConsultasPorExecucao = 10;
    private const int Lote = 50;
    /// <summary>Sem certificado válido não adianta tentar de novo logo.</summary>
    private static readonly TimeSpan EsperaSemCertificado = TimeSpan.FromHours(12);

    public async Task<IReadOnlyList<Guid>> ListarProntasAsync(CancellationToken ct = default) =>
        sefaz.Habilitado ? await repositorio.ListarProntasAsync(clock.UtcNow, Lote, ct) : [];

    public async Task<int> ProcessarAsync(Guid sincronizacaoId, CancellationToken ct = default)
    {
        if (!sefaz.Habilitado) return 0;
        var sinc = await repositorio.ObterParaProcessarAsync(sincronizacaoId, ct);
        if (sinc is not { Ativa: true, Empresa: not null }) return 0;
        var empresa = sinc.Empresa;

        var certificado = await repositorio.ObterCertificadoAsync(empresa.Id, ignorarTenant: true, ct);
        var uf = CodigosUf.Codigo(empresa.Endereco?.Uf);
        string? impedimento = certificado is null ? "A empresa não tem certificado digital A1."
            : certificado.VencidoEm(clock.UtcNow) ? $"O certificado digital venceu em {certificado.ValidoAte:dd/MM/yyyy}."
            : uf is null ? "A empresa não tem UF cadastrada no endereço."
            : empresa.Documento.Length != 14 ? "A empresa não tem CNPJ." : null;

        CertificadoA1? a1 = null;
        if (impedimento is null)
        {
            try { a1 = NotaFiscalService.Abrir(certificado!, cripto); }
            catch (CryptographicException ex)
            {
                logger.LogError(ex, "Não foi possível descriptografar o certificado da empresa {EmpresaId}", empresa.Id);
                impedimento = "Não foi possível abrir o certificado guardado. Envie o certificado novamente.";
            }
        }
        if (impedimento is not null)
        {
            sinc.RegistrarFalha(impedimento, clock.UtcNow);
            sinc.ProximaConsultaEm = clock.UtcNow + EsperaSemCertificado;
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogWarning("Busca de NF-e da empresa {EmpresaId} suspensa: {Motivo}", empresa.Id, impedimento);
            return 0;
        }

        var recebidos = 0;
        for (var i = 0; i < MaximoConsultasPorExecucao && !ct.IsCancellationRequested; i++)
        {
            RetornoDistribuicao retorno;
            try
            {
                retorno = await sefaz.DistribuicaoPorNsuAsync(a1!, empresa.Documento, uf!.Value, sinc.UltimoNsu, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Falha de comunicação com a SEFAZ na busca de NF-e da empresa {EmpresaId}", empresa.Id);
                sinc.RegistrarFalha("Não foi possível falar com a SEFAZ. Uma nova tentativa será feita em 15 minutos.", clock.UtcNow);
                await unitOfWork.SaveChangesAsync(ct);
                break;
            }

            var resumosNovos = new List<NotaFiscal>();
            foreach (var doc in retorno.Documentos.OrderBy(d => d.Nsu))
            {
                DocumentoNFe lido;
                try { lido = LeitorXmlNFe.Ler(doc.Xml); }
                catch (FormatException ex)
                {
                    logger.LogInformation("Documento NSU {Nsu} ({Schema}) da empresa {EmpresaId} ignorado: {Motivo}", doc.Nsu, doc.Schema, empresa.Id, ex.Message);
                    continue;
                }
                var r = await registro.RegistrarAsync(empresa, lido, doc.Xml, OrigemNotaFiscal.Sefaz, doc.Nsu, ignorarTenant: true, ct);
                if (r is { Resultado: ResultadoRegistroNFe.Criada, Nota: { XmlCompleto: false } nova }) resumosNovos.Add(nova);
            }

            sinc.RegistrarRetorno(retorno.CStat, retorno.Motivo, retorno.UltimoNsu, retorno.MaxNsu, retorno.Documentos.Count, clock.UtcNow);
            try { await unitOfWork.SaveChangesAsync(ct); }
            catch
            {
                await registro.DescartarArquivosAsync();
                throw;
            }
            await registro.ConfirmarAsync();
            recebidos += retorno.Documentos.Count;
            logger.LogInformation("Distribuição DF-e da empresa {EmpresaId}: cStat {CStat} ({Motivo}), {Documentos} documentos, NSU {Ult}/{Max}",
                empresa.Id, retorno.CStat, retorno.Motivo, retorno.Documentos.Count, retorno.UltimoNsu, retorno.MaxNsu);

            if (sinc.CienciaAutomatica && resumosNovos.Count > 0)
                await DarCienciaAsync(a1!, empresa, resumosNovos, ct);

            if (!(retorno.CStat == "138" && sinc.UltimoNsu < sinc.MaxNsu)) break;
        }
        return recebidos;
    }

    /// <summary>Ciência da Operação nas notas novas: a SEFAZ libera o XML completo, que chega numa próxima busca.</summary>
    private async Task DarCienciaAsync(CertificadoA1 a1, Empresa empresa, IReadOnlyList<NotaFiscal> notas, CancellationToken ct)
    {
        foreach (var nota in notas.Where(n => n.Situacao == SituacaoNFe.Autorizada && n.Manifestacao == ManifestacaoNFe.SemManifestacao))
        {
            try
            {
                var r = await sefaz.ManifestarAsync(a1, empresa.Documento, nota.ChaveAcesso, ManifestacaoNFe.Ciencia, null, ct);
                if (!r.Registrado)
                {
                    logger.LogWarning("SEFAZ recusou a ciência automática da NF-e {Chave}: {CStat} {Motivo}", nota.ChaveAcesso, r.CStat, r.Motivo);
                    continue;
                }
                nota.Manifestar(ManifestacaoNFe.Ciencia, null, null, r.Protocolo ?? r.CStat, clock.UtcNow);
                await unitOfWork.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Falha na ciência automática da NF-e {Chave}", nota.ChaveAcesso);
            }
        }
    }
}
