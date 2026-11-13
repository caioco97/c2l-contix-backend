using Contix.Application.Common.Models;
using Contix.Domain.Enums;
using FluentValidation;

namespace Contix.Application.Features.Notificacoes;

public sealed record NotificacaoDto(
    Guid Id, TipoNotificacao Tipo, string Titulo, string Mensagem, string? Link,
    Guid? EmpresaId, Guid? ReferenciaId, bool Lida, DateTime? LidaEm, DateTime CriadaEm);

public sealed record NotificacaoFiltro : PageQuery
{
    public bool SomenteNaoLidas { get; init; }
}

public sealed record ContadorNotificacoesDto(int NaoLidas);

public sealed record PreferenciaNotificacaoDto(TipoNotificacao Tipo, string Descricao, bool ReceberEmail);

public sealed record AtualizarPreferenciasRequest(IReadOnlyList<PreferenciaItem> Preferencias);

public sealed record PreferenciaItem(TipoNotificacao Tipo, bool ReceberEmail);

public sealed class AtualizarPreferenciasRequestValidator : AbstractValidator<AtualizarPreferenciasRequest>
{
    public AtualizarPreferenciasRequestValidator()
    {
        RuleFor(x => x.Preferencias).NotNull();
        RuleForEach(x => x.Preferencias).ChildRules(p => p.RuleFor(i => i.Tipo).IsInEnum());
    }
}

public static class TiposNotificacao
{
    public static readonly IReadOnlyDictionary<TipoNotificacao, string> Descricoes = new Dictionary<TipoNotificacao, string>
    {
        [TipoNotificacao.NovoDocumento] = "Novos documentos",
        [TipoNotificacao.VencimentoProximo] = "Vencimentos próximos",
        [TipoNotificacao.NovaSolicitacao] = "Novas solicitações",
        [TipoNotificacao.SolicitacaoAtualizada] = "Atualizações de solicitações",
        [TipoNotificacao.NovaNoticia] = "Novas notícias do escritório",
        [TipoNotificacao.CertidaoVencimento] = "Certidões (CND) a vencer ou vencidas",
        [TipoNotificacao.TarefaAtribuida] = "Tarefas atribuídas a você"
    };
}
