using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;

namespace Contix.UnitTests.Domain;

public sealed class PortalAvisosTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 13);

    [Theory]
    [InlineData(31, false, false, false, null)]
    [InlineData(30, false, false, false, MarcoAvisoCertidao.Dias30)]
    [InlineData(16, true, false, false, null)]
    [InlineData(15, true, false, false, MarcoAvisoCertidao.Dias15)]
    [InlineData(0, true, false, false, MarcoAvisoCertidao.Dias15)]
    [InlineData(5, true, true, false, null)]
    [InlineData(-1, true, true, false, MarcoAvisoCertidao.Vencida)]
    [InlineData(-1, true, true, true, null)]
    [InlineData(-31, false, false, false, null)]
    public void Marco_de_aviso_da_certidao(int dias, bool a30, bool a15, bool aVencida, MarcoAvisoCertidao? esperado) =>
        Assert.Equal(esperado, Certidao.MarcoDevido(Hoje.AddDays(dias), Hoje, a30, a15, aVencida));

    [Fact]
    public void Mudar_a_validade_zera_os_avisos()
    {
        var c = new Certidao();
        c.DefinirDatas(Hoje.AddDays(-100), Hoje.AddDays(10));
        c.RegistrarAviso(MarcoAvisoCertidao.Dias15, DateTime.UtcNow);
        Assert.NotNull(c.AvisoVencimento30Em);
        Assert.NotNull(c.AvisoVencimento15Em);

        c.DefinirDatas(Hoje.AddDays(-100), Hoje.AddDays(10)); // mesma validade: mantém
        Assert.NotNull(c.AvisoVencimento15Em);
        c.DefinirDatas(Hoje, Hoje.AddDays(180));
        Assert.Null(c.AvisoVencimento30Em);
        Assert.Null(c.AvisoVencimento15Em);
    }

    [Fact]
    public void Permissoes_do_vinculo()
    {
        var v = new UsuarioEmpresa();
        Assert.True(v.PodeAcessar(ModuloPortal.NFe));
        Assert.True(v.TodasAsPastas);

        var pasta = Guid.NewGuid();
        v.DefinirPermissoes([ModuloPortal.Documentos, ModuloPortal.Documentos], false, [pasta, pasta]);
        Assert.Equal([ModuloPortal.Documentos], v.Modulos);
        Assert.Equal([pasta], v.PastaIds);
        Assert.False(v.PodeAcessar(ModuloPortal.Solicitacoes));

        Assert.Throws<DomainException>(() => v.DefinirPermissoes([ModuloPortal.Documentos], false, []));
        v.DefinirPermissoes([ModuloPortal.Solicitacoes], true, [pasta]);
        Assert.Empty(v.PastaIds);
    }

    [Fact]
    public void Convite_fica_pendente_ate_o_usuario_definir_a_senha()
    {
        var u = new Usuario();
        var agora = DateTime.UtcNow;
        u.RegistrarConvite(agora);
        u.RegistrarConvite(agora.AddMinutes(1)); // reenvio
        Assert.True(u.ConvitePendente);

        u.RedefinirSenha("hash", agora.AddMinutes(2));
        Assert.False(u.ConvitePendente);
        Assert.Throws<DomainException>(() => u.RegistrarConvite(agora.AddMinutes(3)));
    }
}
