namespace Contix.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string Secao = "Email";

    /// <summary>"Smtp" envia de verdade; "Log" apenas registra no log (padrão quando não há servidor configurado).</summary>
    public string Provider { get; set; } = "Log";
    public string Remetente { get; set; } = "nao-responda@contix.dev";
    public string NomeRemetente { get; set; } = "Contix";
    public SmtpOptions Smtp { get; set; } = new();
    /// <summary>Envio em segundo plano da caixa de saída.</summary>
    public bool ProcessarFila { get; set; } = true;
    public int IntervaloFilaSegundos { get; set; } = 10;
    public int LoteFila { get; set; } = 20;
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Porta { get; set; } = 1025;
    /// <summary>None, Auto, SslOnConnect, StartTls, StartTlsWhenAvailable.</summary>
    public string Seguranca { get; set; } = "None";
    public string? Usuario { get; set; }
    public string? Senha { get; set; }
}
