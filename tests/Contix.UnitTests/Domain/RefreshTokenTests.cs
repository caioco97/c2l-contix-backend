using Contix.Domain.Entities;

namespace Contix.UnitTests.Domain;

public class RefreshTokenTests
{
    [Fact]
    public void Revogar_registra_motivo_e_desativa()
    {
        var agora = DateTime.UtcNow;
        var token = new RefreshToken { ExpiraEm = agora.AddDays(1) };
        Assert.True(token.EstaAtivo(agora));

        token.Revogar(agora, "Logout", "hash-novo");

        Assert.False(token.EstaAtivo(agora));
        Assert.Equal("Logout", token.MotivoRevogacao);
        Assert.Equal("hash-novo", token.SubstituidoPorHash);
    }

    [Fact]
    public void Expirado_nao_esta_ativo()
    {
        var agora = DateTime.UtcNow;
        Assert.False(new RefreshToken { ExpiraEm = agora.AddSeconds(-1) }.EstaAtivo(agora));
    }
}
