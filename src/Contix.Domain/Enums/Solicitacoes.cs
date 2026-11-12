namespace Contix.Domain.Enums;

/// <summary>Tipos de solicitação de serviço disponíveis no portal do cliente.</summary>
public enum TipoSolicitacao
{
    Geral = 1,
    CadastroEmpregado = 2,
    CadastroContribuinte = 3,
    CadastroEstagiario = 4,
    AvisoFerias = 5,
    CalculoFerias = 6,
    AvisoRescisao = 7,
    CalculoRescisao = 8,
    Afastamento = 9,
    LancamentoRubricas = 10
}

/// <summary>Ciclo de vida: Aberta → Em análise → Aguardando cliente → Concluída / Cancelada.</summary>
public enum StatusSolicitacao
{
    Aberta = 1,
    EmAnalise = 2,
    AguardandoCliente = 3,
    Concluida = 4,
    Cancelada = 5
}

/// <summary>Departamentos do escritório que atendem as solicitações.</summary>
public enum Departamento
{
    Contabil = 1,
    Fiscal = 2,
    Pessoal = 3,
    Societario = 4,
    Financeiro = 5
}

public enum TipoInteracao
{
    Abertura = 1,
    Comentario = 2,
    MudancaStatus = 3,
    Atribuicao = 4
}
