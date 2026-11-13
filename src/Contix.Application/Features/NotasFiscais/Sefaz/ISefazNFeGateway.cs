using Contix.Domain.Enums;

namespace Contix.Application.Features.NotasFiscais.Sefaz;

/// <summary>Documento devolvido pela Distribuição DF-e (já descompactado).</summary>
/// <param name="Schema">Ex.: resNFe_v1.01.xsd, procNFe_v4.00.xsd, resEvento_v1.01.xsd.</param>
public sealed record DocumentoDistribuido(long Nsu, string Schema, string Xml);

/// <summary>Retorno do nfeDistDFeInteresse. cStat 138 = há documentos, 137 = nenhum documento novo, 656 = consumo indevido.</summary>
public sealed record RetornoDistribuicao(string CStat, string Motivo, long UltimoNsu, long MaxNsu, IReadOnlyList<DocumentoDistribuido> Documentos);

/// <summary>Retorno do envio de um evento. Registrado: cStat 135/136 (ou 573, evento já registrado antes).</summary>
public sealed record RetornoEvento(bool Registrado, string CStat, string Motivo, string? Protocolo);

/// <summary>
/// Web services nacionais da NF-e usados pelo destinatário: Distribuição DF-e (NFeDistribuicaoDFe) e
/// Recepção de Evento do Ambiente Nacional (manifestação do destinatário). Autenticação mútua com o certificado A1.
/// </summary>
public interface ISefazNFeGateway
{
    /// <summary>Sefaz, Simulado ou Desabilitado.</summary>
    string Nome { get; }
    bool Habilitado { get; }
    /// <summary>Producao ou Homologacao.</summary>
    string Ambiente { get; }

    /// <summary>Busca os próximos documentos a partir do último NSU lido. Lança em falha de comunicação.</summary>
    Task<RetornoDistribuicao> DistribuicaoPorNsuAsync(CertificadoA1 certificado, string cnpj, int codigoUf, long ultimoNsu, CancellationToken ct = default);

    /// <summary>Envia a manifestação do destinatário. Lança em falha de comunicação.</summary>
    Task<RetornoEvento> ManifestarAsync(CertificadoA1 certificado, string cnpj, string chaveAcesso, ManifestacaoNFe tipo,
        string? justificativa, CancellationToken ct = default);
}
