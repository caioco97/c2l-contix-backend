namespace Contix.Domain.ValueObjects;

/// <summary>
/// Normalização e validação de CEP, telefone, PIS, inscrições e UF.
/// Tudo é guardado sem máscara (somente dígitos); a máscara é responsabilidade do front.
/// </summary>
public static class DadosBrasileiros
{
    public static readonly IReadOnlySet<string> Ufs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA",
        "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"
    };

    private static string? Nulo(string valor) => valor.Length == 0 ? null : valor;

    /// <summary>"01310-100" → "01310100". Vazio vira null.</summary>
    public static string? NormalizarCep(string? valor) => Nulo(DocumentoFiscal.SomenteDigitos(valor));

    public static bool CepValido(string? valor) =>
        DocumentoFiscal.SomenteDigitos(valor) is { Length: 8 } d && d.Distinct().Count() > 1;

    /// <summary>"+55 (11) 91234-5678" → "11912345678" (DDD + número, sem o DDI).</summary>
    public static string? NormalizarTelefone(string? valor)
    {
        var bruto = valor?.Trim() ?? string.Empty;
        var d = DocumentoFiscal.SomenteDigitos(bruto.StartsWith("+55") ? bruto[3..] : bruto);
        if (d.Length is 12 or 13 && d.StartsWith("55")) d = d[2..];
        return Nulo(d);
    }

    /// <summary>Fixo (10 dígitos) ou celular (11 dígitos começando com 9), com DDD válido.</summary>
    public static bool TelefoneValido(string? valor)
    {
        var d = NormalizarTelefone(valor) ?? string.Empty;
        if (d.Length < 10 || d[0] == '0' || d[1] == '0') return false;
        return d.Length switch
        {
            10 => d[2] is >= '2' and <= '5',
            11 => d[2] == '9',
            _ => false
        };
    }

    /// <summary>PIS/PASEP/NIT com dígito verificador (pesos 3,2,9,8,7,6,5,4,3,2).</summary>
    public static bool PisValido(string? valor)
    {
        var d = DocumentoFiscal.SomenteDigitos(valor);
        if (d.Length != 11 || d.Distinct().Count() == 1) return false;
        int[] pesos = [3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        var soma = 0;
        for (var i = 0; i < pesos.Length; i++) soma += (d[i] - '0') * pesos[i];
        var resto = 11 - soma % 11;
        return (resto >= 10 ? 0 : resto) == d[10] - '0';
    }

    public static string? NormalizarPis(string? valor) => Nulo(DocumentoFiscal.SomenteDigitos(valor));

    /// <summary>Inscrição estadual: só dígitos, "P" inicial do produtor rural de SP ou "ISENTO".</summary>
    public static string? NormalizarInscricaoEstadual(string? valor)
    {
        var limpo = new string((valor ?? string.Empty).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (limpo == "ISENTO") return limpo;
        var rural = limpo.StartsWith('P') ? "P" : string.Empty;
        return Nulo(rural + DocumentoFiscal.SomenteDigitos(limpo));
    }

    public static bool InscricaoEstadualValida(string? valor)
    {
        var limpo = new string((valor ?? string.Empty).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (limpo == "ISENTO") return true;
        var numeros = limpo.StartsWith('P') ? limpo[1..] : limpo;
        return numeros.Length is >= 2 and <= 14 && numeros.All(char.IsDigit);
    }

    public static string? NormalizarInscricaoMunicipal(string? valor) => Nulo(DocumentoFiscal.SomenteDigitos(valor));

    public static bool UfValida(string? valor) => valor is not null && Ufs.Contains(valor.Trim());
}
