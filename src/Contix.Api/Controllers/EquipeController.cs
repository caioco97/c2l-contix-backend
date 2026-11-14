using Contix.Api.Infrastructure;
using Contix.Application.Features.Equipe;
using Contix.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Departamentos do escritório e responsáveis por empresa e departamento.</summary>
[Route("api/v1")]
[Authorize(Policy = Policies.Escritorio)]
public sealed class EquipeController(IEquipeService service) : ApiControllerBase
{
    /// <summary>Os cinco departamentos (Contábil, Fiscal, Pessoal, Societário, Financeiro) com o nome e a situação no escritório.</summary>
    [HttpGet("departamentos")]
    [ProducesResponseType(typeof(IReadOnlyList<DepartamentoDto>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<DepartamentoDto>> ListarDepartamentos(CancellationToken ct) => service.ListarDepartamentosAsync(ct);

    /// <summary>Renomeia ou ativa/desativa um departamento. Somente administradores.</summary>
    [HttpPut("departamentos/{codigo}")]
    [Authorize(Policy = Policies.Administrador)]
    [ProducesResponseType(typeof(DepartamentoDto), StatusCodes.Status200OK)]
    public Task<DepartamentoDto> AtualizarDepartamento(Departamento codigo, AtualizarDepartamentoRequest request, CancellationToken ct) =>
        service.AtualizarDepartamentoAsync(codigo, request, ct);

    /// <summary>Responsável da empresa em cada departamento.</summary>
    [HttpGet("empresas/{empresaId:guid}/responsaveis")]
    [ProducesResponseType(typeof(IReadOnlyList<ResponsavelEmpresaDto>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ResponsavelEmpresaDto>> ListarResponsaveis(Guid empresaId, CancellationToken ct) =>
        service.ListarResponsaveisAsync(empresaId, ct);

    /// <summary>Define os responsáveis. Solicitações novas do departamento passam a ser atribuídas a eles.</summary>
    [HttpPut("empresas/{empresaId:guid}/responsaveis")]
    [ProducesResponseType(typeof(IReadOnlyList<ResponsavelEmpresaDto>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ResponsavelEmpresaDto>> DefinirResponsaveis(Guid empresaId, DefinirResponsaveisRequest request, CancellationToken ct) =>
        service.DefinirResponsaveisAsync(empresaId, request, ct);
}
