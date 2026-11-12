using Contix.Domain.ValueObjects;

namespace Contix.UnitTests.Domain;

public class DocumentoFiscalTests
{
    [Theory]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11222333000181", true)]
    [InlineData("11222333000182", false)]
    [InlineData("00000000000000", false)]
    [InlineData("123", false)]
    [InlineData(null, false)]
    public void CnpjValido(string? cnpj, bool esperado) => Assert.Equal(esperado, DocumentoFiscal.CnpjValido(cnpj));

    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224725", true)]
    [InlineData("52998224724", false)]
    [InlineData("11111111111", false)]
    public void CpfValido(string cpf, bool esperado) => Assert.Equal(esperado, DocumentoFiscal.CpfValido(cpf));

    [Theory]
    [InlineData("52998224725", true)]
    [InlineData("11222333000181", true)]
    [InlineData("1122233300018", false)]
    public void CpfOuCnpjValido(string valor, bool esperado) => Assert.Equal(esperado, DocumentoFiscal.CpfOuCnpjValido(valor));
}
