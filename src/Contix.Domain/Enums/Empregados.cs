namespace Contix.Domain.Enums;

/// <summary>Tipo de vínculo da pessoa com a empresa.</summary>
public enum VinculoEmpregado
{
    Empregado = 1,
    /// <summary>Sócio (pró-labore) ou autônomo.</summary>
    Contribuinte = 2,
    Estagiario = 3
}

public enum SituacaoEmpregado
{
    Ativo = 1,
    Afastado = 2,
    Desligado = 3
}
