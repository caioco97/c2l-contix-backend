using Contix.Application.Features.Auth;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.MeuPerfil;
using Microsoft.AspNetCore.Mvc;

namespace Contix.Api.Controllers;

/// <summary>Meu perfil: dados e foto do usuário logado (escritório ou cliente).</summary>
[Route("api/v1/perfil")]
public sealed class PerfilController(IPerfilService perfil) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(UsuarioAutenticadoDto), StatusCodes.Status200OK)]
    public Task<UsuarioAutenticadoDto> Obter(CancellationToken ct) => perfil.ObterAsync(ct);

    /// <summary>Atualiza nome, e-mail e telefone. O e-mail precisa ser único na plataforma.</summary>
    [HttpPut]
    [ProducesResponseType(typeof(UsuarioAutenticadoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<UsuarioAutenticadoDto> Atualizar(AtualizarPerfilRequest request, CancellationToken ct) =>
        perfil.AtualizarAsync(request, ct);

    /// <summary>Envia (ou troca) a foto de perfil: PNG, JPG ou WEBP de até 1 MB.</summary>
    [HttpPut("foto")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    [ProducesResponseType(typeof(UsuarioAutenticadoDto), StatusCodes.Status200OK)]
    public Task<UsuarioAutenticadoDto> EnviarFoto(IFormFile arquivo, CancellationToken ct) =>
        perfil.EnviarFotoAsync(new ArquivoUpload(arquivo.FileName, arquivo.ContentType, arquivo.Length, arquivo.OpenReadStream), ct);

    [HttpDelete("foto")]
    [ProducesResponseType(typeof(UsuarioAutenticadoDto), StatusCodes.Status200OK)]
    public Task<UsuarioAutenticadoDto> RemoverFoto(CancellationToken ct) => perfil.RemoverFotoAsync(ct);

    [HttpGet("foto")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Foto(CancellationToken ct)
    {
        var foto = await perfil.ObterFotoAsync(ct);
        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(foto.Conteudo, foto.ContentType);
    }
}
