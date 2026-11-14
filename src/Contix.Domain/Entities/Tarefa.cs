using Contix.Domain.Common;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;

namespace Contix.Domain.Entities;

/// <summary>
/// Tarefa interna do escritório, vinculada a uma empresa (cliente). Não aparece no portal do cliente.
/// Toda mudança gera uma <see cref="TarefaInteracao"/> no histórico.
/// </summary>
public class Tarefa : TenantEntity
{
    private static readonly Dictionary<StatusTarefa, StatusTarefa[]> Transicoes = new()
    {
        [StatusTarefa.Pendente] = [StatusTarefa.EmAndamento, StatusTarefa.Concluida, StatusTarefa.Cancelada],
        [StatusTarefa.EmAndamento] = [StatusTarefa.Pendente, StatusTarefa.Concluida, StatusTarefa.Cancelada],
        // Reabertura.
        [StatusTarefa.Concluida] = [StatusTarefa.EmAndamento],
        [StatusTarefa.Cancelada] = [StatusTarefa.Pendente],
    };

    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }

    /// <summary>Número sequencial por escritório, exibido ao usuário (ex.: #12).</summary>
    public int Numero { get; set; }
    public string Titulo { get; private set; } = null!;
    public string? Descricao { get; private set; }
    public Departamento Departamento { get; private set; }

    public Guid ResponsavelId { get; private set; }
    public Usuario? Responsavel { get; set; }
    public Guid CriadoPorId { get; set; }
    public Usuario? CriadoPor { get; set; }

    /// <summary>Data combinada dentro do escritório para entregar a tarefa.</summary>
    public DateOnly PrazoInterno { get; private set; }
    /// <summary>Vencimento legal da obrigação (opcional), que não pode ser perdido.</summary>
    public DateOnly? PrazoLegal { get; private set; }

    public StatusTarefa Status { get; private set; } = StatusTarefa.Pendente;
    public DateTime? ConcluidaEm { get; private set; }

    /// <summary>Solicitação do cliente que originou a tarefa (opcional).</summary>
    public Guid? SolicitacaoId { get; private set; }
    public Solicitacao? Solicitacao { get; set; }
    /// <summary>Obrigação recorrente que gerou a tarefa (opcional), com a competência correspondente.</summary>
    public Guid? ObrigacaoId { get; private set; }
    public ObrigacaoRecorrente? Obrigacao { get; set; }
    /// <summary>Competência (primeiro dia do mês) da obrigação.</summary>
    public DateOnly? Competencia { get; private set; }
    /// <summary>Documentos publicados na pasta da empresa ao concluir a tarefa (entrega ao cliente).</summary>
    public List<Guid> DocumentosEntregues { get; private set; } = [];

    public ICollection<TarefaInteracao> Interacoes { get; set; } = new List<TarefaInteracao>();
    public ICollection<TarefaAnexo> Anexos { get; set; } = new List<TarefaAnexo>();

    public bool EstaFinalizada => Status is StatusTarefa.Concluida or StatusTarefa.Cancelada;

    /// <summary>Passou do prazo interno sem ser finalizada.</summary>
    public bool EstaAtrasada(DateOnly hoje) => !EstaFinalizada && PrazoInterno < hoje;

    /// <summary>Passou do prazo legal sem ser finalizada.</summary>
    public bool PrazoLegalVencido(DateOnly hoje) => !EstaFinalizada && PrazoLegal is { } p && p < hoje;

    public static IReadOnlyCollection<StatusTarefa> TransicoesPermitidas(StatusTarefa atual) =>
        Transicoes.GetValueOrDefault(atual) ?? [];

    public static Tarefa Criar(
        Guid tenantId, Guid empresaId, int numero, string titulo, string? descricao, Departamento departamento,
        Guid responsavelId, DateOnly prazoInterno, DateOnly? prazoLegal, Guid criadoPorId, DateTime agora)
    {
        var tarefa = new Tarefa
        {
            TenantId = tenantId, EmpresaId = empresaId, Numero = numero, CriadoPorId = criadoPorId
        };
        tarefa.DefinirDados(titulo, descricao, departamento, responsavelId, prazoInterno, prazoLegal);
        tarefa.AdicionarInteracao(new TarefaInteracao
        {
            AutorId = criadoPorId, Tipo = TipoInteracaoTarefa.Criacao, StatusNovo = tarefa.Status, Mensagem = tarefa.Descricao
        }, agora);
        return tarefa;
    }

    /// <summary>Altera os dados da tarefa e registra no histórico o que mudou.</summary>
    /// <param name="nomeResponsavel">Nome do novo responsável, para o texto do histórico.</param>
    public TarefaInteracao? Editar(
        string titulo, string? descricao, Departamento departamento, Guid responsavelId, string nomeResponsavel,
        DateOnly prazoInterno, DateOnly? prazoLegal, Guid autorId, DateTime agora)
    {
        var mudancas = new List<string>();
        if (Titulo != titulo.Trim()) mudancas.Add("título");
        if (Descricao != Normalizar(descricao)) mudancas.Add("descrição");
        if (Departamento != departamento) mudancas.Add("departamento");
        if (ResponsavelId != responsavelId) mudancas.Add($"responsável: {nomeResponsavel}");
        if (PrazoInterno != prazoInterno) mudancas.Add($"prazo interno: {prazoInterno:dd/MM/yyyy}");
        if (PrazoLegal != prazoLegal) mudancas.Add(prazoLegal is { } p ? $"prazo legal: {p:dd/MM/yyyy}" : "prazo legal removido");
        if (mudancas.Count == 0) return null;

        DefinirDados(titulo, descricao, departamento, responsavelId, prazoInterno, prazoLegal);
        return AdicionarInteracao(new TarefaInteracao
        {
            AutorId = autorId, Tipo = TipoInteracaoTarefa.Edicao,
            Mensagem = "Alterou " + string.Join("; ", mudancas) + "."
        }, agora);
    }

    /// <summary>Mantém o vínculo com a solicitação do cliente que originou a tarefa.</summary>
    public void VincularSolicitacao(Guid solicitacaoId)
    {
        if (SolicitacaoId is not null && SolicitacaoId != solicitacaoId)
            throw new DomainException("A tarefa já está vinculada a outra solicitação.");
        SolicitacaoId = solicitacaoId;
    }

    /// <summary>Marca a tarefa como gerada por uma obrigação recorrente na competência.</summary>
    public void VincularObrigacao(Guid obrigacaoId, DateOnly competencia)
    {
        ObrigacaoId = obrigacaoId;
        Competencia = new DateOnly(competencia.Year, competencia.Month, 1);
    }

    /// <summary>Registra os documentos entregues ao cliente na conclusão.</summary>
    public void RegistrarEntrega(IEnumerable<Guid> documentoIds)
    {
        foreach (var id in documentoIds)
            if (!DocumentosEntregues.Contains(id)) DocumentosEntregues.Add(id);
    }

    public TarefaInteracao Comentar(Guid autorId, string? mensagem, bool possuiAnexos, DateTime agora)
    {
        if (string.IsNullOrWhiteSpace(mensagem) && !possuiAnexos)
            throw new DomainException("Escreva um comentário ou anexe um arquivo.");
        return AdicionarInteracao(new TarefaInteracao
        {
            AutorId = autorId, Tipo = TipoInteracaoTarefa.Comentario, Mensagem = mensagem?.Trim()
        }, agora);
    }

    public TarefaInteracao AlterarStatus(StatusTarefa novo, Guid autorId, string? mensagem, DateTime agora)
    {
        if (novo == Status) throw new DomainException("A tarefa já está neste status.");
        if (!TransicoesPermitidas(Status).Contains(novo))
            throw new DomainException($"Não é possível mudar de '{Status}' para '{novo}'.");

        var anterior = Status;
        Status = novo;
        ConcluidaEm = novo == StatusTarefa.Concluida ? agora : null;
        return AdicionarInteracao(new TarefaInteracao
        {
            AutorId = autorId, Tipo = TipoInteracaoTarefa.MudancaStatus, Mensagem = Normalizar(mensagem),
            StatusAnterior = anterior, StatusNovo = novo
        }, agora);
    }

    private void DefinirDados(string titulo, string? descricao, Departamento departamento, Guid responsavelId,
        DateOnly prazoInterno, DateOnly? prazoLegal)
    {
        if (string.IsNullOrWhiteSpace(titulo)) throw new DomainException("Informe o título da tarefa.");
        if (responsavelId == Guid.Empty) throw new DomainException("Informe o responsável pela tarefa.");
        if (prazoLegal is { } legal && prazoInterno > legal)
            throw new DomainException("O prazo interno não pode ser posterior ao prazo legal.");

        Titulo = titulo.Trim();
        Descricao = Normalizar(descricao);
        Departamento = departamento;
        ResponsavelId = responsavelId;
        PrazoInterno = prazoInterno;
        PrazoLegal = prazoLegal;
    }

    private static string? Normalizar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private TarefaInteracao AdicionarInteracao(TarefaInteracao interacao, DateTime agora)
    {
        interacao.TarefaId = Id;
        interacao.CreatedAt = agora;
        Interacoes.Add(interacao);
        return interacao;
    }
}

/// <summary>Item do histórico da tarefa: criação, comentário, mudança de status ou edição.</summary>
public class TarefaInteracao : BaseEntity
{
    public Guid TarefaId { get; set; }
    public Guid AutorId { get; set; }
    public Usuario? Autor { get; set; }
    public TipoInteracaoTarefa Tipo { get; set; }
    public string? Mensagem { get; set; }
    public StatusTarefa? StatusAnterior { get; set; }
    public StatusTarefa? StatusNovo { get; set; }
}

/// <summary>Arquivo anexado na criação ou em um comentário da tarefa.</summary>
public class TarefaAnexo : BaseEntity
{
    public Guid TarefaId { get; set; }
    public Guid? InteracaoId { get; set; }
    public string NomeArquivo { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long TamanhoBytes { get; set; }
    public string StorageKey { get; set; } = null!;
    public Guid EnviadoPorId { get; set; }
    public Usuario? EnviadoPor { get; set; }
}
