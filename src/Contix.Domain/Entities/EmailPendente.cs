using Contix.Domain.Common;

namespace Contix.Domain.Entities;

/// <summary>
/// Caixa de saída de e-mails (padrão outbox): gravada na mesma transação da ação que a originou
/// e enviada em segundo plano, com novas tentativas em caso de falha.
/// </summary>
public class EmailPendente : BaseEntity
{
    public const int MaxTentativas = 5;

    public string Para { get; set; } = null!;
    public string? NomePara { get; set; }
    public string Assunto { get; set; } = null!;
    public string CorpoHtml { get; set; } = null!;
    public string CorpoTexto { get; set; } = null!;
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
