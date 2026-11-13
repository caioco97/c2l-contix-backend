using Contix.Api.Infrastructure;
using Contix.Application.Common.Models;
using Contix.Application.Features.Usuarios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Usuários do escritório (administradores/colaboradores) e do portal do cliente.</summary>
[Route("api/v1/usuarios")]
[Authorize(Policy = Policies.Escritorio)]
public sealed class UsuariosController(IUsuarioService service) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<UsuarioDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<UsuarioDto>> Pesquisar([FromQuery] UsuarioFiltro filtro, CancellationToken ct) =>
        service.PesquisarAsync(filtro, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(UsuarioDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<UsuarioDto> Obter(Guid id, CancellationToken ct) => service.ObterAsync(id, ct);

    /// <summary>Cria um usuário. Sem senha, o usuário recebe um convite por e-mail. Colaboradores só podem criar usuários do cliente.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(UsuarioDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UsuarioDto>> Criar(CriarUsuarioRequest request, CancellationToken ct)
    {
        var usuario = await service.CriarAsync(request, ct);
        return CreatedAtAction(nameof(Obter), new { id = usuario.Id }, usuario);
    }

    /// <summary>Atualiza nome, perfil, situação e empresas vinculadas.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(UsuarioDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<UsuarioDto> Atualizar(Guid id, AtualizarUsuarioRequest request, CancellationToken ct) =>
        service.AtualizarAsync(id, request, ct);

    /// <summary>Reenvia o convite de primeiro acesso por e-mail.</summary>
    [HttpPost("{id:guid}/convite")]
    [ProducesResponseType(typeof(UsuarioDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<UsuarioDto> ReenviarConvite(Guid id, CancellationToken ct) => service.ReenviarConviteAsync(id, ct);

    /// <summary>Ativa ou inativa o acesso do usuário à plataforma.</summary>
    [HttpPut("{id:guid}/situacao")]
    [ProducesResponseType(typeof(UsuarioDto), StatusCodes.Status200OK)]
    public Task<UsuarioDto> AlterarSituacao(Guid id, AlterarSituacaoUsuarioRequest request, CancellationToken ct) =>
        service.AlterarSituacaoAsync(id, request, ct);
}
