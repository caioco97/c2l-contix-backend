using Contix.Domain.Entities;

namespace Contix.Application.Features.Pastas;

/// <summary>Utilitários para montar a árvore e o caminho das pastas.</summary>
public static class PastaTree
{
    public static IReadOnlyList<PastaDto> Montar(IReadOnlyList<Pasta> pastas, IReadOnlyDictionary<Guid, int> naoLidos)
    {
        var porPai = pastas.ToLookup(p => p.PastaPaiId);

        IReadOnlyList<PastaDto> Filhos(Guid? paiId) => porPai[paiId]
            .OrderBy(p => p.Ordem).ThenBy(p => p.Nome)
            .Select(p => new PastaDto(p.Id, p.Nome, p.PastaPaiId, p.Ordem, p.PermiteUploadCliente,
                naoLidos.GetValueOrDefault(p.Id), Filhos(p.Id)))
            .ToList();

        return Filhos(null);
    }

    /// <summary>Ex.: "Pessoal/DARF DCTFWeb".</summary>
    public static string Caminho(Guid pastaId, IReadOnlyDictionary<Guid, Pasta> pastas)
    {
        var partes = new Stack<string>();
        Guid? atual = pastaId;
        var guard = 0;
        while (atual is { } id && pastas.TryGetValue(id, out var p) && guard++ < 32)
        {
            partes.Push(p.Nome);
            atual = p.PastaPaiId;
        }
        return string.Join('/', partes);
    }

    /// <summary>Pasta raiz (sem pai) à qual a pasta pertence.</summary>
    public static Guid Raiz(Guid pastaId, IReadOnlyDictionary<Guid, Pasta> pastas)
    {
        var atual = pastaId;
        var guard = 0;
        while (pastas.TryGetValue(atual, out var p) && p.PastaPaiId is { } pai && guard++ < 32)
            atual = pai;
        return atual;
    }
}
