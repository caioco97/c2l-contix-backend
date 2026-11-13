namespace Contix.Domain.Enums;

/// <summary>Manifestação do destinatário sobre a NF-e (eventos 2102xx da SEFAZ).</summary>
public enum ManifestacaoNFe
{
    SemManifestacao = 0,
    /// <summary>Ciência da Operação (210210). Não é conclusiva: libera o XML completo e admite outra manifestação depois.</summary>
    Ciencia = 1,
    /// <summary>Confirmação da Operação (210200).</summary>
    Confirmada = 2,
    /// <summary>Desconhecimento da Operação (210220).</summary>
    Desconhecida = 3,
    /// <summary>Operação não Realizada (210240), com justificativa.</summary>
    NaoRealizada = 4
}

/// <summary>Situação da NF-e na SEFAZ.</summary>
public enum SituacaoNFe
{
    Autorizada = 1,
    Denegada = 2,
    Cancelada = 3
}

/// <summary>Como a nota chegou ao Contix.</summary>
public enum OrigemNotaFiscal
{
    /// <summary>XML importado manualmente pelo escritório.</summary>
    ImportacaoXml = 1,
    /// <summary>Distribuição DF-e da SEFAZ (busca automática).</summary>
    Sefaz = 2
}
