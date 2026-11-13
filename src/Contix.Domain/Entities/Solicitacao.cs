using Contix.Domain.Common;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;

namespace Contix.Domain.Entities;

/// <summary>
/// Solicitação de serviço aberta por uma empresa (cliente) para o escritório.
/// Concentra as regras do fluxo de status; toda mudança gera uma <see cref="SolicitacaoInteracao"/> no histórico.
/// </summary>
public class Solicitacao : TenantEntity
{
    /// <summary>Transições permitidas ao escritório (administrador/colaborador).</summary>
    private static readonly Dictionary<StatusSolicitacao, StatusSolicitacao[]> TransicoesEscritorio = new()
    {
        [StatusSolicitacao.Aberta] = [StatusSolicitacao.EmAnalise, StatusSolicitacao.AguardandoCliente, StatusSolicitacao.Concluida, StatusSolicitacao.Cancelada],
        [StatusSolicitacao.EmAnalise] = [StatusSolicitacao.AguardandoCliente, StatusSolicitacao.Concluida, StatusSolicitacao.Cancelada],
        [StatusSolicitacao.AguardandoCliente] = [StatusSolicitacao.EmAnalise, StatusSolicitacao.Concluida, StatusSolicitacao.Cancelada],
        // Reabertura.
        [StatusSolicitacao.Concluida] = [StatusSolicitacao.EmAnalise],
        [StatusSolicitacao.Cancelada] = [StatusSolicitacao.EmAnalise],
    };

