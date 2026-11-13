using Contix.Domain.Entities;
using Contix.Domain.Enums;

namespace Contix.UnitTests.Domain;

public class DocumentoEventoTests
{
    [Fact]
    public void Criar_guarda_usuario_data_e_ip()
    {
        var quando = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        var usuario = Guid.NewGuid();
        var evento = DocumentoEvento.Criar(Guid.NewGuid(), TipoEventoDocumento.Download, usuario, quando, ip: "203.0.113.45");

        Assert.Equal(usuario, evento.UsuarioId);
        Assert.Equal(quando, evento.OcorridoEm);
        Assert.Equal("203.0.113.45", evento.Ip);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_sem_ip_fica_nulo(string? ip) =>
        Assert.Null(DocumentoEvento.Criar(Guid.NewGuid(), TipoEventoDocumento.Upload, Guid.NewGuid(), DateTime.UtcNow, ip: ip).Ip);

    [Fact]
    public void Criar_limita_o_tamanho_do_ip()
    {
        var evento = DocumentoEvento.Criar(Guid.NewGuid(), TipoEventoDocumento.Upload, Guid.NewGuid(), DateTime.UtcNow, ip: new string('a', 100));
        Assert.Equal(DocumentoEvento.TamanhoMaximoIp, evento.Ip!.Length);
    }
}
