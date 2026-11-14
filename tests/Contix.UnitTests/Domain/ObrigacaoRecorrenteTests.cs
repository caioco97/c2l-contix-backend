using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;

namespace Contix.UnitTests.Domain;

public class ObrigacaoRecorrenteTests
{
    private static ObrigacaoRecorrente Nova(PeriodicidadeObrigacao p = PeriodicidadeObrigacao.Mensal, int dia = 20, int mesesApos = 1,
        int mesReferencia = 1, int antecedencia = 5, params RegimeTributario[] regimes) =>
        ObrigacaoRecorrente.Criar("DCTFWeb", null, Departamento.Pessoal, p, dia, mesesApos, mesReferencia, antecedencia, regimes, null);

    [Fact]
    public void Vencimento_no_mes_seguinte_respeita_o_ultimo_dia_do_mes()
    {
        var o = Nova(dia: 31);
        Assert.Equal(new DateOnly(2026, 2, 28), o.Vencimento(new DateOnly(2026, 1, 1)));
        Assert.Equal(new DateOnly(2026, 12, 31), o.Vencimento(new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public void Prazo_interno_usa_a_antecedencia_sem_ficar_no_passado()
    {
        var o = Nova(dia: 20, antecedencia: 5);
        var competencia = new DateOnly(2026, 10, 1); // vence em 20/11
        Assert.Equal(new DateOnly(2026, 11, 15), o.PrazoInterno(competencia, new DateOnly(2026, 11, 1)));
        Assert.Equal(new DateOnly(2026, 11, 18), o.PrazoInterno(competencia, new DateOnly(2026, 11, 18)));
    }

    [Fact]
    public void Trimestral_e_anual_geram_so_nas_competencias_da_periodicidade()
    {
        var trimestral = Nova(PeriodicidadeObrigacao.Trimestral, mesReferencia: 3);
        Assert.Equal([3, 6, 9, 12], Enumerable.Range(1, 12).Where(m => trimestral.AplicaNaCompetencia(new DateOnly(2026, m, 1))));

        var anual = Nova(PeriodicidadeObrigacao.Anual, mesReferencia: 12, mesesApos: 3, dia: 31);
        var competencias = anual.CompetenciasComVencimentoEntre(new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31)).ToList();
        Assert.Equal([new DateOnly(2026, 12, 1)], competencias);
        Assert.Equal(new DateOnly(2027, 3, 31), anual.Vencimento(competencias[0]));
    }

    [Fact]
    public void Competencias_na_janela_de_geracao()
    {
        var o = Nova(dia: 20);
        var c = o.CompetenciasComVencimentoEntre(new DateOnly(2026, 11, 14), new DateOnly(2026, 12, 29)).ToList();
        Assert.Equal([new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1)], c);
    }

    [Fact]
    public void Regimes_vazios_aplicam_a_todas_as_empresas()
    {
        Assert.True(Nova().AplicaAoRegime(RegimeTributario.LucroReal));
        var simples = Nova(regimes: RegimeTributario.SimplesNacional);
        Assert.True(simples.AplicaAoRegime(RegimeTributario.SimplesNacional));
        Assert.False(simples.AplicaAoRegime(RegimeTributario.LucroPresumido));
    }

    [Theory]
    [InlineData(0, 1, 1, 0)]
    [InlineData(32, 1, 1, 0)]
    [InlineData(10, 13, 1, 0)]
    [InlineData(10, 1, 0, 0)]
    [InlineData(10, 1, 1, 61)]
    public void Valores_invalidos_sao_recusados(int dia, int mesesApos, int mesReferencia, int antecedencia) =>
        Assert.Throws<DomainException>(() => Nova(dia: dia, mesesApos: mesesApos, mesReferencia: mesReferencia, antecedencia: antecedencia));

    [Fact]
    public void Tarefa_registra_vinculos_e_entrega()
    {
        var t = Tarefa.Criar(Guid.NewGuid(), Guid.NewGuid(), 1, "Folha", null, Departamento.Pessoal, Guid.NewGuid(),
            new DateOnly(2026, 11, 20), null, Guid.NewGuid(), DateTime.UtcNow);
        var solicitacao = Guid.NewGuid();
        t.VincularSolicitacao(solicitacao);
        Assert.Throws<DomainException>(() => t.VincularSolicitacao(Guid.NewGuid()));
        t.VincularObrigacao(Guid.NewGuid(), new DateOnly(2026, 10, 17));
        Assert.Equal(new DateOnly(2026, 10, 1), t.Competencia);
        var doc = Guid.NewGuid();
        t.RegistrarEntrega([doc, doc]);
        Assert.Equal([doc], t.DocumentosEntregues);
    }
}
