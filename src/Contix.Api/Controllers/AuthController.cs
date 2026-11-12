using Contix.Application.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Contix.Api.Controllers;

/// <summary>Autenticação com JWT e refresh token rotativo.</summary>
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService auth) : ApiControllerBase
{
    /// <summary>Autentica com e-mail e senha.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public Task<AuthResponse> Login(LoginRequest request, CancellationToken ct) => auth.LoginAsync(request, ct);

    /// <summary>Troca um refresh token válido por um novo par de tokens (o anterior é revogado).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public Task<AuthResponse> Refresh(RefreshTokenRequest request, CancellationToken ct) => auth.RefreshAsync(request, ct);

    /// <summary>Revoga o refresh token informado.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken ct)
    {
        await auth.LogoutAsync(request, ct);
        return NoContent();
    }

    /// <summary>
    /// Solicita o link de redefinição de senha por e-mail. A resposta é sempre 202,
    /// exista ou não uma conta com o e-mail informado.
    /// </summary>
    [HttpPost("esqueci-senha")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> EsqueciSenha(EsqueciSenhaRequest request, CancellationToken ct)
    {
        await auth.SolicitarRedefinicaoSenhaAsync(request, ct);
        return Accepted();
    }

    /// <summary>Define a nova senha com o token recebido por e-mail (uso único). Encerra as sessões abertas.</summary>
    [HttpPost("redefinir-senha")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RedefinirSenha(RedefinirSenhaRequest request, CancellationToken ct)
    {
        await auth.RedefinirSenhaAsync(request, ct);
        return NoContent();
    }

    /// <summary>Onboarding: cria um escritório contábil e o usuário administrador.</summary>
    [HttpPost("registrar")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Registrar(RegistrarEscritorioRequest request, CancellationToken ct)
    {
        var response = await auth.RegistrarEscritorioAsync(request, ct);
        return CreatedAtAction(nameof(Me), response);
    }

    /// <summary>Dados do usuário autenticado.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UsuarioAutenticadoDto), StatusCodes.Status200OK)]
    public Task<UsuarioAutenticadoDto> Me(CancellationToken ct) => auth.ObterUsuarioAtualAsync(ct);
}
