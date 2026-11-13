using Contix.Domain.Common;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;

namespace Contix.Domain.Entities;

/// <summary>NF-e emitida contra o CNPJ da empresa (nota de entrada), com a manifestação do destinatário.</summary>
public class NotaFiscal : TenantEntity
{
    public const int JustificativaMinima = 15;
    public const int JustificativaMaxima = 255;

    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }

    /// <summary>Chave de acesso (44 dígitos).</summary>
    public string ChaveAcesso { get; set; } = null!;
    public int Modelo { get; set; }
    public int Serie { get; set; }
    public long Numero { get; set; }

    public string EmitenteDocumento { get; set; } = null!;
    public string EmitenteNome { get; set; } = null!;
    public string? EmitenteIe { get; set; }
    public string DestinatarioDocumento { get; set; } = null!;

    public decimal ValorTotal { get; set; }
    public DateTime DataEmissao { get; set; }
    /// <summary>0 = entrada, 1 = saída (do ponto de vista do emitente).</summary>
    public int? TipoOperacao { get; set; }
    /// <summary>Protocolo de autorização.</summary>
    public string? Protocolo { get; set; }
    public SituacaoNFe Situacao { get; set; } = SituacaoNFe.Autorizada;

    public OrigemNotaFiscal Origem { get; set; }
    /// <summary>true quando o XML guardado é a nota completa (procNFe); false quando é só o resumo (resNFe) da SEFAZ.</summary>
    public bool XmlCompleto { get; set; }
    public string? XmlStorageKey { get; set; }
    /// <summary>NSU da Distribuição DF-e do último documento que atualizou a nota.</summary>
    public long? Nsu { get; set; }

    public ManifestacaoNFe Manifestacao { get; private set; } = ManifestacaoNFe.SemManifestacao;
    public DateTime? ManifestadaEm { get; private set; }
    /// <summary>Quem manifestou. Nulo: ciência automática da busca na SEFAZ.</summary>
    public Guid? ManifestadaPorId { get; private set; }
    public Usuario? ManifestadaPor { get; set; }
    public string? JustificativaManifestacao { get; private set; }
    /// <summary>Protocolo do evento na SEFAZ. Nulo: manifestação registrada só no Contix (sem certificado/integração).</summary>
    public string? ProtocoloManifestacao { get; private set; }

    public static bool Conclusiva(ManifestacaoNFe m) =>
        m is ManifestacaoNFe.Confirmada or ManifestacaoNFe.Desconhecida or ManifestacaoNFe.NaoRealizada;

    /// <summary>Valida se a manifestação pode ser feita agora (antes de enviá-la à SEFAZ).</summary>
    public void GarantirPodeManifestar(ManifestacaoNFe tipo, string? justificativa)
    {
        if (tipo == ManifestacaoNFe.SemManifestacao || !Enum.IsDefined(tipo))
            throw new DomainException("Escolha o tipo de manifestação.");
        if (Situacao == SituacaoNFe.Cancelada)
            throw new DomainException("A NF-e foi cancelada pelo emitente e não pode mais ser manifestada.");
        if (Conclusiva(Manifestacao))
            throw new DomainException("Esta NF-e já tem uma manifestação conclusiva e não pode ser alterada.");
        if (tipo == ManifestacaoNFe.Ciencia && Manifestacao == ManifestacaoNFe.Ciencia)
            throw new DomainException("A ciência da operação já foi registrada para esta NF-e.");
        if (tipo == ManifestacaoNFe.NaoRealizada)
        {
            var j = justificativa?.Trim() ?? "";
            if (j.Length is < JustificativaMinima or > JustificativaMaxima)
                throw new DomainException($"Informe a justificativa (de {JustificativaMinima} a {JustificativaMaxima} caracteres).");
        }
    }

    public void Manifestar(ManifestacaoNFe tipo, string? justificativa, Guid? usuarioId, string? protocolo, DateTime agora)
    {
        GarantirPodeManifestar(tipo, justificativa);
        Manifestacao = tipo;
        JustificativaManifestacao = tipo == ManifestacaoNFe.NaoRealizada ? justificativa!.Trim() : null;
        ManifestadaPorId = usuarioId;
        ManifestadaEm = agora;
        ProtocoloManifestacao = protocolo;
    }

    /// <summary>Extrai modelo, série e número da chave de acesso.</summary>
    public void PreencherDaChave()
    {
        if (ChaveAcesso is not { Length: 44 } c || !c.All(char.IsAsciiDigit))
            throw new DomainException("Chave de acesso inválida.");
        Modelo = int.Parse(c.AsSpan(20, 2));
        Serie = int.Parse(c.AsSpan(22, 3));
        Numero = long.Parse(c.AsSpan(25, 9));
    }
}
