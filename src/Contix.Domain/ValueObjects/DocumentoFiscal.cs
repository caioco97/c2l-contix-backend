namespace Contix.Domain.ValueObjects;

/// <summary>Validação e normalização de CPF/CNPJ.</summary>
public static class DocumentoFiscal
{
    public static string SomenteDigitos(string? valor) =>
        valor is null ? string.Empty : new string(valor.Where(char.IsDigit).ToArray());

    public static bool CnpjValido(string? valor)
    {
        var cnpj = SomenteDigitos(valor);
        if (cnpj.Length != 14 || cnpj.Distinct().Count() == 1) return false;

        int[] m1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] m2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        return Digito(cnpj[..12], m1) == cnpj[12] - '0'
            && Digito(cnpj[..13], m2) == cnpj[13] - '0';
    }

    public static bool CpfValido(string? valor)
    {
        var cpf = SomenteDigitos(valor);
        if (cpf.Length != 11 || cpf.Distinct().Count() == 1) return false;

        int[] m1 = [10, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] m2 = [11, 10, 9, 8, 7, 6, 5, 4, 3, 2];
        return Digito(cpf[..9], m1) == cpf[9] - '0'
            && Digito(cpf[..10], m2) == cpf[10] - '0';
    }

    public static bool CpfOuCnpjValido(string? valor)
    {
        var d = SomenteDigitos(valor);
        return d.Length switch { 11 => CpfValido(d), 14 => CnpjValido(d), _ => false };
    }

    private static int Digito(string baseNum, int[] mult)
    {
        var soma = 0;
        for (var i = 0; i < mult.Length; i++) soma += (baseNum[i] - '0') * mult[i];
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
