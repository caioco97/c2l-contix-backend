namespace Contix.Application.Features.Notificacoes;

public sealed class NotificacaoOptions
{
    public const string Secao = "Notificacoes";

    /// <summary>URL pública do frontend, usada nos links dos e-mails.</summary>
    public string UrlFrontend { get; set; } = "http://localhost:5173";
    /// <summary>Validade do link de redefinição de senha.</summary>
    public int ValidadeLinkRedefinicaoMinutos { get; set; } = 60;
    /// <summary>Validade do link de primeiro acesso enviado no convite de usuário.</summary>
    public int ValidadeConviteDias { get; set; } = 7;
}
