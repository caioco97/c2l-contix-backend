using Contix.Domain.Common;

namespace Contix.Domain.Entities;

/// <summary>
/// Refresh token rotativo. Armazenado apenas como hash.
/// Tokens de uma mesma cadeia compartilham a <see cref="Familia"/> para detecção de reutilização.
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public string TokenHash { get; set; } = null!;
    public Guid Familia { get; set; }
    public DateTime ExpiraEm { get; set; }
    public DateTime? RevogadoEm { get; set; }
    public string? SubstituidoPorHash { get; set; }
    public string? MotivoRevogacao { get; set; }
    public string? CriadoPorIp { get; set; }

    public bool EstaAtivo(DateTime agora) => RevogadoEm is null && ExpiraEm > agora;

    public void Revogar(DateTime agora, string motivo, string? substituidoPorHash = null)
    {
        if (RevogadoEm is not null) return;
        RevogadoEm = agora;
        MotivoRevogacao = motivo;
        SubstituidoPorHash = substituidoPorHash;
    }
}
