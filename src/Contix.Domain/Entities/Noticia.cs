using Contix.Domain.Common;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;

namespace Contix.Domain.Entities;

/// <summary>Notícia ou comunicado publicado pelo escritório para as empresas clientes.</summary>
public class Noticia : TenantEntity
{
    public string Titulo { get; set; } = null!;
    /// <summary>Resumo exibido na lista do portal. Se vazio, o portal usa o início do conteúdo.</summary>
    public string? Resumo { get; set; }
    /// <summary>Texto da notícia (parágrafos separados por linha em branco).</summary>
    public string Conteudo { get; set; } = null!;

    public DateTime PublicarEm { get; private set; }
    public DateTime? ExpiraEm { get; private set; }

    public PublicoNoticia Publico { get; private set; } = PublicoNoticia.Todos;
    /// <summary>Usado quando <see cref="Publico"/> é <see cref="PublicoNoticia.RegimeTributario"/>.</summary>
    public List<RegimeTributario> Regimes { get; private set; } = [];
    /// <summary>Usado quando <see cref="Publico"/> é <see cref="PublicoNoticia.EmpresasSelecionadas"/>.</summary>
    public ICollection<NoticiaEmpresa> Empresas { get; set; } = new List<NoticiaEmpresa>();

    public string? ImagemStorageKey { get; set; }
    public string? ImagemContentType { get; set; }

    /// <summary>Quando o público-alvo foi avisado da publicação (notificação in-app e e-mail).</summary>
    public DateTime? NotificadaEm { get; private set; }

    public Guid AutorId { get; set; }
    public Usuario? Autor { get; set; }

    public ICollection<NoticiaAnexo> Anexos { get; set; } = new List<NoticiaAnexo>();
    public ICollection<NoticiaLeitura> Leituras { get; set; } = new List<NoticiaLeitura>();

    public void DefinirPeriodo(DateTime publicarEm, DateTime? expiraEm)
    {
        if (expiraEm is { } e && e <= publicarEm)
            throw new DomainException("A data de expiração deve ser posterior à data de publicação.");
        PublicarEm = publicarEm;
        ExpiraEm = expiraEm;
    }

    /// <summary>Define o público. Empresas e regimes que não se aplicam ao tipo escolhido são descartados.</summary>
    public void DefinirPublico(PublicoNoticia publico, IReadOnlyCollection<Guid> empresaIds, IReadOnlyCollection<RegimeTributario> regimes)
    {
        if (publico == PublicoNoticia.EmpresasSelecionadas && empresaIds.Count == 0)
            throw new DomainException("Selecione ao menos uma empresa.");
        if (publico == PublicoNoticia.RegimeTributario && regimes.Count == 0)
            throw new DomainException("Selecione ao menos um regime tributário.");

        Publico = publico;
        Regimes = publico == PublicoNoticia.RegimeTributario ? regimes.Distinct().Order().ToList() : [];

        var desejadas = publico == PublicoNoticia.EmpresasSelecionadas ? empresaIds.ToHashSet() : [];
        foreach (var vinculo in Empresas.Where(v => !desejadas.Contains(v.EmpresaId)).ToList())
            Empresas.Remove(vinculo);
        foreach (var empresaId in desejadas.Except(Empresas.Select(v => v.EmpresaId)))
            Empresas.Add(new NoticiaEmpresa { NoticiaId = Id, EmpresaId = empresaId });
    }

    /// <summary>Publicada, não expirada e ainda sem aviso ao público.</summary>
    public bool PrecisaAvisar(DateTime agora) => NotificadaEm is null && Situacao(agora) == SituacaoNoticia.Publicada;

    public void MarcarNotificada(DateTime agora) => NotificadaEm ??= agora;

    public SituacaoNoticia Situacao(DateTime agora) =>
        PublicarEm > agora ? SituacaoNoticia.Agendada
        : ExpiraEm is { } e && e <= agora ? SituacaoNoticia.Expirada
        : SituacaoNoticia.Publicada;

    /// <summary>A notícia se destina a esta empresa (independente das datas)?</summary>
    public bool DestinadaA(Empresa empresa) => Publico switch
    {
        PublicoNoticia.Todos => true,
        PublicoNoticia.EmpresasSelecionadas => Empresas.Any(v => v.EmpresaId == empresa.Id),
        PublicoNoticia.RegimeTributario => Regimes.Contains(empresa.RegimeTributario),
        _ => false
    };
}

/// <summary>Empresa destinatária de uma notícia com público "empresas selecionadas".</summary>
public class NoticiaEmpresa
{
    public Guid NoticiaId { get; set; }
    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }
}

/// <summary>Leitura de uma notícia por um usuário, no contexto de uma empresa (para medir o alcance por empresa).</summary>
public class NoticiaLeitura
{
    public Guid NoticiaId { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public DateTime LidoEm { get; set; }
}

/// <summary>Arquivo anexado à notícia.</summary>
public class NoticiaAnexo : BaseEntity
{
    public Guid NoticiaId { get; set; }
    public string NomeArquivo { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long TamanhoBytes { get; set; }
    public string StorageKey { get; set; } = null!;
}
