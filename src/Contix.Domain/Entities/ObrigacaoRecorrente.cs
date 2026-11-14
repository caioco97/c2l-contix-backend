using Contix.Domain.Common;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;

namespace Contix.Domain.Entities;

/// <summary>
/// Modelo de obrigação recorrente do escritório (ex.: DCTFWeb, folha, SPED). Gera uma tarefa por empresa aplicável
/// em cada competência, com o vencimento calculado a partir da periodicidade e do dia de vencimento.
/// </summary>
public class ObrigacaoRecorrente : TenantEntity
{
    public const int MaxDiasAntecedencia = 60;

    public string Titulo { get; private set; } = null!;
    public string? Descricao { get; private set; }
    public Departamento Departamento { get; private set; }
    public PeriodicidadeObrigacao Periodicidade { get; private set; }
    /// <summary>Dia do vencimento legal (1 a 31; nos meses mais curtos vale o último dia).</summary>
    public int DiaVencimento { get; private set; }
    /// <summary>Quantos meses depois da competência a obrigação vence (ex.: 1 = mês seguinte).</summary>
    public int MesesAposCompetencia { get; private set; }
    /// <summary>
    /// Mês de referência da periodicidade: no trimestral, um dos meses de competência (os demais são de 3 em 3 meses);
    /// no anual, o mês da competência. Ignorado no mensal.
    /// </summary>
    public int MesReferencia { get; private set; } = 1;
    /// <summary>Quantos dias antes do vencimento legal fica o prazo interno da tarefa.</summary>
    public int DiasAntecedencia { get; private set; }
    /// <summary>Regimes tributários a que se aplica. Vazio: todas as empresas.</summary>
    public List<RegimeTributario> Regimes { get; private set; } = [];
    /// <summary>Responsável usado quando a empresa não tem responsável definido no departamento.</summary>
    public Guid? ResponsavelPadraoId { get; private set; }
    public Usuario? ResponsavelPadrao { get; set; }
    public bool Ativa { get; private set; } = true;
    /// <summary>Quem cadastrou o modelo: aparece como autor das tarefas geradas.</summary>
    public Guid CriadaPorId { get; set; }

    public static ObrigacaoRecorrente Criar(
        string titulo, string? descricao, Departamento departamento, PeriodicidadeObrigacao periodicidade,
        int diaVencimento, int mesesAposCompetencia, int mesReferencia, int diasAntecedencia,
        IEnumerable<RegimeTributario> regimes, Guid? responsavelPadraoId)
    {
        var o = new ObrigacaoRecorrente();
        o.Definir(titulo, descricao, departamento, periodicidade, diaVencimento, mesesAposCompetencia, mesReferencia,
            diasAntecedencia, regimes, responsavelPadraoId, true);
        return o;
    }

    public void Definir(
        string titulo, string? descricao, Departamento departamento, PeriodicidadeObrigacao periodicidade,
        int diaVencimento, int mesesAposCompetencia, int mesReferencia, int diasAntecedencia,
        IEnumerable<RegimeTributario> regimes, Guid? responsavelPadraoId, bool ativa)
    {
        if (string.IsNullOrWhiteSpace(titulo)) throw new DomainException("Informe o título da obrigação.");
        if (diaVencimento is < 1 or > 31) throw new DomainException("O dia de vencimento deve estar entre 1 e 31.");
        if (mesesAposCompetencia is < 0 or > 12) throw new DomainException("O vencimento deve ficar entre 0 e 12 meses após a competência.");
        if (mesReferencia is < 1 or > 12) throw new DomainException("O mês de referência deve estar entre 1 e 12.");
        if (diasAntecedencia is < 0 or > MaxDiasAntecedencia)
            throw new DomainException($"A antecedência do prazo interno deve estar entre 0 e {MaxDiasAntecedencia} dias.");

        Titulo = titulo.Trim();
        Descricao = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim();
        Departamento = departamento;
        Periodicidade = periodicidade;
        DiaVencimento = diaVencimento;
        MesesAposCompetencia = mesesAposCompetencia;
        MesReferencia = mesReferencia;
        DiasAntecedencia = diasAntecedencia;
        Regimes = regimes.Distinct().Order().ToList();
        ResponsavelPadraoId = responsavelPadraoId;
        Ativa = ativa;
    }

    public bool AplicaAoRegime(RegimeTributario regime) => Regimes.Count == 0 || Regimes.Contains(regime);

    /// <summary>A competência (primeiro dia do mês) faz parte da periodicidade?</summary>
    public bool AplicaNaCompetencia(DateOnly competencia) => Periodicidade switch
    {
        PeriodicidadeObrigacao.Mensal => true,
        PeriodicidadeObrigacao.Trimestral => ((competencia.Month - MesReferencia) % 3 + 3) % 3 == 0,
        PeriodicidadeObrigacao.Anual => competencia.Month == MesReferencia,
        _ => false
    };

    /// <summary>Vencimento legal da obrigação na competência.</summary>
    public DateOnly Vencimento(DateOnly competencia)
    {
        var mes = new DateOnly(competencia.Year, competencia.Month, 1).AddMonths(MesesAposCompetencia);
        return new DateOnly(mes.Year, mes.Month, Math.Min(DiaVencimento, DateTime.DaysInMonth(mes.Year, mes.Month)));
    }

    /// <summary>Prazo interno: o vencimento menos a antecedência, nunca antes de <paramref name="hoje"/>.</summary>
    public DateOnly PrazoInterno(DateOnly competencia, DateOnly hoje)
    {
        var prazo = Vencimento(competencia).AddDays(-DiasAntecedencia);
        var vencimento = Vencimento(competencia);
        return prazo < hoje ? (hoje <= vencimento ? hoje : vencimento) : prazo;
    }

    /// <summary>Competências (primeiro dia do mês) cujo vencimento cai entre <paramref name="de"/> e <paramref name="ate"/>.</summary>
    public IEnumerable<DateOnly> CompetenciasComVencimentoEntre(DateOnly de, DateOnly ate)
    {
        var inicio = new DateOnly(de.Year, de.Month, 1).AddMonths(-MesesAposCompetencia);
        var fim = new DateOnly(ate.Year, ate.Month, 1).AddMonths(-MesesAposCompetencia);
        for (var c = inicio; c <= fim; c = c.AddMonths(1))
        {
            if (!AplicaNaCompetencia(c)) continue;
            var v = Vencimento(c);
            if (v >= de && v <= ate) yield return c;
        }
    }
}
