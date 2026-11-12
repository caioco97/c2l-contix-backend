using Contix.Domain.Common;

namespace Contix.Domain.Entities;

/// <summary>
/// Token de uso único para redefinir a senha. Armazenado apenas como hash SHA-256;
/// o valor em claro só existe no link enviado por e-mail.
/// </summary>
public class TokenRedefinicaoSenha : BaseEntity
{
    public static readonly TimeSpan ValidadePadrao = TimeSpan.FromHours(1);

    public Guid UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public string TokenHash { get; set; } = null!;
    public DateTime ExpiraEm { get; set; }
    public DateTime? UsadoEm { get; private set; }
    /// <summary>Preenchido quando o token é invalidado sem uso (novo pedido ou senha já redefinida).</summary>
    public DateTime? InvalidadoEm { get; private set; }
    public string? CriadoPorIp { get; set; }

    public bool EstaValido(DateTime agora) => UsadoEm is null && InvalidadoEm is null && ExpiraEm > agora;

    public void MarcarComoUsado(DateTime agora)
    {
        if (!EstaValido(agora)) throw new Exceptions.DomainException("Link de redefinição inválido ou expirado.");
        UsadoEm = agora;
    }

    public void Invalidar(DateTime agora)
    {
        if (UsadoEm is null && InvalidadoEm is null) InvalidadoEm = agora;
    }
}
