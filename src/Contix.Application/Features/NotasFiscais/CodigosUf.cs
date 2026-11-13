namespace Contix.Application.Features.NotasFiscais;

/// <summary>Códigos IBGE das UFs (cUF/cUFAutor da NF-e).</summary>
public static class CodigosUf
{
    private static readonly Dictionary<string, int> Codigos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RO"] = 11, ["AC"] = 12, ["AM"] = 13, ["RR"] = 14, ["PA"] = 15, ["AP"] = 16, ["TO"] = 17,
        ["MA"] = 21, ["PI"] = 22, ["CE"] = 23, ["RN"] = 24, ["PB"] = 25, ["PE"] = 26, ["AL"] = 27, ["SE"] = 28, ["BA"] = 29,
        ["MG"] = 31, ["ES"] = 32, ["RJ"] = 33, ["SP"] = 35,
        ["PR"] = 41, ["SC"] = 42, ["RS"] = 43,
        ["MS"] = 50, ["MT"] = 51, ["GO"] = 52, ["DF"] = 53
    };

    public static int? Codigo(string? uf) => uf is not null && Codigos.TryGetValue(uf.Trim(), out var c) ? c : null;
}
