using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Common.Validation;
using Contix.Application.Features.Auth;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Usuarios;
using Contix.Domain.Entities;
using Contix.Domain.ValueObjects;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.MeuPerfil;

/// <summary>Dados que o próprio usuário pode alterar na tela Meu perfil.</summary>
public sealed record AtualizarPerfilRequest(string Nome, string Email, string? Telefone);

public sealed class AtualizarPerfilRequestValidator : AbstractValidator<AtualizarPerfilRequest>
{
    public AtualizarPerfilRequestValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().WithMessage("Informe o nome.").MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().WithMessage("Informe o e-mail.").EmailAddress().WithMessage("E-mail inválido.")
            .MaximumLength(200);
        RuleFor(x => x.Telefone).Telefone();
    }
}

public sealed record FotoPerfilArquivo(Stream Conteudo, string ContentType);

/// <summary>Perfil do usuário logado (escritório ou cliente).</summary>
public interface IPerfilService
{
    Task<UsuarioAutenticadoDto> ObterAsync(CancellationToken ct = default);
    Task<UsuarioAutenticadoDto> AtualizarAsync(AtualizarPerfilRequest request, CancellationToken ct = default);
    Task<UsuarioAutenticadoDto> EnviarFotoAsync(ArquivoUpload arquivo, CancellationToken ct = default);
    Task<UsuarioAutenticadoDto> RemoverFotoAsync(CancellationToken ct = default);
    Task<FotoPerfilArquivo> ObterFotoAsync(CancellationToken ct = default);
}

public sealed class PerfilService(
    IUsuarioRepository usuarios,
    IFileStorage storage,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<PerfilService> logger) : IPerfilService
{
    public const long TamanhoMaximoFoto = 1024 * 1024;

    // SVG fica de fora: pode conter script e o arquivo é servido pela própria API.
    private static readonly Dictionary<string, string> Formatos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
    };

    public async Task<UsuarioAutenticadoDto> ObterAsync(CancellationToken ct = default) =>
        UsuarioAutenticadoDto.From(await UsuarioAtualAsync(ct));

    public async Task<UsuarioAutenticadoDto> AtualizarAsync(AtualizarPerfilRequest request, CancellationToken ct = default)
    {
        var usuario = await UsuarioAtualAsync(ct);
        var email = request.Email.Trim().ToLowerInvariant();
        if (email != usuario.Email && await usuarios.EmailExisteIgnorandoTenantAsync(email, usuario.Id, ct))
            throw new ConflictException("Este e-mail já está em uso.");

        usuario.Nome = request.Nome.Trim();
        usuario.Email = email;
        usuario.Telefone = DadosBrasileiros.NormalizarTelefone(request.Telefone);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Perfil atualizado pelo usuário {UsuarioId}", usuario.Id);
        return UsuarioAutenticadoDto.From(usuario);
    }

    public async Task<UsuarioAutenticadoDto> EnviarFotoAsync(ArquivoUpload arquivo, CancellationToken ct = default)
    {
        if (!Formatos.TryGetValue(arquivo.ContentType, out var extensao))
            throw new ValidationException([new("arquivo", "Envie a foto em PNG, JPG ou WEBP.")]);
        if (arquivo.Tamanho <= 0 || arquivo.Tamanho > TamanhoMaximoFoto)
            throw new ValidationException([new("arquivo", "A foto deve ter até 1 MB.")]);

        var usuario = await UsuarioAtualAsync(ct);
        var anterior = usuario.FotoStorageKey;
        var chave = $"{usuario.TenantId:N}/usuarios/{usuario.Id:N}/foto-{Guid.NewGuid():N}{extensao}";

        await using (var stream = arquivo.AbrirLeitura())
            await storage.SaveAsync(chave, stream, arquivo.ContentType, ct);

        usuario.FotoStorageKey = chave;
        usuario.FotoContentType = arquivo.ContentType.ToLowerInvariant();
        usuario.FotoAtualizadaEm = Agora();
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            await ApagarAsync(chave);
            throw;
        }

        if (anterior != null) await ApagarAsync(anterior);
        return UsuarioAutenticadoDto.From(usuario);
    }

    public async Task<UsuarioAutenticadoDto> RemoverFotoAsync(CancellationToken ct = default)
    {
        var usuario = await UsuarioAtualAsync(ct);
        var anterior = usuario.FotoStorageKey;
        usuario.FotoStorageKey = null;
        usuario.FotoContentType = null;
        usuario.FotoAtualizadaEm = Agora();
        await unitOfWork.SaveChangesAsync(ct);
        if (anterior != null) await ApagarAsync(anterior);
        return UsuarioAutenticadoDto.From(usuario);
    }

    public async Task<FotoPerfilArquivo> ObterFotoAsync(CancellationToken ct = default)
    {
        var usuario = await UsuarioAtualAsync(ct);
        if (usuario.FotoStorageKey is null) throw new NotFoundException("Foto de perfil");
        return new FotoPerfilArquivo(await storage.OpenReadAsync(usuario.FotoStorageKey, ct),
            usuario.FotoContentType ?? "application/octet-stream");
    }

    private async Task<Usuario> UsuarioAtualAsync(CancellationToken ct)
    {
        var usuario = await usuarios.GetByIdIgnorandoTenantAsync(currentUser.RequireUserId(), ct);
        if (usuario is null || !usuario.Ativo) throw new UnauthorizedException();
        return usuario;
    }

    // O PostgreSQL guarda microssegundos: truncar mantém a versão igual antes e depois de salvar.
    private DateTime Agora()
    {
        var agora = clock.UtcNow;
        return new DateTime(agora.Ticks - agora.Ticks % 10, DateTimeKind.Utc);
    }

    private async Task ApagarAsync(string chave)
    {
        try { await storage.DeleteAsync(chave, CancellationToken.None); }
        catch (Exception ex) { logger.LogWarning(ex, "Falha ao remover foto de perfil {StorageKey}", chave); }
    }
}
