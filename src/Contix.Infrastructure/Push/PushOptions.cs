namespace Contix.Infrastructure.Push;

public sealed class PushOptions
{
    public const string Secao = "Push";

    /// <summary>"Expo" envia pelo Expo Push Service; "Log" apenas registra no log (padrão).</summary>
    public string Provider { get; set; } = "Log";
    /// <summary>Endpoint do Expo Push Service.</summary>
    public string UrlExpo { get; set; } = "https://exp.host/--/api/v2/push/send";
    /// <summary>Access token do Expo, obrigatório quando o "Enhanced Push Security" está ativo no projeto.</summary>
    public string? AccessToken { get; set; }
    /// <summary>Envio em segundo plano da caixa de saída.</summary>
    public bool ProcessarFila { get; set; } = true;
    public int IntervaloFilaSegundos { get; set; } = 5;
    public int LoteFila { get; set; } = 50;
}
