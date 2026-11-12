using Contix.Domain.Common;
using Contix.Domain.Enums;

namespace Contix.Domain.Entities;

/// <summary>Notificação in-app destinada a um usuário.</summary>
public class Notificacao : TenantEntity
{
    public Guid UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public TipoNotificacao Tipo { get; set; }
    public string Titulo { get; set; } = null!;
    public string Mensagem { get; set; } = null!;
    /// <summary>Rota do frontend para abrir o item relacionado (ex.: <c>/documentos/{pastaId}</c>).</summary>
    public string? Link { get; set; }
    /// <summary>Empresa do item relacionado, para o frontend trocar a empresa ativa antes de navegar.</summary>
    public Guid? EmpresaId { get; set; }
    /// <summary>Id do documento/solicitação que originou a notificação.</summary>
    public Guid? ReferenciaId { get; set; }
    public DateTime? LidaEm { get; private set; }

    public bool Lida => LidaEm is not null;

    public void MarcarComoLida(DateTime agora) => LidaEm ??= agora;
}

/// <summary>
/// Preferência de e-mail por tipo de notificação. Sem registro, o e-mail é enviado (opt-out).
/// As notificações in-app são sempre geradas.
/// </summary>
public class PreferenciaNotificacao
{
    public Guid UsuarioId { get; set; }
    public TipoNotificacao Tipo { get; set; }
    public bool ReceberEmail { get; set; } = true;
}
