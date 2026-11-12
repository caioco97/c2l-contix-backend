namespace Contix.Domain.Enums;

/// <summary>Tipos de certidão negativa de débitos controlados pelo escritório.</summary>
public enum TipoCertidao
{
    /// <summary>Débitos relativos a tributos federais e à dívida ativa da União (RFB/PGFN).</summary>
    FederalPgfn = 1,
    Estadual = 2,
    Municipal = 3,
    /// <summary>Certificado de Regularidade do FGTS (CRF).</summary>
    Fgts = 4,
    /// <summary>Certidão Negativa de Débitos Trabalhistas (CNDT).</summary>
    Trabalhista = 5
}

/// <summary>Resultado da certidão, conforme emitida pelo órgão.</summary>
public enum SituacaoCertidao
{
    Negativa = 1,
    PositivaComEfeitoNegativa = 2,
    Positiva = 3
}

/// <summary>Validade calculada em relação à data de hoje.</summary>
public enum ValidadeCertidao
{
    Vigente = 1,
    /// <summary>Vence nos próximos <see cref="Entities.Certidao.DiasAlertaVencimento"/> dias.</summary>
    AVencer = 2,
    Vencida = 3
}
