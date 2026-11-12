using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Domain.Entities;

namespace Contix.Application.Features.Empresas;

/// <summary>Garante que o usuário atual pode acessar os dados de uma empresa.</summary>
public interface IAcessoEmpresaService
{
    Task<Empresa> GarantirAcessoAsync(Guid empresaId, CancellationToken ct = default);
}

public sealed class AcessoEmpresaService(IEmpresaRepository empresas, ICurrentUser currentUser) : IAcessoEmpresaService
{
    public async Task<Empresa> GarantirAcessoAsync(Guid empresaId, CancellationToken ct = default)
    {
        // O filtro global já restringe ao tenant do usuário.
        var empresa = await empresas.GetByIdAsync(empresaId, ct) ?? throw new NotFoundException("Empresa", empresaId);

        // Usuário do cliente só enxerga empresas às quais está vinculado.
        // Retorna 404 (e não 403) para não revelar a existência da empresa.
        if (currentUser.EhCliente && !await empresas.UsuarioVinculadoAsync(currentUser.RequireUserId(), empresaId, ct))
            throw new NotFoundException("Empresa", empresaId);

        return empresa;
    }
}
