using Contix.Domain.ValueObjects;

namespace Contix.UnitTests.Domain;

public class DadosBrasileirosTests
{
    [Theory]
    [InlineData("01310-100", "01310100")]
    [InlineData("01310100", "01310100")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizarCep(string? cep, string? esperado) => Assert.Equal(esperado, DadosBrasileiros.NormalizarCep(cep));

    [Theory]
    [InlineData("01310-100", true)]
    [InlineData("0131010", false)]
    [InlineData("00000000", false)]
    public void CepValido(string cep, bool esperado) => Assert.Equal(esperado, DadosBrasileiros.CepValido(cep));

    [Theory]
    [InlineData("+55 (11) 91234-5678", "11912345678")]
    [InlineData("(11) 4000-0000", "1140000000")]
    [InlineData("5511912345678", "11912345678")]
    [InlineData("11912345678", "11912345678")]
    [InlineData("  ", null)]
    public void NormalizarTelefone(string telefone, string? esperado) =>
        Assert.Equal(esperado, DadosBrasileiros.NormalizarTelefone(telefone));

    [Theory]
    [InlineData("+55 (11) 91234-5678", true)]
    [InlineData("(11) 4000-0000", true)]
    [InlineData("(11) 81234-5678", false)]
    [InlineData("(01) 91234-5678", false)]
    [InlineData("(11) 1000-0000", false)]
    [InlineData("1191234", false)]
    public void TelefoneValido(string telefone, bool esperado) => Assert.Equal(esperado, DadosBrasileiros.TelefoneValido(telefone));

    [Theory]
    [InlineData("120.69870.68-7", true)]
    [InlineData("12069870687", true)]
    [InlineData("12069870686", false)]
    [InlineData("11111111111", false)]
    [InlineData("1206987068", false)]
    public void PisValido(string pis, bool esperado) => Assert.Equal(esperado, DadosBrasileiros.PisValido(pis));

    [Theory]
    [InlineData("110.042.490.114", "110042490114")]
    [InlineData("isento", "ISENTO")]
    [InlineData("P-01100424.3/002", "P011004243002")]
    [InlineData("", null)]
    public void NormalizarInscricaoEstadual(string ie, string? esperado) =>
        Assert.Equal(esperado, DadosBrasileiros.NormalizarInscricaoEstadual(ie));

    [Theory]
    [InlineData("110.042.490.114", true)]
    [InlineData("ISENTO", true)]
    [InlineData("P-01100424.3/002", true)]
    [InlineData("ABC123", false)]
    [InlineData("1", false)]
    [InlineData("123456789012345", false)]
    public void InscricaoEstadualValida(string ie, bool esperado) =>
        Assert.Equal(esperado, DadosBrasileiros.InscricaoEstadualValida(ie));

    [Theory]
    [InlineData("SP", true)]
    [InlineData("sp", true)]
    [InlineData("XX", false)]
    [InlineData(null, false)]
    public void UfValida(string? uf, bool esperado) => Assert.Equal(esperado, DadosBrasileiros.UfValida(uf));
}
