using Contix.Domain.Common;

namespace Contix.Domain.Entities;

/// <summary>
/// Caixa de saída de push (padrão outbox): gravada na mesma transação da notificação in-app
/// e enviada em segundo plano para os aparelhos do usuário, com novas tentativas em caso de falha.
/// </summary>
public class PushPendente : BaseEntity
{
    public const int MaxTentativas = 5;

    public Guid UsuarioId { get; set; }
    public string Titulo { get; set; } = null!;
    public string Mensagem { get; set; } = null!;
    /// <summary>Dados para o app abrir o item (JSON: notificacaoId, tipo, link, empresaId, referenciaId).</summary>
    public string Dados { get; set; } = "{}";
    public int Tentativas { get; private set; }
    public DateTime ProximaTentativaEm { get; set; }
    public DateTime? EnviadoEm { get; private set; }
    public string? UltimoErro { get; private set; }

    public bool Desistiu => EnviadoEm is null && Tentativas >= MaxTentativas;

    public void RegistrarEnvio(DateTime agora)
    {
        Tentativas++;
        EnviadoEm = agora;
        UltimoErro = null;
    }

    /// <summary>Backoff exponencial: 1, 2, 4, 8... minutos.</summary>
    public void RegistrarFalha(DateTime agora, string erro)
    {
        Tentativas++;
        UltimoErro = erro.Length > 1000 ? erro[..1000] : erro;
        ProximaTentativaEm = agora.AddMinutes(Math.Pow(2, Tentativas - 1));
    }
}
