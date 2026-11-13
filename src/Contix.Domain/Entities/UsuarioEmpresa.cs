using Contix.Domain.Enums;
using Contix.Domain.Exceptions;

namespace Contix.Domain.Entities;

/// <summary>
/// Vínculo entre um usuário do portal do cliente e uma empresa, com as permissões
/// do usuário naquela empresa (módulos e pastas de documentos).
/// </summary>
public class UsuarioEmpresa
{
    public static readonly IReadOnlyList<ModuloPortal> TodosOsModulos = Enum.GetValues<ModuloPortal>();

    public Guid UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }
    public DateTime VinculadoEm { get; set; }

    /// <summary>Módulos do portal liberados para o usuário nesta empresa.</summary>
    public List<ModuloPortal> Modulos { get; private set; } = [.. TodosOsModulos];

    /// <summary>Quando falso, o usuário só vê as pastas raiz listadas em <see cref="PastaIds"/> (e as subpastas delas).</summary>
    public bool TodasAsPastas { get; private set; } = true;

    /// <summary>Pastas raiz liberadas quando <see cref="TodasAsPastas"/> é falso.</summary>
    public List<Guid> PastaIds { get; private set; } = [];

    public bool PodeAcessar(ModuloPortal modulo) => Modulos.Contains(modulo);

    public void DefinirPermissoes(IEnumerable<ModuloPortal> modulos, bool todasAsPastas, IEnumerable<Guid>? pastaIds)
    {
        var lista = modulos.Distinct().Order().ToList();
        var pastas = todasAsPastas ? [] : (pastaIds ?? []).Distinct().ToList();
        if (lista.Contains(ModuloPortal.Documentos) && !todasAsPastas && pastas.Count == 0)
            throw new DomainException("Selecione ao menos uma pasta ou libere todas as pastas.");

        Modulos = lista;
        TodasAsPastas = todasAsPastas;
        PastaIds = pastas;
    }
}
