using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;

namespace Contix.UnitTests.Domain;

public class SolicitacaoTests
{
    private static readonly DateTime Agora = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Cliente = Guid.NewGuid();
    private static readonly Guid Colaborador = Guid.NewGuid();

    private static Solicitacao Nova()
    {
        var s = new Solicitacao { Tipo = TipoSolicitacao.Geral, Assunto = "Teste", SolicitanteId = Cliente };
        s.RegistrarAbertura(Agora, new DateOnly(2026, 11, 17));
        return s;
    }

    [Fact]
    public void Abertura_registra_historico_e_prazo()
    {
        var s = Nova();

        Assert.Equal(StatusSolicitacao.Aberta, s.Status);
        Assert.Equal(new DateOnly(2026, 11, 17), s.Prazo);
        var abertura = Assert.Single(s.Interacoes);
        Assert.Equal(TipoInteracao.Abertura, abertura.Tipo);
        Assert.Equal(s.Id, abertura.SolicitacaoId);
        Assert.Throws<DomainException>(() => s.RegistrarAbertura(Agora, null));
    }

    [Theory]
    [InlineData(StatusSolicitacao.Concluida)]
    [InlineData(StatusSolicitacao.EmAnalise)]
    [InlineData(StatusSolicitacao.AguardandoCliente)]
    public void Cliente_so_pode_cancelar(StatusSolicitacao destino)
    {
        var s = Nova();
        Assert.Throws<DomainException>(() => s.AlterarStatus(destino, Cliente, autorEhCliente: true, "x", Agora));

        s.AlterarStatus(StatusSolicitacao.Cancelada, Cliente, autorEhCliente: true, null, Agora);
        Assert.Equal(StatusSolicitacao.Cancelada, s.Status);
        Assert.Equal(Agora, s.FinalizadaEm);
    }

    [Fact]
    public void Aguardando_cliente_exige_mensagem()
    {
        var s = Nova();
        Assert.Throws<DomainException>(() =>
            s.AlterarStatus(StatusSolicitacao.AguardandoCliente, Colaborador, false, " ", Agora));
    }

    [Fact]
    public void Resposta_do_cliente_devolve_para_em_analise()
    {
        var s = Nova();
        s.AlterarStatus(StatusSolicitacao.AguardandoCliente, Colaborador, false, "Envie o atestado", Agora);

        s.Comentar(Cliente, autorEhCliente: true, "Segue", possuiAnexos: true, Agora.AddMinutes(5));

        Assert.Equal(StatusSolicitacao.EmAnalise, s.Status);
        var ultimas = s.Interacoes.OrderBy(i => i.CreatedAt).TakeLast(2).ToList();
        Assert.Equal(TipoInteracao.Comentario, ultimas[0].Tipo);
        Assert.Equal(TipoInteracao.MudancaStatus, ultimas[1].Tipo);
        Assert.Equal(StatusSolicitacao.AguardandoCliente, ultimas[1].StatusAnterior);
    }

    [Fact]
    public void Comentario_do_escritorio_nao_muda_status()
    {
        var s = Nova();
        s.AlterarStatus(StatusSolicitacao.AguardandoCliente, Colaborador, false, "Envie o atestado", Agora);
        s.Comentar(Colaborador, autorEhCliente: false, "Lembrete", possuiAnexos: false, Agora);
        Assert.Equal(StatusSolicitacao.AguardandoCliente, s.Status);
    }

    [Fact]
    public void Finalizada_nao_aceita_comentario_do_cliente_e_pode_ser_reaberta_pelo_escritorio()
    {
        var s = Nova();
        s.AlterarStatus(StatusSolicitacao.Concluida, Colaborador, false, null, Agora);

        Assert.Throws<DomainException>(() => s.Comentar(Cliente, true, "Oi", false, Agora));
        Assert.Empty(Solicitacao.TransicoesPermitidas(s.Status, ehCliente: true));

        s.AlterarStatus(StatusSolicitacao.EmAnalise, Colaborador, false, "Reaberta", Agora);
        Assert.Null(s.FinalizadaEm);
    }

    [Fact]
    public void Comentario_vazio_e_rejeitado()
    {
        var s = Nova();
        Assert.Throws<DomainException>(() => s.Comentar(Cliente, true, "  ", possuiAnexos: false, Agora));
    }

    [Fact]
    public void Atrasada_quando_prazo_vencido_e_nao_finalizada()
    {
        var s = Nova();
        Assert.False(s.EstaAtrasada(new DateOnly(2026, 11, 17)));
        Assert.True(s.EstaAtrasada(new DateOnly(2026, 11, 18)));

        s.AlterarStatus(StatusSolicitacao.Concluida, Colaborador, false, null, Agora);
        Assert.False(s.EstaAtrasada(new DateOnly(2026, 11, 18)));
    }
}
