using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;

namespace Contix.UnitTests.Domain;

public class TarefaTests
{
    private static readonly DateTime Agora = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Hoje = DateOnly.FromDateTime(Agora);
    private static readonly Guid Autor = Guid.NewGuid();
    private static readonly Guid Responsavel = Guid.NewGuid();

    private static Tarefa Nova(DateOnly? interno = null, DateOnly? legal = null) =>
        Tarefa.Criar(Guid.NewGuid(), Guid.NewGuid(), 1, "  Apurar ICMS  ", " ", Departamento.Fiscal, Responsavel,
            interno ?? new DateOnly(2026, 11, 15), legal ?? new DateOnly(2026, 11, 20), Autor, Agora);

    [Fact]
    public void Criacao_normaliza_campos_e_registra_historico()
    {
        var t = Nova();

        Assert.Equal("Apurar ICMS", t.Titulo);
        Assert.Null(t.Descricao);
        Assert.Equal(StatusTarefa.Pendente, t.Status);
        var criacao = Assert.Single(t.Interacoes);
        Assert.Equal(TipoInteracaoTarefa.Criacao, criacao.Tipo);
        Assert.Equal(t.Id, criacao.TarefaId);
    }

    [Fact]
    public void Prazo_interno_nao_pode_passar_do_prazo_legal()
    {
        Assert.Throws<DomainException>(() => Nova(new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 20)));
        var t = Nova();
        Assert.Throws<DomainException>(() => t.Editar("x", null, Departamento.Fiscal, Responsavel, "R",
            new DateOnly(2026, 11, 25), new DateOnly(2026, 11, 20), Autor, Agora));
    }

    [Fact]
    public void Atraso_considera_prazos_e_status()
    {
        var t = Nova(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11));
        Assert.True(t.EstaAtrasada(Hoje));
        Assert.True(t.PrazoLegalVencido(Hoje));

        t.AlterarStatus(StatusTarefa.Concluida, Autor, null, Agora);
        Assert.False(t.EstaAtrasada(Hoje));
        Assert.False(t.PrazoLegalVencido(Hoje));
        Assert.Equal(Agora, t.ConcluidaEm);
    }

    [Fact]
    public void Fluxo_de_status_e_reabertura()
    {
        var t = Nova();
        t.AlterarStatus(StatusTarefa.EmAndamento, Autor, null, Agora);
        t.AlterarStatus(StatusTarefa.Concluida, Autor, "Entregue", Agora);

        Assert.Equal([StatusTarefa.EmAndamento], Tarefa.TransicoesPermitidas(t.Status));
        Assert.Throws<DomainException>(() => t.AlterarStatus(StatusTarefa.Cancelada, Autor, null, Agora));
        Assert.Throws<DomainException>(() => t.AlterarStatus(StatusTarefa.Concluida, Autor, null, Agora));

        t.AlterarStatus(StatusTarefa.EmAndamento, Autor, null, Agora);
        Assert.Null(t.ConcluidaEm);
        Assert.Equal(4, t.Interacoes.Count);
    }

    [Fact]
    public void Edicao_registra_somente_o_que_mudou()
    {
        var t = Nova();
        Assert.Null(t.Editar("Apurar ICMS", null, Departamento.Fiscal, Responsavel, "R",
            new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 20), Autor, Agora));

        var novo = Guid.NewGuid();
        var edicao = t.Editar("Apurar ICMS", null, Departamento.Fiscal, novo, "Maria",
            new DateOnly(2026, 11, 16), null, Autor, Agora);

        Assert.NotNull(edicao);
        Assert.Equal("Alterou responsável: Maria; prazo interno: 16/11/2026; prazo legal removido.", edicao.Mensagem);
        Assert.Equal(novo, t.ResponsavelId);
        Assert.Null(t.PrazoLegal);
    }

    [Fact]
    public void Comentario_exige_mensagem_ou_anexo()
    {
        var t = Nova();
        Assert.Throws<DomainException>(() => t.Comentar(Autor, "  ", possuiAnexos: false, Agora));
        Assert.Null(t.Comentar(Autor, null, possuiAnexos: true, Agora).Mensagem);
        Assert.Equal("ok", t.Comentar(Autor, " ok ", possuiAnexos: false, Agora).Mensagem);
    }
}
