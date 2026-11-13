using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Pastas;
using Contix.Domain.Entities;
using Contix.Domain.Enums;

namespace Contix.Application.Features.Empresas;

/// <summary>
/// Permissões do usuário atual em uma empresa. O escritório tem acesso irrestrito; o usuário do
/// cliente tem os módulos e as pastas liberados no seu vínculo com a empresa.
/// </summary>
public sealed record PermissoesEmpresa(bool Irrestrito, IReadOnlySet<ModuloPortal> Modulos, bool TodasAsPastas, IReadOnlySet<Guid> PastasRaiz)
{
    public static readonly PermissoesEmpresa Escritorio = new(true, UsuarioEmpresa.TodosOsModulos.ToHashSet(), true, new HashSet<Guid>());

    public static PermissoesEmpresa De(UsuarioEmpresa v) =>
        new(false, v.Modulos.ToHashSet(), v.TodasAsPastas, v.PastaIds.ToHashSet());

    public bool Pode(ModuloPortal modulo) => Irrestrito || Modulos.Contains(modulo);

    /// <summary>O usuário vê todas as pastas da empresa?</summary>
    public bool VeTodasAsPastas => Irrestrito || TodasAsPastas;

    /// <summary>Ids das pastas visíveis (raízes liberadas e suas subpastas), ou null quando todas são visíveis.</summary>
    public IReadOnlySet<Guid>? PastasVisiveis(IReadOnlyList<Pasta> pastasDaEmpresa)
    {
        if (VeTodasAsPastas) return null;
        var mapa = pastasDaEmpresa.ToDictionary(p => p.Id);
        return pastasDaEmpresa.Where(p => PastasRaiz.Contains(PastaTree.Raiz(p.Id, mapa))).Select(p => p.Id).ToHashSet();
    }
}

/// <summary>Garante que o usuário atual pode acessar os dados de uma empresa (e, opcionalmente, um módulo do portal).</summary>
public interface IAcessoEmpresaService
{
    Task<Empresa> GarantirAcessoAsync(Guid empresaId, CancellationToken ct = default);

    /// <summary>Garante o acesso à empresa e ao módulo. Responde 403 quando o módulo não foi liberado ao usuário.</summary>
    Task<(Empresa Empresa, PermissoesEmpresa Permissoes)> GarantirModuloAsync(Guid empresaId, ModuloPortal modulo, CancellationToken ct = default);

    /// <summary>Permissões do usuário atual na empresa (após <see cref="GarantirAcessoAsync"/>).</summary>
    Task<PermissoesEmpresa> ObterPermissoesAsync(Guid empresaId, CancellationToken ct = default);
}

public sealed class AcessoEmpresaService(IEmpresaRepository empresas, ICurrentUser currentUser) : IAcessoEmpresaService
{
    // Cache por requisição (serviço scoped): evita consultar o vínculo várias vezes.
    private readonly Dictionary<Guid, PermissoesEmpresa> _permissoes = [];

    public async Task<Empresa> GarantirAcessoAsync(Guid empresaId, CancellationToken ct = default)
    {
        // O filtro global já restringe ao tenant do usuário.
        var empresa = await empresas.GetByIdAsync(empresaId, ct) ?? throw new NotFoundException("Empresa", empresaId);

        if (!currentUser.EhCliente)
        {
            _permissoes[empresaId] = PermissoesEmpresa.Escritorio;
            return empresa;
        }

        // Usuário do cliente só enxerga empresas às quais está vinculado.
        // Retorna 404 (e não 403) para não revelar a existência da empresa.
        var vinculo = await empresas.ObterVinculoAsync(currentUser.RequireUserId(), empresaId, ct)
                      ?? throw new NotFoundException("Empresa", empresaId);
        _permissoes[empresaId] = PermissoesEmpresa.De(vinculo);
        return empresa;
    }

    public async Task<(Empresa Empresa, PermissoesEmpresa Permissoes)> GarantirModuloAsync(Guid empresaId, ModuloPortal modulo, CancellationToken ct = default)
    {
        var empresa = await GarantirAcessoAsync(empresaId, ct);
        var permissoes = _permissoes[empresaId];
        if (!permissoes.Pode(modulo))
            throw new ForbiddenException($"Seu usuário não tem acesso ao módulo {NomeModulo(modulo)} desta empresa.");
        return (empresa, permissoes);
    }

    public async Task<PermissoesEmpresa> ObterPermissoesAsync(Guid empresaId, CancellationToken ct = default)
    {
        if (!_permissoes.TryGetValue(empresaId, out var p))
        {
            await GarantirAcessoAsync(empresaId, ct);
            p = _permissoes[empresaId];
        }
        return p;
    }

    public static string NomeModulo(ModuloPortal m) => m switch
    {
        ModuloPortal.Documentos => "Documentos",
        ModuloPortal.Solicitacoes => "Solicitações",
        ModuloPortal.NFe => "NF-e",
        _ => m.ToString()
    };
}
