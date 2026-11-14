using System.Globalization;
using System.Text;

namespace Contix.Infrastructure.Pdf;

/// <summary>Código de barras Code 128 (conjunto C, só dígitos), usado na chave de acesso do DANFE.</summary>
public static class Code128
{
    // Larguras barra/espaço de cada símbolo (0 a 105) e do stop (106).
    public static readonly IReadOnlyList<string> Padroes =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
    ];

    private const int StartC = 105;
    private const int Stop = 106;

    /// <summary>Símbolos (start, dados, verificador, stop) para uma sequência de dígitos de tamanho par.</summary>
    public static IReadOnlyList<int> Codificar(string digitos)
    {
        if (digitos.Length == 0 || digitos.Length % 2 != 0 || !digitos.All(char.IsAsciiDigit))
            throw new ArgumentException("O Code 128C exige uma quantidade par de dígitos.", nameof(digitos));

        var simbolos = new List<int> { StartC };
        for (var i = 0; i < digitos.Length; i += 2)
            simbolos.Add(int.Parse(digitos.AsSpan(i, 2), CultureInfo.InvariantCulture));

        var soma = StartC;
        for (var i = 1; i < simbolos.Count; i++) soma += i * simbolos[i];
        simbolos.Add(soma % 103);
        simbolos.Add(Stop);
        return simbolos;
    }

    /// <summary>Larguras (em módulos) das barras e espaços alternados, começando por uma barra.</summary>
    public static IReadOnlyList<int> Larguras(string digitos) =>
        string.Concat(Codificar(digitos).Select(s => Padroes[s])).Select(c => c - '0').ToList();

    /// <summary>SVG com as barras (sem texto), na proporção dos módulos.</summary>
    public static string Svg(string digitos, int altura = 40)
    {
        var larguras = string.Concat(Codificar(digitos).Select(s => Padroes[s]));
        var total = larguras.Sum(c => c - '0') + 20; // zona de silêncio de 10 módulos de cada lado
        var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{total}\" height=\"{altura}\" viewBox=\"0 0 {total} {altura}\">");
        var x = 10;
        for (var i = 0; i < larguras.Length; i++)
        {
            var w = larguras[i] - '0';
            if (i % 2 == 0) svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{x}\" y=\"0\" width=\"{w}\" height=\"{altura}\" fill=\"#000\"/>");
            x += w;
        }
        return svg.Append("</svg>").ToString();
    }
}
