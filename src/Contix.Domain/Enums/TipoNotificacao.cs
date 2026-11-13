namespace Contix.Domain.Enums;

/// <summary>Eventos que geram notificação in-app e, conforme a preferência do usuário, e-mail.</summary>
public enum TipoNotificacao
{
    NovoDocumento = 1,
    VencimentoProximo = 2,
    NovaSolicitacao = 3,
    SolicitacaoAtualizada = 4,
    NovaNoticia = 5,
    CertidaoVencimento = 6
}
