using Contix.Domain.Entities;

namespace Contix.UnitTests.Domain;

public class EmpresaTests
{
    [Fact]
    public void Cria_pastas_padrao_do_tenant()
    {
        var tenant = Guid.NewGuid();
        var empresa = new Empresa { TenantId = tenant, RazaoSocial = "X", Documento = "11222333000181" };

        var pastas = empresa.CriarPastasPadrao();

        Assert.Equal(Empresa.PastasPadrao, pastas.Select(p => p.Nome));
        Assert.All(pastas, p =>
        {
            Assert.Equal(tenant, p.TenantId);
            Assert.Equal(empresa.Id, p.EmpresaId);
            Assert.Null(p.PastaPaiId);
        });
    }
}
