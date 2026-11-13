using Contix.Domain.Common;
using Contix.Domain.Enums;

namespace Contix.Domain.Entities;

/// <summary>
/// Aparelho (app mobile) que recebe push do usuário. O token é do Expo Push Service
/// (<c>ExponentPushToken[...]</c>) e é único: se outro usuário entrar no mesmo aparelho, o token passa a ser dele.
/// </summary>
public class DispositivoPush : BaseEntity
{
    public const int TamanhoMaximoToken = 200;

    public Guid UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public string Token { get; set; } = null!;
    public PlataformaDispositivo Plataforma { get; set; }
    /// <summary>Nome do aparelho informado pelo app (ex.: "iPhone de Ana").</summary>
    public string? Nome { get; set; }
    public DateTime UltimoUsoEm { get; set; }

    public void AtribuirA(Guid usuarioId, PlataformaDispositivo plataforma, string? nome, DateTime agora)
    {
        UsuarioId = usuarioId;
        Plataforma = plataforma;
        Nome = nome;
        UltimoUsoEm = agora;
    }
}
