namespace Contix.Application.Common.Abstractions;

/// <summary>Push para todos os aparelhos de um usuário. <paramref name="Dados"/> segue para o app.</summary>
public sealed record PushMensagem(Guid UsuarioId, string Titulo, string Mensagem, IReadOnlyDictionary<string, string?> Dados);

/// <summary>
/// Enfileira push na caixa de saída. A gravação acontece no próximo <see cref="IUnitOfWork.SaveChangesAsync"/>,
/// junto com a ação que originou o push; o envio é feito em segundo plano.
/// </summary>
public interface IPushQueue
{
    Task EnfileirarAsync(PushMensagem mensagem, CancellationToken ct = default);
}
