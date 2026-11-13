using System.Text;
using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.NotasFiscais;

public enum ResultadoRegistroNFe { Criada = 1, Atualizada = 2, Ignorada = 3 }

public sealed record RegistroNFe(ResultadoRegistroNFe Resultado, NotaFiscal? Nota, string? Motivo);

/// <summary>
/// Grava na lista de NF-e da empresa um documento lido do XML, venha ele da importação manual ou da SEFAZ.
/// Não duplica (chave de acesso única por empresa) e troca o resumo pela nota completa quando ela chega.
/// O chamador salva a unidade de trabalho e, se falhar, chama <see cref="DescartarArquivosAsync"/>.
/// </summary>
public sealed class RegistroNotasFiscais(INotaFiscalRepository notas, IFileStorage storage, ILogger<RegistroNotasFiscais> logger)
{
    private readonly List<string> _gravados = [];
    private readonly List<string> _substituidos = [];

    public async Task<RegistroNFe> RegistrarAsync(
        Empresa empresa, DocumentoNFe documento, string xml, OrigemNotaFiscal origem, long? nsu, bool ignorarTenant, CancellationToken ct)
    {
        switch (documento)
        {
            case EventoLido evento:
                if (evento.TipoEvento != LeitorXmlNFe.EventoCancelamento)
                    return new(ResultadoRegistroNFe.Ignorada, null, "Evento sem efeito na lista de notas.");
                var cancelada = await notas.ObterPorChaveAsync(empresa.Id, evento.ChaveAcesso, ignorarTenant, ct);
                if (cancelada is null || cancelada.Situacao == SituacaoNFe.Cancelada)
                    return new(ResultadoRegistroNFe.Ignorada, cancelada, "Cancelamento de nota que não está na lista.");
                cancelada.Situacao = SituacaoNFe.Cancelada;
                if (nsu is not null) cancelada.Nsu = nsu;
                return new(ResultadoRegistroNFe.Atualizada, cancelada, "NF-e cancelada pelo emitente.");

            case NotaLida lida:
                var d = lida.Dados;
                if (d.DestinatarioDocumento is not null && d.DestinatarioDocumento != empresa.Documento)
                    return new(ResultadoRegistroNFe.Ignorada, null,
                        "A nota não foi emitida contra o CNPJ/CPF desta empresa (o destinatário é outro).");

                var existente = await notas.ObterPorChaveAsync(empresa.Id, d.ChaveAcesso, ignorarTenant, ct);
                if (existente is null)
                {
                    var nova = new NotaFiscal
                    {
                        TenantId = empresa.TenantId, EmpresaId = empresa.Id, ChaveAcesso = d.ChaveAcesso,
                        DestinatarioDocumento = empresa.Documento, Origem = origem, Nsu = nsu
                    };
                    nova.PreencherDaChave();
                    Aplicar(nova, d);
                    nova.XmlCompleto = lida.Completa;
                    nova.XmlStorageKey = await GravarXmlAsync(nova, xml, ct);
                    await notas.AddAsync(nova, ct);
                    return new(ResultadoRegistroNFe.Criada, nova, null);
                }

                if (lida.Completa && !existente.XmlCompleto)
                {
                    Aplicar(existente, d);
                    existente.XmlCompleto = true;
                    if (existente.XmlStorageKey is not null) _substituidos.Add(existente.XmlStorageKey);
                    existente.XmlStorageKey = await GravarXmlAsync(existente, xml, ct);
                    if (nsu is not null) existente.Nsu = nsu;
                    return new(ResultadoRegistroNFe.Atualizada, existente, "XML completo da nota recebido.");
                }

                if (d.Situacao != existente.Situacao && d.Situacao != SituacaoNFe.Autorizada)
                {
                    existente.Situacao = d.Situacao;
                    return new(ResultadoRegistroNFe.Atualizada, existente, "Situação da nota atualizada.");
                }
                return new(ResultadoRegistroNFe.Ignorada, existente, "A nota já está na lista.");

            default:
                return new(ResultadoRegistroNFe.Ignorada, null, "Documento não reconhecido.");
        }
    }

    /// <summary>Depois de salvar: apaga os XMLs que foram substituídos.</summary>
    public async Task ConfirmarAsync()
    {
        foreach (var key in _substituidos) await ApagarAsync(key);
        _substituidos.Clear();
        _gravados.Clear();
    }

    /// <summary>Se o salvamento falhou: apaga os XMLs gravados nesta operação.</summary>
    public async Task DescartarArquivosAsync()
    {
        foreach (var key in _gravados) await ApagarAsync(key);
        _gravados.Clear();
        _substituidos.Clear();
    }

    private static void Aplicar(NotaFiscal n, DadosNotaFiscal d)
    {
        n.EmitenteDocumento = d.EmitenteDocumento;
        n.EmitenteNome = Limitar(d.EmitenteNome, 200)!;
        n.EmitenteIe = Limitar(d.EmitenteIe, 20);
        n.ValorTotal = d.ValorTotal;
        n.DataEmissao = d.DataEmissaoUtc;
        n.TipoOperacao = d.TipoOperacao;
        n.Protocolo = Limitar(d.Protocolo, 20) ?? n.Protocolo;
        n.Situacao = n.Situacao == SituacaoNFe.Cancelada ? SituacaoNFe.Cancelada : d.Situacao;
    }

    private async Task<string> GravarXmlAsync(NotaFiscal n, string xml, CancellationToken ct)
    {
        var key = $"{n.TenantId:N}/{n.EmpresaId:N}/nfe/{n.ChaveAcesso}-{Guid.CreateVersion7():N}.xml";
        using var conteudo = new MemoryStream(Encoding.UTF8.GetBytes(xml), writable: false);
        await storage.SaveAsync(key, conteudo, "application/xml", ct);
        _gravados.Add(key);
        return key;
    }

    private async Task ApagarAsync(string key)
    {
        try { await storage.DeleteAsync(key, CancellationToken.None); }
        catch (Exception e) { logger.LogWarning(e, "Falha ao remover XML de NF-e {StorageKey}", key); }
    }

    private static string? Limitar(string? v, int max) => v is null ? null : v.Length > max ? v[..max] : v;
}