    /// <summary>Transições permitidas ao cliente: apenas cancelar enquanto o escritório não concluiu.</summary>
    private static readonly Dictionary<StatusSolicitacao, StatusSolicitacao[]> TransicoesCliente = new()
    {
        [StatusSolicitacao.Aberta] = [StatusSolicitacao.Cancelada],
        [StatusSolicitacao.AguardandoCliente] = [StatusSolicitacao.Cancelada],
    };

    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }

    /// <summary>Número sequencial por escritório, exibido ao usuário (ex.: #1024).</summary>
    public int Numero { get; set; }
    public TipoSolicitacao Tipo { get; set; }
    public Departamento Departamento { get; set; }
    public string Assunto { get; set; } = null!;
    public string? Descricao { get; set; }
    /// <summary>Campos específicos do tipo (férias, rescisão...), em JSON.</summary>
    public string? Dados { get; set; }
    /// <summary>
    /// Empregado a que a solicitação se refere (férias, rescisão, afastamento) ou que ela gerou (cadastros, ao concluir).
    /// Lançamentos de rubricas referenciam vários empregados dentro de <see cref="Dados"/>.
    /// </summary>
    public Guid? EmpregadoId { get; set; }
    public Empregado? Empregado { get; set; }

    public StatusSolicitacao Status { get; private set; } = StatusSolicitacao.Aberta;
    public Guid SolicitanteId { get; set; }
    public Usuario? Solicitante { get; set; }
    public Guid? ResponsavelId { get; private set; }
    public Usuario? Responsavel { get; set; }
    /// <summary>Prazo de atendimento (SLA).</summary>
    public DateOnly? Prazo { get; private set; }
    public DateTime? FinalizadaEm { get; private set; }

    public ICollection<SolicitacaoInteracao> Interacoes { get; set; } = new List<SolicitacaoInteracao>();
    public ICollection<SolicitacaoAnexo> Anexos { get; set; } = new List<SolicitacaoAnexo>();

    public bool EstaFinalizada => Status is StatusSolicitacao.Concluida or StatusSolicitacao.Cancelada;

    public bool EstaAtrasada(DateOnly hoje) => !EstaFinalizada && Prazo is { } p && p < hoje;

    public static IReadOnlyCollection<StatusSolicitacao> TransicoesPermitidas(StatusSolicitacao atual, bool ehCliente) =>
        (ehCliente ? TransicoesCliente : TransicoesEscritorio).GetValueOrDefault(atual) ?? [];

    /// <summary>Registra a abertura no histórico. Chamado uma vez, logo após a criação.</summary>
    public SolicitacaoInteracao RegistrarAbertura(DateTime agora, DateOnly? prazo)
    {
        if (Interacoes.Count > 0) throw new DomainException("A solicitação já foi aberta.");
        Prazo = prazo;
        return AdicionarInteracao(new SolicitacaoInteracao
        {
            AutorId = SolicitanteId, Tipo = TipoInteracao.Abertura, StatusNovo = Status, Mensagem = Descricao
        }, agora);
    }

    /// <summary>
    /// Adiciona um comentário. Quando o cliente responde a uma solicitação que aguardava por ele,
    /// ela volta automaticamente para "Em análise".
    /// </summary>
    public SolicitacaoInteracao Comentar(Guid autorId, bool autorEhCliente, string? mensagem, bool possuiAnexos, DateTime agora)
    {
        if (string.IsNullOrWhiteSpace(mensagem) && !possuiAnexos)
            throw new DomainException("Escreva uma mensagem ou anexe um arquivo.");
        if (autorEhCliente && EstaFinalizada)
            throw new DomainException("Esta solicitação foi finalizada e não aceita novas mensagens.");

        var interacao = AdicionarInteracao(new SolicitacaoInteracao
        {
            AutorId = autorId, Tipo = TipoInteracao.Comentario, Mensagem = mensagem?.Trim()
        }, agora);

        if (autorEhCliente && Status == StatusSolicitacao.AguardandoCliente)
            // 1 µs depois, para aparecer logo após o comentário no histórico.
            MudarStatus(StatusSolicitacao.EmAnalise, autorId, "Resposta do cliente recebida.", agora.AddTicks(10));

        return interacao;
    }

    public SolicitacaoInteracao AlterarStatus(StatusSolicitacao novo, Guid autorId, bool autorEhCliente, string? mensagem, DateTime agora)
    {
        if (novo == Status) throw new DomainException("A solicitação já está neste status.");
        if (!TransicoesPermitidas(Status, autorEhCliente).Contains(novo))
            throw new DomainException($"Não é possível mudar de '{Status}' para '{novo}'.");
        if (novo == StatusSolicitacao.AguardandoCliente && string.IsNullOrWhiteSpace(mensagem))
            throw new DomainException("Informe ao cliente o que é necessário para continuar.");

        return MudarStatus(novo, autorId, mensagem, agora);
    }

    public SolicitacaoInteracao Atribuir(Guid? responsavelId, string? responsavelNome, DateOnly? prazo, Guid autorId, DateTime agora)
    {
        ResponsavelId = responsavelId;
        Prazo = prazo;
        var texto = responsavelId is null ? "Responsável removido." : $"Responsável: {responsavelNome}.";
        if (prazo is { } p) texto += $" Prazo: {p:dd/MM/yyyy}.";
        return AdicionarInteracao(new SolicitacaoInteracao
        {
            AutorId = autorId, Tipo = TipoInteracao.Atribuicao, Mensagem = texto
        }, agora);
    }

    private SolicitacaoInteracao MudarStatus(StatusSolicitacao novo, Guid autorId, string? mensagem, DateTime agora)
    {
        var anterior = Status;
        Status = novo;
        FinalizadaEm = EstaFinalizada ? agora : null;
        return AdicionarInteracao(new SolicitacaoInteracao
        {
            AutorId = autorId, Tipo = TipoInteracao.MudancaStatus, Mensagem = mensagem?.Trim(),
            StatusAnterior = anterior, StatusNovo = novo
        }, agora);
    }

    private SolicitacaoInteracao AdicionarInteracao(SolicitacaoInteracao interacao, DateTime agora)
    {
        interacao.SolicitacaoId = Id;
        interacao.CreatedAt = agora;
        Interacoes.Add(interacao);
        return interacao;
    }
}

/// <summary>Item do histórico: abertura, comentário, mudança de status ou atribuição.</summary>
public class SolicitacaoInteracao : BaseEntity
{
    public Guid SolicitacaoId { get; set; }
    public Guid AutorId { get; set; }
    public Usuario? Autor { get; set; }
    public TipoInteracao Tipo { get; set; }
    public string? Mensagem { get; set; }
    public StatusSolicitacao? StatusAnterior { get; set; }
    public StatusSolicitacao? StatusNovo { get; set; }
    public ICollection<SolicitacaoAnexo> Anexos { get; set; } = new List<SolicitacaoAnexo>();
}

/// <summary>Arquivo anexado na abertura ou em um comentário.</summary>
public class SolicitacaoAnexo : BaseEntity
{
    public Guid SolicitacaoId { get; set; }
    public Guid? InteracaoId { get; set; }
    public string NomeArquivo { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long TamanhoBytes { get; set; }
    public string StorageKey { get; set; } = null!;
    public Guid EnviadoPorId { get; set; }
    public Usuario? EnviadoPor { get; set; }
}
