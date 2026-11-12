using Contix.Domain.Entities;

namespace Contix.UnitTests.Domain;

public class UsuarioTests
{
    [Fact]
    public void Bloqueia_apos_maximo_de_tentativas()
    {
        var agora = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var usuario = new Usuario();

        for (var i = 0; i < Usuario.MaxTentativasLogin - 1; i++) usuario.RegistrarFalhaLogin(agora);
        Assert.False(usuario.EstaBloqueado(agora));

        usuario.RegistrarFalhaLogin(agora);
        Assert.True(usuario.EstaBloqueado(agora));
        Assert.False(usuario.EstaBloqueado(agora + Usuario.DuracaoBloqueio + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Login_com_sucesso_zera_tentativas()
    {
        var agora = DateTime.UtcNow;
        var usuario = new Usuario();
        usuario.RegistrarFalhaLogin(agora);
        usuario.RegistrarLoginComSucesso(agora);

        Assert.Equal(0, usuario.TentativasLoginFalhas);
        Assert.Equal(agora, usuario.UltimoLoginEm);
    }
}
