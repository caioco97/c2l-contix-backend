namespace Contix.Domain.Enums;

/// <summary>Ciclo de vida da tarefa interna do escritório.</summary>
public enum StatusTarefa
{
    Pendente = 1,
    EmAndamento = 2,
    Concluida = 3,
    Cancelada = 4
}

public enum TipoInteracaoTarefa
{
    Criacao = 1,
    Comentario = 2,
    MudancaStatus = 3,
    Edicao = 4
}
