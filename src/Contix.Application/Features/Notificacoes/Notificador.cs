using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contix.Application.Features.Notificacoes;

/// <summary>Evento a ser notificado. O link pode variar conforme o destinatário (portal do cliente x atendimento).</summary>
/// <param name="IgnorarPreferenciaEmail">Envio explícito (ex.: "Enviar documentos"): manda o e-mail mesmo para quem desativou o tipo.</param>
/// <param name="MensagemAdicional">Mensagem livre de quem enviou, incluída no e-mail.</param>
public sealed record EventoNotificacao(
    TipoNotificacao Tipo,
    string Titulo,
    string Mensagem,
    Guid? EmpresaId,
    Guid? ReferenciaId,
    Func<Destinatario, string?> Link,
    string TextoBotao = "Abrir no Contix",
    bool IgnorarPreferenciaEmail = false,
    string? MensagemAdicional = null);

/// <summary>
/// Gera notificações in-app e enfileira o push (app mobile) e os e-mails correspondentes (respeitando as preferências).
/// Não salva: as notificações são persistidas no próximo SaveChanges, junto com a ação que as originou.
/// </summary>
public interface INotificador
{
    Task NotificarAsync(Guid tenantId, IEnumerable<Destinatario> destinatarios, EventoNotificacao evento, CancellationToken ct = default);
}

public sealed class Notificador(
    INotificacaoRepository repositorio,
    IEmailQueue emails,
    IPushQueue push,
    ICurrentUser currentUser,
    IOptions<NotificacaoOptions> options,
    ILogger<Notificador> logger) : INotificador
{
    public async Task NotificarAsync(Guid tenantId, IEnumerable<Destinatario> destinatarios, EventoNotificacao evento, CancellationToken ct = default)
    {
        // Quem executou a ação não é notificado sobre ela.
        var lista = destinatarios.Where(d => d.Id != currentUser.UserId).DistinctBy(d => d.Id).ToList();
        if (lista.Count == 0) return;

        var semEmail = (await repositorio.ListarPreferenciasAsync(lista.Select(d => d.Id).ToList(), ct))
            .Where(p => p.Tipo == evento.Tipo && !p.ReceberEmail)
            .Select(p => p.UsuarioId)
            .ToHashSet();

        var escritorio = await repositorio.ObterEscritorioAsync(tenantId, ct);
        var marca = new EmailTemplates.Marca(escritorio?.NomeFantasia ?? escritorio?.RazaoSocial ?? "Contix", escritorio?.LogoUrl);

        foreach (var d in lista)
        {
            var link = evento.Link(d);
            var notificacao = new Notificacao
            {
                TenantId = tenantId,
                UsuarioId = d.Id,
                Tipo = evento.Tipo,
                Titulo = evento.Titulo,
                Mensagem = evento.Mensagem,
                Link = link,
                EmpresaId = evento.EmpresaId,
                ReferenciaId = evento.ReferenciaId
            };
            await repositorio.AddAsync(notificacao, ct);

            // Push nos aparelhos do usuário (app mobile), com os dados para abrir o item.
            await push.EnfileirarAsync(new PushMensagem(d.Id, evento.Titulo, evento.Mensagem, new Dictionary<string, string?>
            {
                ["notificacaoId"] = notificacao.Id.ToString(),
                ["tipo"] = evento.Tipo.ToString(),
                ["link"] = link,
                ["empresaId"] = evento.EmpresaId?.ToString(),
                ["referenciaId"] = evento.ReferenciaId?.ToString()
            }), ct);

            if (!evento.IgnorarPreferenciaEmail && semEmail.Contains(d.Id)) continue;
            string[] paragrafos = string.IsNullOrWhiteSpace(evento.MensagemAdicional)
                ? [evento.Mensagem]
                : [evento.Mensagem, evento.MensagemAdicional];
            await emails.EnfileirarAsync(EmailTemplates.Montar(
                d.Email, d.Nome, evento.Titulo, marca, evento.Titulo, paragrafos,
                evento.TextoBotao, EmailTemplates.Url(options.Value.UrlFrontend, link),
                "Você pode escolher quais e-mails recebe nas preferências de notificação do Contix."), ct);
        }

        logger.LogInformation("Notificação {Tipo} gerada para {Quantidade} usuário(s) no tenant {TenantId}",
            evento.Tipo, lista.Count, tenantId);
    }
}
