using Contix.Api.Infrastructure;
using Contix.Application.Features.Usuarios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Usuários do portal do cliente de uma empresa e as permissões deles (somente escritório).</summary>
[Route("api/v1/empresas/{empresaId:guid}/usuarios")]
[Authorize(Policy = Policies.Escritorio)]
public sealed class EmpresaUsuariosController(IUsuarioPortalService service) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UsuarioPortalDto>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<UsuarioPortalDto>> Listar(Guid empresaId, CancellationToken ct) => service.ListarAsync(empresaId, ct);

    /// <summary>
    /// Convida um usuário por e-mail para o portal da empresa. Se já existir um usuário do cliente
    /// com este e-mail no escritório, ele passa a acessar também esta empresa.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(UsuarioPortalDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UsuarioPortalDto>> Convidar(Guid empresaId, ConvidarUsuarioPortalRequest request, CancellationToken ct)
    {
        var usuario = await service.ConvidarAsync(empresaId, request, ct);
        return StatusCode(StatusCodes.Status201Created, usuario);
    }

    /// <summary>Define os módulos e as pastas que o usuário acessa nesta empresa.</summary>
    [HttpPut("{usuarioId:guid}")]
    [ProducesResponseType(typeof(UsuarioPortalDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<UsuarioPortalDto> AtualizarPermissoes(Guid empresaId, Guid usuarioId, PermissoesPortalRequest request, CancellationToken ct) =>
        service.AtualizarPermissoesAsync(empresaId, usuarioId, request, ct);

    /// <summary>Remove o acesso do usuário a esta empresa.</summary>
    [HttpDelete("{usuarioId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Desvincular(Guid empresaId, Guid usuarioId, CancellationToken ct)
    {
        await service.DesvincularAsync(empresaId, usuarioId, ct);
        return NoContent();
    }
}
